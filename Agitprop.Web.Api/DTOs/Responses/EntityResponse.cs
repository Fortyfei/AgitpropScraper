namespace Agitprop.Web.Api.DTOs.Responses;

public class EntityResponse
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Type { get; set; } = string.Empty;
}
