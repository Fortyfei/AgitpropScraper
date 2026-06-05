namespace Agitprop.Web.Api.DTOs.Responses;

public class TopMentionedEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int MentionCount { get; set; }
}

public class TopMentionedEntitiesResponse
{
    public List<TopMentionedEntity> Entities { get; set; } = [];
}
