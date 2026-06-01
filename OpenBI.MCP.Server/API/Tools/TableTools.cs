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
public sealed class TableTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "get_tables", ReadOnly = true)]
    [Description("Returns all tables in the session's OpenBI asset.")]
    public async Task<string> GetTables(
        [Description("The session ID.")] string session_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<TableTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbTables = await ctx.Tables.AsNoTracking().ToListAsync(ct);
        var tables = dbTables.Select(OpenBIMapper.MapDbTable).ToList();
        return JsonSerializer.Serialize(new { tables }, JsonOpts);
    }

    [McpServerTool(Name = "get_table", ReadOnly = true)]
    [Description("Returns a single table by ID, including its columns.")]
    public async Task<string> GetTable(
        [Description("The session ID.")] string session_id,
        [Description("The table ID.")] string table_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<TableTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbTable = await ctx.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == table_id, ct);
        if (dbTable is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Table '{table_id}' not found." });

        var table = OpenBIMapper.MapDbTable(dbTable);
        var dbColumns = await ctx.Columns.AsNoTracking().Where(c => c.TableId == table_id).ToListAsync(ct);
        table.Columns = dbColumns.Select(OpenBIMapper.MapDbColumn).ToList();

        return JsonSerializer.Serialize(table, JsonOpts);
    }

    [McpServerTool(Name = "create_table")]
    [Description("Creates a new table in the session's OpenBI asset. Returns the generated table ID.")]
    public async Task<string> CreateTable(
        [Description("The session ID.")] string session_id,
        [Description("Asset Id. Required and must be the exact id of one of the assets in session.")] string assetId,
        [Description("The table name.")] string name,
        [Description("The table type: 'Table' or 'Object'. Defaults to 'Table'.")] string? type,
        [Description("Optional source expression code for the table (e.g. M query, SQL).")] string? source_expression,
        [Description("Optional expression language (e.g. 'M', 'SQL', 'DAX').")] string? expression_language,
        [Description("Optional expression type (e.g. 'SourceQuery', 'Calculated').")] string? expression_type,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<TableTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var assetInfo = await ctx.AssetInfos.FirstOrDefaultAsync(x => x.Id == assetId, ct);
        if (assetInfo is null)
            return JsonSerializer.Serialize(new { status = "Error", message = "Specified asset with id not found." });

        var tableId = Guid.NewGuid().ToString();
        var dbTable = new DbTable
        {
            Id = tableId,
            Name = name,
            Type = type ?? Table.TableTypeTable,
            ExpressionJson = ToExpressionJson(source_expression, expression_language, expression_type),
            AssetInfoId = assetInfo.Id
        };

        ctx.Tables.Add(dbTable);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("create_table: {TableId} '{Name}'", tableId, name);
        return JsonSerializer.Serialize(new { status = "Success", tableId });
    }

    [McpServerTool(Name = "update_table")]
    [Description("Updates a table in the session asset. Omit optional fields to leave unchanged; for nullable strings, pass empty string to clear.")]
    public async Task<string> UpdateTable(
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<TableTools> logger,
        [Description("The session ID.")] string session_id,
        [Description("OpenBI AssetInfo.Id; table must belong to this asset.")] string asset_id,
        [Description("The table ID to update.")] string id_table,
        [Description("New table name; null to leave unchanged.")] string? name = null,
        [Description("New type 'Table' or 'Object'; null unchanged, empty string clears.")] string? type = null,
        [Description("New source expression code; null unchanged, empty string clears.")] string? source_expression = null,
        [Description("New expression language (e.g. 'M', 'SQL'); null unchanged.")] string? expression_language = null,
        [Description("New expression type; null unchanged.")] string? expression_type = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbTable = await ctx.Tables.FirstOrDefaultAsync(
            t => t.Id == id_table && t.AssetInfoId == asset_id, ct);
        if (dbTable is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Table '{id_table}' not found for this asset." });

        if (name is not null)
            dbTable.Name = name;

        if (type is not null)
        {
            if (type.Length == 0)
                dbTable.Type = null;
            else if (type != Table.TableTypeTable && type != Table.TableTypeObject)
                return JsonSerializer.Serialize(new { status = "Error", message = $"Invalid table type: '{type}'." });
            else
                dbTable.Type = type;
        }

        if (source_expression is not null)
            dbTable.ExpressionJson = source_expression.Length == 0
                ? null
                : ToExpressionJson(source_expression, expression_language, expression_type);

        await ctx.SaveChangesAsync(ct);
        logger.LogInformation("update_table: {TableId}", id_table);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    [McpServerTool(Name = "delete_table")]
    [Description("Deletes a table and all its columns from the session's OpenBI asset.")]
    public async Task<string> DeleteTable(
        [Description("The session ID.")] string session_id,
        [Description("The table ID to delete.")] string table_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<TableTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbTable = await ctx.Tables.FirstOrDefaultAsync(t => t.Id == table_id, ct);
        if (dbTable is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Table '{table_id}' not found." });

        ctx.Tables.Remove(dbTable);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("delete_table: {TableId}", table_id);
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
