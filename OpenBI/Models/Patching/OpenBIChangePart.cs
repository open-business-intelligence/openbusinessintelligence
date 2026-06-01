using System.Text.Json;

namespace OpenBI.Patching;

/// <summary>
/// Represents a single property change within an <see cref="OpenBIChange"/> Replace operation.
/// </summary>
public sealed class OpenBIChangePart
{
    /// <summary>camelCase property name (e.g. "name", "expression", "dataType").</summary>
    public required string Property { get; init; }

    /// <summary>JSON-serialized new value. Null clears the property.</summary>
    public string? ValueJson { get; init; }

    public static OpenBIChangePart For<T>(string property, T? value) =>
        new()
        {
            Property  = property,
            ValueJson = value is null ? null : JsonSerializer.Serialize(value)
        };
}
