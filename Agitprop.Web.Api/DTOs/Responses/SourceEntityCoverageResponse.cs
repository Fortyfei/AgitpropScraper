namespace Agitprop.Web.Api.DTOs.Responses;

public sealed class SourceEntityCoverageItem
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int ArticleCount { get; set; }
    public double PercentOfSourceArticles { get; set; }
}

public sealed class SourceCoverageItem
{
    public string Domain { get; set; } = string.Empty;
    public int ArticleCount { get; set; }
    public List<SourceEntityCoverageItem> Entities { get; set; } = [];
}

public sealed class SourceEntityCoverageResponse
{
    public List<SourceCoverageItem> Sources { get; set; } = [];
}
