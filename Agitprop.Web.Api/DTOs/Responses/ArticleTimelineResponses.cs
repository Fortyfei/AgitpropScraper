namespace Agitprop.Web.Api.DTOs.Responses;

public sealed class FeedEntityItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public sealed class ArticleFeedItemResponse
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public DateTime PublishedTime { get; set; }
    public List<FeedEntityItem> Entities { get; set; } = [];
}

public sealed class ArticleFeedResponse
{
    public List<ArticleFeedItemResponse> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class DailyCountItem
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
    public bool IsSpike { get; set; }
}

public sealed class DailyCountsResponse
{
    public List<DailyCountItem> Days { get; set; } = [];
}

public sealed class FirstPublisherItem
{
    public string Domain { get; set; } = string.Empty;
    public DateTime FirstPublished { get; set; }
    public int ArticleCount { get; set; }
}

public sealed class FirstPublishersResponse
{
    public List<FirstPublisherItem> Publishers { get; set; } = [];
}
