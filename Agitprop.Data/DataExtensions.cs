using Agitprop.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agitprop.Data;

public static class DataExtensions
{
    private const string ConnectionName = "newsfeed";
    private const string PostgresRegistrationKey = "Agitprop.Data.PostgresRegistration";

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
        if (builder.Properties.ContainsKey(PostgresRegistrationKey))
        {
            return builder;
        }

        builder.AddNpgsqlDbContext<AppDbContext>(ConnectionName);
        builder.Properties[PostgresRegistrationKey] = PostgresRegistrationKey;

        return builder;
    }
}
