using Agitprop.Core;

namespace Agitprop.Data;

public enum EntityResolutionKind
{
    Existing,
    Create,
    Rejected
}

public sealed record EntityResolution(
    string SurfaceForm,
    string NormalizedSurface,
    string Type,
    EntityResolutionKind Kind,
    Guid? EntityId,
    string CanonicalName,
    string MatchKind,
    bool TypeConflict,
    bool AliasAmbiguous);

public sealed record AliasCandidate(Guid EntityId, string EntityName, string EntityType, int Priority);

public readonly record struct EntityKey(string NormalizedName, string Type);

/// <summary>
/// Snapshot of the entities and aliases that can match a surface form. Built once per article so a
/// whole batch resolves against the same view without per-mention round trips.
/// </summary>
public sealed class EntityResolutionIndex
{
    private readonly Dictionary<EntityKey, Guid> _entities = new();
    private readonly Dictionary<Guid, string> _entityNames = new();
    private readonly Dictionary<EntityKey, List<AliasCandidate>> _typedAliases = new();
    private readonly Dictionary<string, List<AliasCandidate>> _anyAliases = new(StringComparer.Ordinal);

    public void AddEntity(Guid id, string name, string normalizedName, string type)
    {
        _entities[new EntityKey(normalizedName, type)] = id;
        _entityNames[id] = name;
    }

    public void AddAlias(Guid entityId, string entityName, string entityType, string normalizedAlias, string type, int priority)
    {
        var candidate = new AliasCandidate(entityId, entityName, entityType, priority);

        var key = new EntityKey(normalizedAlias, type);
        if (!_typedAliases.TryGetValue(key, out var typed))
        {
            typed = [];
            _typedAliases[key] = typed;
        }

        typed.Add(candidate);

        if (!_anyAliases.TryGetValue(normalizedAlias, out var any))
        {
            any = [];
            _anyAliases[normalizedAlias] = any;
        }

        any.Add(candidate);
    }

    public bool TryGetEntity(string normalizedName, string type, out Guid id) =>
        _entities.TryGetValue(new EntityKey(normalizedName, type), out id);

    public string GetName(Guid id) => _entityNames.TryGetValue(id, out var name) ? name : string.Empty;

    public IReadOnlyList<AliasCandidate> GetAliases(string normalizedAlias, string type) =>
        _typedAliases.TryGetValue(new EntityKey(normalizedAlias, type), out var candidates) ? candidates : [];

    public IReadOnlyList<AliasCandidate> GetAliases(string normalizedAlias) =>
        _anyAliases.TryGetValue(normalizedAlias, out var candidates) ? candidates : [];
}

/// <summary>
/// Decides which entity a surface form belongs to. Order: exact canonical name, typed alias, then a
/// single-candidate fallback across types (the NER model sometimes swaps LOC/ORG), otherwise a new
/// entity. The type is part of the lookup key, so a homograph such as the river Tisza and the Tisza
/// party never collapse into one row.
/// </summary>
public static class EntityResolver
{
    public static EntityResolution Resolve(NamedEntity mention, EntityResolutionIndex index)
    {
        var surfaceForm = mention.Name?.Trim() ?? string.Empty;
        var normalized = EntityNormalizer.Normalize(surfaceForm);
        var type = (mention.Type ?? string.Empty).Trim().ToUpperInvariant();

        if (type.Length == 0 || !EntityNormalizer.IsMeaningful(normalized))
        {
            return new EntityResolution(
                surfaceForm, normalized, type,
                EntityResolutionKind.Rejected, null, string.Empty, string.Empty, false, false);
        }

        if (index.TryGetEntity(normalized, type, out var entityId))
        {
            return new EntityResolution(
                surfaceForm, normalized, type,
                EntityResolutionKind.Existing, entityId, index.GetName(entityId), Models.EntityMatchKinds.Exact, false, false);
        }

        var typed = index.GetAliases(normalized, type);
        if (typed.Count > 0)
        {
            var chosen = Order(typed)[0];
            return new EntityResolution(
                surfaceForm, normalized, type,
                EntityResolutionKind.Existing, chosen.EntityId, chosen.EntityName, Models.EntityMatchKinds.Alias,
                TypeConflict: false, AliasAmbiguous: typed.Count > 1);
        }

        var distinct = index.GetAliases(normalized)
            .GroupBy(candidate => candidate.EntityId)
            .Select(group => Order(group.ToList())[0])
            .ToList();
        if (distinct.Count == 1)
        {
            var chosen = distinct[0];
            return new EntityResolution(
                surfaceForm, normalized, type,
                EntityResolutionKind.Existing, chosen.EntityId, chosen.EntityName, Models.EntityMatchKinds.Alias,
                TypeConflict: true, AliasAmbiguous: false);
        }

        if (distinct.Count > 1)
        {
            // The surface form belongs to several known entities and the model's type does not pick
            // one. Guessing would corrupt the mention, and a third entity would duplicate a subject,
            // so the mention is dropped instead.
            return new EntityResolution(
                surfaceForm, normalized, type,
                EntityResolutionKind.Rejected, null, string.Empty, string.Empty,
                TypeConflict: false, AliasAmbiguous: true);
        }

        return new EntityResolution(
            surfaceForm, normalized, type,
            EntityResolutionKind.Create, null, surfaceForm, Models.EntityMatchKinds.Created, false, false);
    }

    private static List<AliasCandidate> Order(IReadOnlyList<AliasCandidate> candidates) =>
        candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.EntityName, StringComparer.Ordinal)
            .ToList();
}
