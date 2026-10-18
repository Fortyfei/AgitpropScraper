using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agitprop.Core;

/// <summary>
/// Identifies a news source. Comparison is case-insensitive; serializes as a plain string.
/// </summary>
[JsonConverter(typeof(SourceIdJsonConverter))]
public readonly record struct SourceId : IEquatable<SourceId>
{
    public SourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public bool Equals(SourceId other) =>
        string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator SourceId(string value) => new(value);
}

public sealed class SourceIdJsonConverter : JsonConverter<SourceId>
{
    public override SourceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("Source id must be a string."));

    public override void Write(Utf8JsonWriter writer, SourceId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
