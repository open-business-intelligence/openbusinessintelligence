namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbAssetInfo
{
    public string Id { get; set; } = null!;
    public string? IdSite { get; set; }
    public string ExternalType { get; set; } = null!;
    public int Type { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public DateTime? LatestUpdate { get; set; }
    public string? LatestUpdater { get; set; }

    public string? AdditionalMetadataJson { get; set; }
    public string? RefreshTasksJson { get; set; }

    public ICollection<DbTable> Tables { get; set; } = new List<DbTable>();
    public ICollection<DbPage> Pages { get; set; } = new List<DbPage>();
    public ICollection<DbRelationship> Relationships { get; set; } = new List<DbRelationship>();
}
