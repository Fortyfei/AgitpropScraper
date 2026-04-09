using Agitprop.Core.Models;

namespace Agitprop.Web.Api;

internal static class Extensions
{
    internal static IEnumerable<EntityDto> ToEntityDtos(this IEnumerable<Entity> entities)
    {
        foreach (var entity in entities)
        {
            yield return entity.ToEntityDto();
        }
    }
    internal static EntityDto ToEntityDto(this Entity e)
    {
        return new EntityDto
        {
            Id = e.Id?.ToString() ?? "<empty>",
            Name = e.Name,
        };
    }

    internal static IEnumerable<ArticleDto> ToArticleDto(this IEnumerable<Article> articles)
    {
        foreach (var article in articles)
        {
            yield return article.ToArticleDto();
        }
    }
    internal static ArticleDto ToArticleDto(this Article a)
    {
        return new ArticleDto
        {
            Id = a.Id ?? "<empty>",
            Title=a.Title,
            ArticleUrl = a.Url,
            ArticlePublishedTime = a.PublishedTime,
        };
    }

}