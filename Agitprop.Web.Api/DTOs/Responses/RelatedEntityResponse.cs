using Agitprop.Web.Api.Models;

namespace Agitprop.Web.Api.DTOs.Responses;

public class RelatedEntityResponse
{
    public string EntityId { get; set; }
    public List<EntityCoMentionDto> CoMentionedEntities { get; set; }
}
