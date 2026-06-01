namespace OpenBI.Patching;

/// <summary>
/// Encodes and decodes the composite identity key used for <see cref="OpenBI.VisualProjection"/>
/// in patch operations.
/// <para>
/// Format: <c>{visualId}::{projectionName}::{order}</c>
/// </para>
/// <para>
/// VisualProjection has no stable single-field Id — its identity is the combination of the
/// containing visual's Id, its slot name, and its position within that slot.  The comparer
/// encodes this composite key into <see cref="OpenBIChange.Id"/> so that every converter
/// can locate the exact projection to Remove or Replace without needing to know the
/// parent visual independently.
/// </para>
/// </summary>
public static class VisualProjectionKey
{
    private const string Separator = "::";

    /// <summary>Encodes a composite VisualProjection key.</summary>
    public static string Encode(string visualId, string projectionName, int order)
        => $"{visualId}{Separator}{projectionName}{Separator}{order}";

    /// <summary>
    /// Decodes a composite VisualProjection key produced by <see cref="Encode"/>.
    /// Returns <c>false</c> when <paramref name="id"/> is null, empty, or malformed.
    /// </summary>
    public static bool TryDecode(
        string? id,
        out string visualId,
        out string projectionName,
        out int order)
    {
        visualId       = string.Empty;
        projectionName = string.Empty;
        order          = 0;

        if (string.IsNullOrEmpty(id))
            return false;

        var parts = id.Split(new[] { Separator }, 3, System.StringSplitOptions.None);
        if (parts.Length != 3 || !int.TryParse(parts[2], out order))
            return false;

        visualId       = parts[0];
        projectionName = parts[1];
        return true;
    }
}
