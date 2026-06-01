namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbRefreshTask
{
    public string Id { get; set; } = null!;

    public string? TriggersJson { get; set; }
    public string? AdditionalMetadataJson { get; set; }

    public string AssetInfoId { get; set; } = null!;
    public DbAssetInfo AssetInfo { get; set; } = null!;
}
