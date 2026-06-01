namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbVisual
{
    public string Id { get; set; } = null!;
    public string FriendlyName { get; set; } = null!;
    public int Category { get; set; }
    public string Type { get; set; } = null!;
    public string? OpenBIVisualType { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Z { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public string? Description { get; set; }

    public string? AdditionalMetadataJson { get; set; }

    public string? PageId { get; set; }
    public DbPage? Page { get; set; }

    public string? ParentVisualId { get; set; }
    public DbVisual? ParentVisual { get; set; }

    public ICollection<DbVisualProjection> VisualProjections { get; set; } = new List<DbVisualProjection>();
    public ICollection<DbFilter> VisualLevelFilters { get; set; } = new List<DbFilter>();
    public ICollection<DbVisual> Children { get; set; } = new List<DbVisual>();
}
