namespace OpenBI.MCP.Server.Application.Platforms;

public sealed class BiVisualTypeDefinition
{
    public BiVisualObjectType ObjectType { get; init; }
    public string VisualType { get; init; } = "";
    public string? OpenBIVisualType { get; init; }
    public string? VisualDescription { get; init; }
    public string VisualTypeProjection { get; init; } = "";
    public string ProjectionDescription { get; init; } = "";
    public int Order { get; init; }
    public bool ProjectionAllowsMultipleValues { get; init; }
    /// <summary>Set when loaded from <c>platforms/{id}/visualtypes.json</c>.</summary>
    public string? PlatformId { get; init; }
}
