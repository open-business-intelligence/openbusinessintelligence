namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbPage
{
    public string Id { get; set; } = null!;
    public string FriendlyName { get; set; } = null!;
    public int Order { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public bool IsEnabled { get; set; }
    public string? PageImagePath { get; set; }
    public string? Description { get; set; }

    public string? AdditionalMetadataJson { get; set; }

    public string AssetInfoId { get; set; } = null!;
    public DbAssetInfo AssetInfo { get; set; } = null!;

    public ICollection<DbVisual> Visuals { get; set; } = new List<DbVisual>();
    public ICollection<DbFilter> PageLevelFilters { get; set; } = new List<DbFilter>();
}
