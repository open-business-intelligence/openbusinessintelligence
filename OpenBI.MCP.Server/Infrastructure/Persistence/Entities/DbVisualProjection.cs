namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbVisualProjection
{
    public int RowId { get; set; }
    public string ProjectionName { get; set; } = null!;
    public string? ExpressionJson { get; set; }
    public bool IsActive { get; set; }
    public string? Type { get; set; }
    public bool IsDimension { get; set; }
    public bool IsMeasure { get; set; }
    public string? IdColumnReference { get; set; }
    public int Order { get; set; }

    public string VisualId { get; set; } = null!;
    public DbVisual Visual { get; set; } = null!;
}
