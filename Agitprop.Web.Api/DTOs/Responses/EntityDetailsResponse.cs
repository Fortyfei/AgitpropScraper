namespace Agitprop.Api.Controllers;

public class EntityDetailsResponse
{
    public string EntityId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public int TotalMentions { get; set; }
}
