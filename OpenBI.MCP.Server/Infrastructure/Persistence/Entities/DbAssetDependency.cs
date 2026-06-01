namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbAssetDependency
{
    public string DependentAssetId { get; set; } = null!;
    public string DependsOnAssetId { get; set; } = null!;

    public DbAssetInfo? DependentAsset { get; set; }
    public DbAssetInfo? DependsOnAsset { get; set; }
}
