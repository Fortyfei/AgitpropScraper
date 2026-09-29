namespace Agitprop.Core.Models
{
    public class Article
    {
        public string? Id { init; get; }
        public required string Title { init; get; }
        public required string Url { init; get; }
        public DateTime PublishedTime { init; get; }
        public List<Entity> MentionedEntities { init; get; } = [];
    }
}
