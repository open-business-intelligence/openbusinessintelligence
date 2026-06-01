namespace OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

public class DbFilter
{
    public int RowId { get; set; }
    public string? OriginalId { get; set; }
    public string? IdColumn { get; set; }
    public int Function { get; set; }
    public string? FunctionName { get; set; }
    public string? LogicalOperator { get; set; }
    public string? ExpressionJson { get; set; }
    public bool IsGroup { get; set; }

    public string? ValuesJson { get; set; }
    public string? ChildrenJson { get; set; }

    public string? PageId { get; set; }
    public DbPage? Page { get; set; }

    public string? VisualId { get; set; }
    public DbVisual? Visual { get; set; }
}
