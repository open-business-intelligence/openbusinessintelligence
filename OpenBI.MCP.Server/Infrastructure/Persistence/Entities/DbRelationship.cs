namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbRelationship
{
    public int RowId { get; set; }
    public string? Name { get; set; }
    public string IdColumnFrom { get; set; } = null!;
    public string IdColumnTo { get; set; } = null!;
    public int Type { get; set; }
    public string? ExpressionJson { get; set; }

    public string? AdditionalMetadataJson { get; set; }

    public string AssetInfoId { get; set; } = null!;
    public DbAssetInfo AssetInfo { get; set; } = null!;
}
