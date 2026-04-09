namespace Agitprop.Api.Controllers;

public class PaginatedEntitiesResponse
{
    public IEnumerable<EntityDto> Entities { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get => Entities.Count(); }
}
