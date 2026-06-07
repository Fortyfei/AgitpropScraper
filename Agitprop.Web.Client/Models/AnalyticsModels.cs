namespace Agitprop.Web.Client.Models;

public class EntityBrowseItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int MentionCount { get; set; }
}

public class EntityBrowsePage
{
    public List<EntityBrowseItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class DomainStat
{
    public string Domain { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percent { get; set; }
}

public class EntitySummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Mentions { get; set; }
}

public class EntityInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class ArticleSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishedTime { get; set; }
}

public class ArticlePage
{
    public List<ArticleSummary> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class TimelinePoint
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}

public class EntityTrendSeries
{
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public List<TimelinePoint> Timeline { get; set; } = new();
}
