using Agitprop.Core;
using Agitprop.Core.Enums;

namespace Agitprop.CLI.Services;

public interface IScrapeCommandOrchestrator
{
    Task<ArticleScrapeExecutionResult> ExecuteArticleAsync(ArticleScrapeRequest request, CancellationToken cancellationToken = default);

    Task<ArchiveSiteScrapeExecutionResult> ExecuteArchiveSiteAsync(ArchiveSiteScrapeRequest request, CancellationToken cancellationToken = default);

    Task<PublishExecutionResult> PublishArticlesAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default);
}

public sealed record ArticleScrapeRequest(string Url, bool ShortenOutput);

public sealed record ArticleScrapeExecutionResult(IReadOnlyList<string> OutputLines);

public sealed record ArchiveSiteScrapeRequest(NewsSites Site, DateOnly Date);

public sealed record ArchiveSiteScrapeExecutionResult(string SourceUrl, List<ScrapingJobDescription> Jobs);

public sealed record PublishArticlesRequest(List<ScrapingJobDescription> Articles, string? ConnectionString)
{
    public bool IsPublishingEnabled => !string.IsNullOrWhiteSpace(ConnectionString);
}

public sealed record PublishExecutionResult(bool Success, int PublishedCount, string? ErrorMessage, bool PublishingEnabled);
