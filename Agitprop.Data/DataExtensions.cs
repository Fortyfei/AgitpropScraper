using Agitprop.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agitprop.Data;

public static class DataExtensions
{
    /// <summary>
    /// Adds the Newsfeed database services to the application builder.
    /// </summary>
    public static IHostApplicationBuilder AddNewsfeedDB(this IHostApplicationBuilder builder)
    {
        builder.AddPostgresConnection();
        builder.Services.AddTransient<INewsfeedDB, NewsfeedDB>();

        return builder;
    }

    /// <summary>
    /// Adds the Newsfeed repository services to the application builder.
    /// </summary>
    public static IHostApplicationBuilder AddNewsfeedRepositories(this IHostApplicationBuilder builder)
    {
        builder.AddPostgresConnection();
        builder.Services.AddTransient<IEntityRepository, EntityRepository>();
        builder.Services.AddTransient<IArticleFeedRepository, ArticleFeedRepository>();
        builder.Services.AddTransient<ITrendingRepository, TrendingRepository>();

        return builder;
    }

    private static IHostApplicationBuilder AddPostgresConnection(this IHostApplicationBuilder builder)
    {
        var conn = builder.Configuration.GetConnectionString("newsfeed");
        builder.Services.AddDbContext<AppDbContext>(opts =>
        {
            opts.UseNpgsql(conn, o => o.EnableRetryOnFailure());
            if (builder.Environment.IsDevelopment()) opts.EnableSensitiveDataLogging();
        });
        return builder;
    }
}
