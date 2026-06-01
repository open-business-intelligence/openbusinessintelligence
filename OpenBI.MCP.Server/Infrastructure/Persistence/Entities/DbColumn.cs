namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbColumn
{
    public string Id { get; set; } = null!;
    public string? Type { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int DataType { get; set; }
    public string? SummarizeBy { get; set; }
    public string? ExpressionJson { get; set; }
    public string? FormatString { get; set; }
    public string? DataCategory { get; set; }
    public bool IsKey { get; set; }
    public bool IsUnique { get; set; }
    public bool IsNullable { get; set; }
    public bool IsDimension { get; set; }
    public bool IsMeasure { get; set; }

    public string? ColumnsReferencesJson { get; set; }
    public string? AdditionalMetadataJson { get; set; }

    public string TableId { get; set; } = null!;
    public DbTable Table { get; set; } = null!;
}
