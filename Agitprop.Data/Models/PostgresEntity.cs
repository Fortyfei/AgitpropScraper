namespace Agitprop.Data.Models;

public class PostgresEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public required string Type { get; set; }
    public required string Source { get; set; }
    public string? WikidataId { get; set; }

    public ICollection<PostgresMention> Mentions { get; set; } = new List<PostgresMention>();
    public ICollection<PostgresEntityAlias> Aliases { get; set; } = new List<PostgresEntityAlias>();
}
