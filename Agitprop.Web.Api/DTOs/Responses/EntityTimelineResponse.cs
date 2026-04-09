namespace Agitprop.Web.Api.DTOs.Responses;

public class EntityTimelineResponse
{
    public required string EntityId { get; set; }
    public required string Name { get; set; }
    public required List<EntityTimelinePoint> Timeline { get; set; }
}
