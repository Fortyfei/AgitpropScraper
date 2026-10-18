namespace Agitprop.Core;

/// <summary>
/// Represents the result of parsing content from a web page.
/// </summary>
public record class ContentParserResult
{
    private DateTime publishDate;

    public required SourceId SourceSite { get; init; }

    public required DateTime PublishDate 
    { 
        get => publishDate;
        init => publishDate = value.Kind == DateTimeKind.Unspecified 
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
    public required string Text { get; init; }
    public string Title { get; init; } = "";
}
