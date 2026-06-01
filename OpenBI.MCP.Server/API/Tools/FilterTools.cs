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
public sealed class FilterTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "get_filters", ReadOnly = true)]
    [Description("Returns all filters for a given page or visual.")]
    public async Task<string> GetFilters(
        [Description("The session ID.")] string session_id,
        [Description("Parent type: 'page' or 'visual'.")] string parent_type,
        [Description("The parent page ID or visual ID.")] string parent_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<FilterTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));

        IQueryable<DbFilter> query = parent_type.ToLowerInvariant() switch
        {
            "page" => ctx.Filters.AsNoTracking().Where(f => f.PageId == parent_id && f.VisualId == null),
            "visual" => ctx.Filters.AsNoTracking().Where(f => f.VisualId == parent_id),
            _ => throw new ArgumentException($"Invalid parent_type: '{parent_type}'. Must be 'page' or 'visual'.")
        };

        var dbFilters = await query.ToListAsync(ct);
        var filters = dbFilters.Select(f => new
        {
            rowId = f.RowId,
            filter = OpenBIMapper.MapDbFilter(f)
        }).ToList();

        return JsonSerializer.Serialize(new { filters }, JsonOpts);
    }

    [McpServerTool(Name = "get_filter", ReadOnly = true)]
    [Description("Returns a single filter by its row ID.")]
    public async Task<string> GetFilter(
        [Description("The session ID.")] string session_id,
        [Description("The filter row ID (integer).")] int filter_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<FilterTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbFilter = await ctx.Filters.AsNoTracking()
            .FirstOrDefaultAsync(f => f.RowId == filter_id, ct);
        if (dbFilter is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Filter {filter_id} not found." });

        return JsonSerializer.Serialize(OpenBIMapper.MapDbFilter(dbFilter), JsonOpts);
    }

    [McpServerTool(Name = "create_filter")]
    [Description("Creates a new filter on a page or visual. Returns the generated row ID.")]
    public async Task<string> CreateFilter(
        [Description("The session ID.")] string session_id,
        [Description("Parent type: 'page' or 'visual'.")] string parent_type,
        [Description("The parent page ID or visual ID.")] string parent_id,
        [Description("The column ID this filter applies to (optional for group filters).")] string? id_column,
        [Description("Filter function: OnlySelectedValues, ExceptSelectedValues, Expression.")] string function,
        [Description("Optional filter expression code.")] string? expression,
        [Description("Optional expression language (e.g. 'DAX', 'M').")] string? expression_language,
        [Description("Optional filter values as a JSON array of strings.")] string? values_json,
        [Description("Optional filter function name.")] string? function_name,
        [Description("Optional logical operator for groups: 'AND' or 'OR'.")] string? logical_operator,
        [Description("Whether this is a filter group. Defaults to false.")] bool? is_group,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<FilterTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));

        if (!Enum.TryParse<FilterFunctionType>(function, true, out var funcType))
            return JsonSerializer.Serialize(new { status = "Error", message = $"Invalid function: '{function}'." });

        string? pageId = null;
        string? visualId = null;

        switch (parent_type.ToLowerInvariant())
        {
            case "page":
                pageId = parent_id;
                break;
            case "visual":
                visualId = parent_id;
                break;
            default:
                return JsonSerializer.Serialize(new { status = "Error", message = $"Invalid parent_type: '{parent_type}'." });
        }

        var dbFilter = new DbFilter
        {
            IdColumn = id_column,
            Function = (int)funcType,
            FunctionName = function_name,
            LogicalOperator = logical_operator,
            ExpressionJson = ToExpressionJson(expression, expression_language),
            IsGroup = is_group ?? false,
            ValuesJson = values_json,
            PageId = pageId,
            VisualId = visualId
        };

        ctx.Filters.Add(dbFilter);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("create_filter: {RowId} on {ParentType} {ParentId}", dbFilter.RowId, parent_type, parent_id);
        return JsonSerializer.Serialize(new { status = "Success", filterId = dbFilter.RowId });
    }

    [McpServerTool(Name = "delete_filter")]
    [Description("Deletes a filter by its row ID.")]
    public async Task<string> DeleteFilter(
        [Description("The session ID.")] string session_id,
        [Description("The filter row ID to delete.")] int filter_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<FilterTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbFilter = await ctx.Filters.FirstOrDefaultAsync(f => f.RowId == filter_id, ct);
        if (dbFilter is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Filter {filter_id} not found." });

        ctx.Filters.Remove(dbFilter);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("delete_filter: {RowId}", filter_id);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    private static string? ToExpressionJson(string? code, string? language)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        return JsonSerializer.Serialize(
            new Expression { Id = Guid.NewGuid().ToString(), Code = code, Language = language },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
