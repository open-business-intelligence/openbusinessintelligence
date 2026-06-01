namespace OpenBI.MCP.Server.Application.Platforms;

public sealed class BiPlatformEntry
{
    public required BiPlatformInfo Info { get; init; }
    public required IReadOnlyList<BiVisualTypeDefinition> VisualTypes { get; init; }
    public required IReadOnlyList<BiPlatformAssetTypeInfo> AssetTypes { get; init; }
}
