using System.Text.Json.Serialization;

namespace Agitprop.Core;

/// <summary>
/// Represents a named entity and its type.
/// </summary>
public class NamedEntity : IEquatable<NamedEntity>
{
    [JsonPropertyName("Item1")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Item2")]
    public string Type { get; set; } = "";

    public bool Equals(NamedEntity? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Type, other.Type, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as NamedEntity);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Name?.ToLowerInvariant() ?? string.Empty,
            Type?.ToLowerInvariant() ?? string.Empty
        );
    }

    public override string ToString() => $"{Name} ({Type})";
}

/// <summary>
/// Represents a collection of named entities.
/// </summary>
public record NamedEntityCollection
{
    [JsonPropertyName("entities")]
    public List<NamedEntity> Entities { get; set; } = [];

    public List<NamedEntity> PER => Entities.Where(e => e.Type.Equals("PER", StringComparison.OrdinalIgnoreCase)).ToList();
    public List<NamedEntity> LOC => Entities.Where(e => e.Type.Equals("LOC", StringComparison.OrdinalIgnoreCase)).ToList();
    public List<NamedEntity> ORG => Entities.Where(e => e.Type.Equals("ORG", StringComparison.OrdinalIgnoreCase)).ToList();
    public List<NamedEntity> MISC => Entities.Where(e => e.Type.Equals("MISC", StringComparison.OrdinalIgnoreCase)).ToList();

    public List<NamedEntity> All => Entities;
}
