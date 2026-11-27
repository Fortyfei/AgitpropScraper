namespace Agitprop.Data.Models;

public class PostgresMention
{
    public Guid ArticleId { get; set; }
    public PostgresArticle Article { get; set; } = null!;

    public Guid EntityId { get; set; }
    public PostgresEntity Entity { get; set; } = null!;

    public string? SurfaceForm { get; set; }
    public string? MatchKind { get; set; }
}
