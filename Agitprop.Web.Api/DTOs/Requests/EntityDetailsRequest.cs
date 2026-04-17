namespace Agitprop.Api.Controllers;

public class EntityDetailsRequest
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
