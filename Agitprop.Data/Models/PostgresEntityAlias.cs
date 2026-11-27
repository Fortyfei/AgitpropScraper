namespace Agitprop.Data.Models;

public class PostgresEntityAlias
{
    public Guid Id { get; set; }

    public Guid EntityId { get; set; }
    public PostgresEntity Entity { get; set; } = null!;

    public required string Alias { get; set; }
    public required string NormalizedAlias { get; set; }
    public required string Type { get; set; }
    public int Priority { get; set; }
}
