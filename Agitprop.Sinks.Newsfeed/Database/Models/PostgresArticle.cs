namespace Agitprop.Sinks.Newsfeed.Database.Models;

public class PostgresArticle
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public DateTime PublishedTime { get; set; }

    public ICollection<PostgresMention> Mentions { get; set; } = new List<PostgresMention>();
}
