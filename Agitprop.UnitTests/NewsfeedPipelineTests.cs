using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Sinks.Newsfeed;
using Agitprop.Sources;
using Microsoft.Extensions.Configuration;

namespace Agitprop.UnitTests;

public class NewsfeedPipelineTests
{
    private FakeSpider _spider = null!;
    private FakeSink _defaultSink = null!;
    private NewsfeedPipeline _pipeline = null!;

    [SetUp]
    public void SetUp()
    {
        _spider = new FakeSpider();
        _defaultSink = new FakeSink();
        _pipeline = new NewsfeedPipeline(new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn()), _spider, _defaultSink);
    }

    [TestCase("https://origo.hu/article")]
    [TestCase("https://www.origo.hu/article")]
    [TestCase("https://WWW.Origo.HU/article")]
    public async Task Article_IsCrawledAsStaticTargetPage(string url)
    {
        await _pipeline.CrawlAsync(Job(url, PageContentType.Article));

        var job = _spider.Jobs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(job.Url, Is.EqualTo(url));
            Assert.That(job.PageCategory, Is.EqualTo(PageCategory.TargetPage));
            Assert.That(job.PageType, Is.EqualTo(PageType.Static));
            Assert.That(job.ContentParsers.Count(), Is.EqualTo(1));
            Assert.That(job.LinkParsers.Count(), Is.EqualTo(1));
            Assert.That(job.Pagination, Is.Null);
        });
    }

    [TestCase("https://telex.hu/archive", PageType.Static, false)]
    [TestCase("https://444.hu/archive", PageType.Dynamic, true)]
    public async Task Archive_IsCrawledAsPaginatedPage(string url, PageType pageType, bool hasActions)
    {
        await _pipeline.CrawlAsync(Job(url, PageContentType.Archive));

        var job = _spider.Jobs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(job.PageCategory, Is.EqualTo(PageCategory.PageWithPagination));
            Assert.That(job.PageType, Is.EqualTo(pageType));
            Assert.That(job.Pagination, Is.Not.Null);
            Assert.That(job.Actions?.Count ?? 0, Is.EqualTo(hasActions ? 1 : 0));
        });
    }

    [Test]
    public void UnknownHost_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(() =>
            _pipeline.CrawlAsync(Job("https://unknown.test/article", PageContentType.Article)));
        Assert.That(_spider.Jobs, Is.Empty);
    }

    [Test]
    public async Task UsesDefaultSink_UnlessOverridden()
    {
        var overrideSink = new FakeSink();

        await _pipeline.CrawlAsync(Job("https://telex.hu/a", PageContentType.Article));
        await _pipeline.CrawlAsync(Job("https://telex.hu/b", PageContentType.Article), overrideSink);

        Assert.That(_spider.Sinks, Is.EqualTo(new ISink[] { _defaultSink, overrideSink }));
    }

    [Test]
    public async Task ReturnsDiscoveredJobs_AndForwardsCancellation()
    {
        var discovered = Job("https://telex.hu/found", PageContentType.Article);
        _spider.Result = [discovered];
        using var cts = new CancellationTokenSource();

        var result = await _pipeline.CrawlAsync(Job("https://telex.hu/archive", PageContentType.Archive), null, cts.Token);

        Assert.That(result, Is.EqualTo(new[] { discovered }));
        Assert.That(_spider.Tokens.Single(), Is.EqualTo(cts.Token));
    }

    [Test]
    public void Catalog_ResolvesIdsAndHostsCaseInsensitively()
    {
        var catalog = new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn());

        Assert.Multiple(() =>
        {
            Assert.That(catalog.TryGetById("telex", out var byId), Is.True);
            Assert.That(byId.Id.Value, Is.EqualTo("Telex"));
            Assert.That(catalog.TryGetByHost("WWW.TELEX.HU", out _), Is.True);
            Assert.That(catalog.TryGetByHost("rss.rtl.hu", out _), Is.False);
            Assert.That(catalog.TryGetByFeedHost("rss.rtl.hu", out var feed), Is.True);
            Assert.That(feed.Id.Value, Is.EqualTo("RTL"));
            Assert.That(catalog.TryGetByFeedHost("origo.hu", out _), Is.True);
        });
    }

    [Test]
    public void Catalog_RejectsDuplicateHosts()
    {
        var sources = NewsfeedSources.BuiltIn().Append(new Alias("Other", "telex.hu"));

        Assert.Throws<ArgumentException>(() => new NewsfeedSourceCatalog(sources));
    }

    [Test]
    public void EnabledSources_AreFilteredByConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Newsfeed:Sources:Enabled:0"] = "telex",
                ["Newsfeed:Sources:Enabled:1"] = "Origo"
            })
            .Build();

        var enabled = NewsfeedSources.Enabled(configuration);

        Assert.That(enabled.Select(s => s.Id.Value), Is.EquivalentTo(new[] { "Telex", "Origo" }));
    }

    [Test]
    public void EnabledSources_WithUnknownId_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Newsfeed:Sources:Enabled:0"] = "nope" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => NewsfeedSources.Enabled(configuration));
    }

    [Test]
    public void BuiltInArchiveUrls_MatchExpectedShapes()
    {
        var catalog = new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn());
        var date = new DateOnly(2024, 3, 7);

        Assert.Multiple(() =>
        {
            Assert.That(Url(catalog, "Origo", date), Is.EqualTo("https://www.origo.hu/hirarchivum/2024/03/07"));
            Assert.That(Url(catalog, "Ripost", date), Is.EqualTo("https://ripost.hu/202403_sitemap.xml"));
            Assert.That(Url(catalog, "HVG", date), Is.EqualTo("https://www.hvg.hu/frisshirek/2024.03.07"));
            Assert.That(Url(catalog, "Telex", date), Is.EqualTo("https://telex.hu/sitemap/2024/03/07/news.xml"));
            Assert.That(Url(catalog, "Alfahir", date), Is.Null);
            Assert.That(Url(catalog, "RTL", date), Is.Null);
        });
    }

    private static string? Url(INewsfeedSourceCatalog catalog, string id, DateOnly date)
    {
        Assert.That(catalog.TryGetById(id, out var source), Is.True);
        return source.GetArchiveUrl(date);
    }

    private static NewsfeedJobDescrpition Job(string url, PageContentType type) => new() { Url = url, Type = type };

    private sealed class Alias(string id, string host) : INewsfeedSource
    {
        private readonly INewsfeedSource _inner = NewsfeedSources.BuiltIn().First();

        public SourceId Id { get; } = new(id);
        public IReadOnlyCollection<string> Hosts { get; } = [host];
        public IContentParser CreateContentParser() => _inner.CreateContentParser();
        public ILinkParser CreateLinkParser() => _inner.CreateLinkParser();
        public IPaginator CreatePaginator() => _inner.CreatePaginator();
    }

    private sealed class FakeSpider : ISpider
    {
        public List<ScrapingJob> Jobs { get; } = [];
        public List<ISink> Sinks { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public List<ScrapingJobDescription> Result { get; set; } = [];

        public Task<List<ScrapingJobDescription>> CrawlAsync(ScrapingJob job, ISink sink, CancellationToken cancellationToken = default)
        {
            Jobs.Add(job);
            Sinks.Add(sink);
            Tokens.Add(cancellationToken);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSink : ISink
    {
        public Task EmitAsync(string url, List<ContentParserResult> data, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> CheckPageAlreadyVisited(string url) => Task.FromResult(false);
    }
}
