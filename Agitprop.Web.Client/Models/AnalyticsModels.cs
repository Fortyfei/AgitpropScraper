namespace Agitprop.Web.Client.Models;

public class EntitySummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Mentions { get; set; }
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
