namespace Agitprop.Web.Api.DTOs.Requests;

public class EntitiesPaginatedRequest
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
}
