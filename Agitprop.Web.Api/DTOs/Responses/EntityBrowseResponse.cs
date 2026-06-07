namespace Agitprop.Web.Api.DTOs.Responses;

public class EntityBrowseResponse
{
    public List<EntityBrowseItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class EntityBrowseItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int MentionCount { get; set; }
}
