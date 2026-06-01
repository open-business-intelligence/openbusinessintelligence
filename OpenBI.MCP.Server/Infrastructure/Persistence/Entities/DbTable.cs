namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbTable
{
    public string Id { get; set; } = null!;
    public string? Type { get; set; }
    public string Name { get; set; } = null!;
    public string? ExpressionJson { get; set; }

    public string? AdditionalMetadataJson { get; set; }

    public string AssetInfoId { get; set; } = null!;
    public DbAssetInfo AssetInfo { get; set; } = null!;

    public ICollection<DbColumn> Columns { get; set; } = new List<DbColumn>();
}
