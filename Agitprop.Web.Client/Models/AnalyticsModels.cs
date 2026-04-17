namespace Agitprop.Web.Client.Models;

public class EntitySummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Mentions { get; set; }
}

public class EntityTypeDistributionPoint
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TimelinePoint
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}

public class EntityDetailSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int TotalMentions { get; set; }
    public List<TimelinePoint> Trend { get; set; } = new();
}

public class RelatedEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int CoOccurrence { get; set; }
}

public class ArticleSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }
    public List<EntityLink> MentionedEntities { get; set; } = new();
    public string EntitiesDisplay => MentionedEntities.Count == 0
        ? string.Empty
        : string.Join(", ", MentionedEntities.Select(entity => entity.Name));
}

public class EntityLink
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class NetworkNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Mentions { get; set; }
}

public class NetworkEdge
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int Weight { get; set; }
}

public class EntityTrendSeries
{
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public List<TimelinePoint> Timeline { get; set; } = new();
}
