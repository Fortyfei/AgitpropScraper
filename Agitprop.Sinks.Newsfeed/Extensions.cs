using Microsoft.Extensions.Logging;
using Agitprop.Core.Interfaces;
using Agitprop.Data;
using Agitprop.Scraper.NLPService;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agitprop.Sinks.Newsfeed;

/// <summary>
/// Provides extension methods for configuring and converting newsfeed-related services and jobs.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds the Newsfeed Sink services to the application builder.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The updated host application builder.</returns>
    public static IHostApplicationBuilder AddNewsfeedSink(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<INamedEntityRecognizer, NamedEntityRecognizer>();

        builder.AddNewsfeedDB();

        builder.Services.AddTransient(sp =>
            new NewsfeedSink(
                sp.GetRequiredService<INamedEntityRecognizer>(),
                sp.GetRequiredService<INewsfeedDB>(),
                sp.GetRequiredService<ILogger<NewsfeedSink>>(),
                sp.GetRequiredService<IConfiguration>()));
        builder.Services.AddTransient<ISink>(sp => sp.GetRequiredService<NewsfeedSink>());

        return builder;
    }
}
