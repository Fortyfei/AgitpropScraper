namespace Agitprop.Core.Models;

public class Entity
{
    public string? Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public IReadOnlyList<string> Aliases { get; set; } = [];
}
