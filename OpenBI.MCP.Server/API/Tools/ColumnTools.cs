using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.MCP.Server.Infrastructure.Persistence.Entities;
using OpenBI.MCP.Server.Infrastructure.Persistence.Mappers;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class ColumnTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "get_columns", ReadOnly = true)]
    [Description("Returns all columns for a given table.")]
    public async Task<string> GetColumns(
        [Description("The session ID.")] string session_id,
        [Description("The parent table ID.")] string table_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<ColumnTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbColumns = await ctx.Columns.AsNoTracking()
            .Where(c => c.TableId == table_id)
            .ToListAsync(ct);
        var columns = dbColumns.Select(OpenBIMapper.MapDbColumn).ToList();
        return JsonSerializer.Serialize(new { columns }, JsonOpts);
    }

    [McpServerTool(Name = "get_column", ReadOnly = true)]
    [Description("Returns a single column by ID.")]
    public async Task<string> GetColumn(
        [Description("The session ID.")] string session_id,
        [Description("The column ID.")] string column_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<ColumnTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbCol = await ctx.Columns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == column_id, ct);
        if (dbCol is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Column '{column_id}' not found." });

        return JsonSerializer.Serialize(OpenBIMapper.MapDbColumn(dbCol), JsonOpts);
    }

    [McpServerTool(Name = "create_column")]
    [Description("Creates a new column in the specified table. Returns the generated column ID.")]
    public async Task<string> CreateColumn(
        [Description("The session ID.")] string session_id,
        [Description("The parent table ID.")] string table_id,
        [Description("Column name.")] string name,
        [Description("Data type: String, Integer, Decimal, Date, Timestamp, Time, Boolean, Unknown.")] string data_type,
        [Description("Optional column type.")] string? type,
        [Description("Optional description.")] string? description,
        [Description("Optional DAX/M expression code.")] string? expression,
        [Description("Optional expression language (e.g. 'DAX', 'M', 'SQL').")] string? expression_language,
        [Description("Optional expression type (e.g. 'Measure', 'CalculatedColumn').")] string? expression_type,
        [Description("Whether this column is a dimension. Defaults to false.")] bool? is_dimension,
        [Description("Whether this column is a measure. Defaults to false.")] bool? is_measure,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<ColumnTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        if (!await ctx.Tables.AnyAsync(t => t.Id == table_id, ct))
            return JsonSerializer.Serialize(new { status = "Error", message = $"Table '{table_id}' not found." });

        if (!Enum.TryParse<Column.ColumnDataType>(data_type, true, out var dataTypeEnum))
            return JsonSerializer.Serialize(new { status = "Error", message = $"Invalid data type: '{data_type}'." });

        var columnId = Guid.NewGuid().ToString();
        var dbCol = new DbColumn
        {
            Id = columnId,
            Name = name,
            Type = type,
            Description = description,
            DataType = (int)dataTypeEnum,
            ExpressionJson = ToExpressionJson(expression, expression_language, expression_type),
            IsKey = false,
            IsUnique = false,
            IsNullable = false,
            IsDimension = is_dimension ?? false,
            IsMeasure = is_measure ?? false,
            TableId = table_id
        };

        ctx.Columns.Add(dbCol);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("create_column: {ColumnId} '{Name}' in table {TableId}", columnId, name, table_id);
        return JsonSerializer.Serialize(new { status = "Success", columnId });
    }

    [McpServerTool(Name = "update_column")]
    [Description("Updates a column in the session asset (via parent table). Null optional fields leave values unchanged; empty string clears nullable strings.")]
    public async Task<string> UpdateColumn(
         SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<ColumnTools> logger,
        [Description("The session ID.")] string session_id,
        [Description("OpenBI AssetInfo.Id; column's table must belong to this asset.")] string asset_id,
        [Description("The column ID to update.")] string id_column,
        [Description("New name; null unchanged.")] string? name = null,
        [Description("New data type (String, Integer, ...); null unchanged.")] string? data_type = null,
        [Description("New column type; null unchanged, empty clears.")] string? type = null,
        [Description("New description; null unchanged, empty clears.")] string? description = null,
        [Description("New expression code; null unchanged, empty clears.")] string? expression = null,
        [Description("New expression language; null unchanged.")] string? expression_language = null,
        [Description("New expression type; null unchanged.")] string? expression_type = null,
        [Description("New is_dimension; null unchanged.")] bool? is_dimension = null,
        [Description("New is_measure; null unchanged.")] bool? is_measure = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbCol = await ctx.Columns
            .Include(c => c.Table)
            .FirstOrDefaultAsync(c => c.Id == id_column && c.Table!.AssetInfoId == asset_id, ct);
        if (dbCol is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Column '{id_column}' not found for this asset." });

        if (name is not null)
            dbCol.Name = name;

        if (data_type is not null)
        {
            if (data_type.Length == 0)
                return JsonSerializer.Serialize(new { status = "Error", message = "data_type cannot be empty; omit or use a valid value." });
            if (!Enum.TryParse<Column.ColumnDataType>(data_type, true, out var dataTypeEnum))
                return JsonSerializer.Serialize(new { status = "Error", message = $"Invalid data type: '{data_type}'." });
            dbCol.DataType = (int)dataTypeEnum;
        }

        if (type is not null)
            dbCol.Type = type.Length == 0 ? null : type;

        if (description is not null)
            dbCol.Description = description.Length == 0 ? null : description;

        if (expression is not null)
            dbCol.ExpressionJson = expression.Length == 0
                ? null
                : ToExpressionJson(expression, expression_language, expression_type);

        if (is_dimension.HasValue)
            dbCol.IsDimension = is_dimension.Value;

        if (is_measure.HasValue)
            dbCol.IsMeasure = is_measure.Value;

        await ctx.SaveChangesAsync(ct);
        logger.LogInformation("update_column: {ColumnId}", id_column);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    [McpServerTool(Name = "delete_column")]
    [Description("Deletes a column by ID.")]
    public async Task<string> DeleteColumn(
        [Description("The session ID.")] string session_id,
        [Description("The column ID to delete.")] string column_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<ColumnTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbCol = await ctx.Columns.FirstOrDefaultAsync(c => c.Id == column_id, ct);
        if (dbCol is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Column '{column_id}' not found." });

        ctx.Columns.Remove(dbCol);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("delete_column: {ColumnId}", column_id);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    private static string? ToExpressionJson(string? code, string? language, string? type)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        return JsonSerializer.Serialize(
            new Expression { Id = Guid.NewGuid().ToString(), Code = code, Language = language, Type = type },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
