namespace Agitprop.Sinks.Newsfeed.Database.Models;

public class PostgresEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }

    public ICollection<PostgresMention> Mentions { get; set; } = new List<PostgresMention>();
}
