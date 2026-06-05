namespace Agitprop.Web.Api.DTOs.Responses;

public class ArticleDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public DateTime PublishedTime { get; set; }
}

public class EntityArticlesResponse
{
    public List<ArticleDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
