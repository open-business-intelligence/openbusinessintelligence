namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbDataSourceConnection
{
    public int RowId { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string? ParametersJson { get; set; }

    public string AssetInfoId { get; set; } = null!;
    public DbAssetInfo AssetInfo { get; set; } = null!;
}
