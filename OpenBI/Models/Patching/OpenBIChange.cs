using System.Collections.Generic;
using System.Text.Json;

namespace OpenBI.Patching;

/// <summary>
/// Represents a single change to apply to an OpenBI artifact during a patch operation.
/// <list type="bullet">
///   <item><b>Add</b>: Id is null; ParentId identifies the container for nested entities; ValueJson holds the full serialized object.</item>
///   <item><b>Remove</b>: Id identifies the target; no Parts or ValueJson needed.</item>
///   <item><b>Replace</b>: Id identifies the target; Parts contains one entry per changed property.</item>
/// </list>
/// </summary>
public sealed class OpenBIChange
{
    public required OpenBIEntity Entity { get; init; }

    /// <summary>
    /// Entity key. Null for Add.
    /// For entities without an Id field, contains the natural key (e.g. Name for Relationship,
    /// DataSourceConnection; ProjectionName for VisualProjection).
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Id of the parent container. Required for Add on nested entities
    /// (e.g. Column → TableId, Visual → PageId, VisualProjection → VisualId).
    /// </summary>
    public string? ParentId { get; init; }

    public required OpenBIChangeOp Op { get; init; }

    /// <summary>Full JSON-serialized object. Used only for Add operations.</summary>
    public string? ValueJson { get; init; }

    /// <summary>Changed properties. Used only for Replace operations.</summary>
    public IReadOnlyList<OpenBIChangePart> Parts { get; init; } = Array.Empty<OpenBIChangePart>();

    // ─── Factory methods ───────────────────────────────────────────────────────

    /// <summary>Creates an Add change for a top-level entity (Page, Table, RefreshTask, DataSourceConnection).</summary>
    public static OpenBIChange Add<T>(OpenBIEntity entity, T value) =>
        new()
        {
            Entity    = entity,
            Op        = OpenBIChangeOp.Add,
            ValueJson = JsonSerializer.Serialize(value)
        };

    /// <summary>Creates an Add change for a nested entity (Column → parentId=TableId, Visual → parentId=PageId, etc.).</summary>
    public static OpenBIChange Add<T>(OpenBIEntity entity, string parentId, T value) =>
        new()
        {
            Entity    = entity,
            Op        = OpenBIChangeOp.Add,
            ParentId  = parentId,
            ValueJson = JsonSerializer.Serialize(value)
        };

    /// <summary>Creates a Remove change.</summary>
    public static OpenBIChange Remove(OpenBIEntity entity, string id) =>
        new()
        {
            Entity = entity,
            Id     = id,
            Op     = OpenBIChangeOp.Remove
        };

    /// <summary>Creates a Replace change with one or more property changes.</summary>
    public static OpenBIChange Replace(OpenBIEntity entity, string id, IReadOnlyList<OpenBIChangePart> parts) =>
        new()
        {
            Entity = entity,
            Id     = id,
            Op     = OpenBIChangeOp.Replace,
            Parts  = parts
        };
}
