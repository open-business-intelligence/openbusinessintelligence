namespace OpenBI.MCP.Server.Application.Platforms;

/// <summary>
/// One compilable asset kind for a platform, backed by <c>platforms/{platformId}/assetTypes/{id}.md</c>.
/// </summary>
public sealed class BiPlatformAssetTypeInfo
{
    /// <summary>File stem (e.g. <c>Report</c> from <c>Report.md</c>).</summary>
    public required string Id { get; init; }

    /// <summary>First Markdown <c>#</c> heading in the file, if any.</summary>
    public string? Title { get; init; }

    /// <summary>Absolute path to the Markdown file; server-only, not exposed to MCP.</summary>
    public required string InstructionsPath { get; init; }
}
