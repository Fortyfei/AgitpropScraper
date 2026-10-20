namespace Agitprop.Core.Models;

public sealed record EntityCoMention(
    string EntityId,
    string EntityName,
    string EntityType,
    int SharedArticleCount);
