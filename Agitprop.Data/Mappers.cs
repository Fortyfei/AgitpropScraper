using System;

using Agitprop.Core.Models;
using Agitprop.Data.Models;

namespace Agitprop.Data;

public static class Mappers
{
    public static Entity ToCoreModel(this PostgresEntity entity)
    {
        return new Entity
        {
            Id = entity.Id.ToString(),
            Name = entity.Name,
            Type = entity.Type,
            Aliases = entity.Aliases
                .Select(alias => alias.Alias)
                .Where(alias => !string.Equals(alias, entity.Name, StringComparison.Ordinal))
                .OrderBy(alias => alias, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public static IEnumerable<Entity> ToCoreModel(this IEnumerable<PostgresEntity> entities)
    {
        foreach (var entity in entities)
        {
            yield return entity.ToCoreModel();
        }
    }

    public static Article ToCoreModel(this PostgresArticle article)
    {
        return new Article
        {
            Id = article.Id.ToString(),
            Title = article.Title,
            Url = article.Url,
            PublishedTime = article.PublishedTime,
            MentionedEntities = article.Mentions?
                .Where(m => m.Entity != null)
                .Select(m => m.Entity!.ToCoreModel())
                .ToList() ?? new List<Entity>()
        };
    }

    public static IEnumerable<Article> ToCoreModel(this IEnumerable<PostgresArticle> articles)
    {
        foreach (var article in articles)
        {
            yield return article.ToCoreModel();
        }
    }
}
