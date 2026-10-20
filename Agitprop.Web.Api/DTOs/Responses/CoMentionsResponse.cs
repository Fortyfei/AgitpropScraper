namespace Agitprop.Web.Api.DTOs.Responses;

public sealed class CoMentionItem
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int SharedArticleCount { get; set; }
}

public sealed class CoMentionsResponse
{
    public List<CoMentionItem> Entities { get; set; } = [];
}
