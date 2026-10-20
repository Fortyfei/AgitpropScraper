namespace Agitprop.Core.Models;

public sealed record SourceEntityCoverage(
    string Domain,
    int ArticleCount,
    IReadOnlyList<EntityCoverageStat> Entities);

public sealed record EntityCoverageStat(
    string EntityId,
    string EntityName,
    string EntityType,
    int ArticleCount,
    double PercentOfSourceArticles);
