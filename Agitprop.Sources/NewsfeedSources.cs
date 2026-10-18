using Agitprop.Core.Interfaces;
using Agitprop.Sources.Sites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agitprop.Sources;

public static class NewsfeedSources
{
    /// <summary>All built-in sources, for callers that build the pipeline by hand.</summary>
    public static IReadOnlyList<INewsfeedSource> BuiltIn() =>
    [
        new OrigoSource(), new RipostSource(), new MandinerSource(), new MetropolSource(),
        new MagyarNemzetSource(), new AlfahirSource(), new HuszonnegyHuSource(), new NegyNegyNegySource(),
        new HvgSource(), new TelexSource(), new RtlSource(), new IndexSource(),         new MerceSource(),
                new BlikkSource(), new KontrollSource()    ];

    /// <summary>
    /// Built-in sources, filtered by the optional "Newsfeed:Sources:Enabled" array of source ids
    /// (all sources when absent).
    /// </summary>
    public static IReadOnlyList<INewsfeedSource> Enabled(IConfiguration configuration)
    {
        var enabled = configuration.GetSection("Newsfeed:Sources:Enabled").Get<string[]>();
        var all = BuiltIn();
        if (enabled is not { Length: > 0 }) return all;

        var unknown = enabled.Where(id => !all.Any(s => string.Equals(s.Id.Value, id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Unknown newsfeed source id(s) in configuration: {string.Join(", ", unknown)}");

        return all.Where(s => enabled.Contains(s.Id.Value, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    public static IHostApplicationBuilder AddNewsfeedSources(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<INewsfeedSourceCatalog>(_ => new NewsfeedSourceCatalog(Enabled(builder.Configuration)));
        builder.Services.AddTransient<INewsfeedPipeline>(sp => new NewsfeedPipeline(
            sp.GetRequiredService<INewsfeedSourceCatalog>(),
            sp.GetRequiredService<ISpider>(),
            sp.GetRequiredService<ISink>()));
        return builder;
    }
}
