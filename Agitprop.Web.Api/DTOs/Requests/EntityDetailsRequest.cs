namespace Agitprop.Api.Controllers;

public class EntityDetailsRequest
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
