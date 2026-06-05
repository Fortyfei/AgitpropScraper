namespace Agitprop.Web.Api.DTOs.Responses;

public class EntitiesTimelineResponse
{
    public Dictionary<string, List<EntityTimelinePoint>> Timeline { get; set; } = new();
}
