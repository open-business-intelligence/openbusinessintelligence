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
public sealed class VisualProjectionTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "get_visual_projections", ReadOnly = true)]
    [Description("Returns all visual projections for a given visual.")]
    public async Task<string> GetVisualProjections(
        [Description("The session ID.")] string session_id,
        [Description("The parent visual ID.")] string visual_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualProjectionTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbProjs = await ctx.VisualProjections.AsNoTracking()
            .Where(p => p.VisualId == visual_id)
            .OrderBy(p => p.Order)
            .ToListAsync(ct);

        var projections = dbProjs.Select(p => new
        {
            rowId = p.RowId,
            projection = OpenBIMapper.MapDbVisualProjection(p)
        }).ToList();

        return JsonSerializer.Serialize(new { projections }, JsonOpts);
    }

    [McpServerTool(Name = "get_visual_projection", ReadOnly = true)]
    [Description("Returns a single visual projection by its row ID.")]
    public async Task<string> GetVisualProjection(
        [Description("The session ID.")] string session_id,
        [Description("The projection row ID (integer).")] int projection_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualProjectionTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbProj = await ctx.VisualProjections.AsNoTracking()
            .FirstOrDefaultAsync(p => p.RowId == projection_id, ct);
        if (dbProj is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual projection {projection_id} not found." });

        return JsonSerializer.Serialize(OpenBIMapper.MapDbVisualProjection(dbProj), JsonOpts);
    }

    [McpServerTool(Name = "create_visual_projection")]
    [Description("Creates a new visual projection on a visual. Returns the generated row ID.")]
    public async Task<string> CreateVisualProjection(
        [Description("The session ID.")] string session_id,
        [Description("The parent visual ID.")] string visual_id,
        [Description("Projection name/role (e.g. 'Values', 'X', 'Legend', 'Category').")] string projection_name,
        [Description("Whether the projection is active.")] bool is_active,
        [Description("Sort order.")] int order,
        [Description("Optional expression code (e.g. DAX measure or column reference).")] string? expression,
        [Description("Optional expression language (e.g. 'DAX', 'M').")] string? expression_language,
        [Description("Optional expression type (e.g. 'Measure', 'ColumnReference').")] string? expression_type,
        [Description("Optional projection type.")] string? type,
        [Description("Whether the projection is a dimension.")] bool? is_dimension,
        [Description("Whether the projection is a measure.")] bool? is_measure,
        [Description("Optional referenced column ID.")] string? id_column_reference,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualProjectionTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        if (!await ctx.Visuals.AnyAsync(v => v.Id == visual_id, ct))
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual '{visual_id}' not found." });

        var dbProj = new DbVisualProjection
        {
            ProjectionName = projection_name,
            ExpressionJson = ToExpressionJson(expression, expression_language, expression_type),
            IsActive = is_active,
            Order = order,
            Type = type,
            IsDimension = is_dimension ?? false,
            IsMeasure = is_measure ?? false,
            IdColumnReference = id_column_reference,
            VisualId = visual_id
        };

        ctx.VisualProjections.Add(dbProj);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("create_visual_projection: {RowId} '{Name}' on visual {VisualId}",
            dbProj.RowId, projection_name, visual_id);
        return JsonSerializer.Serialize(new { status = "Success", projectionId = dbProj.RowId });
    }

    [McpServerTool(Name = "update_visual_projection")]
    [Description("Updates a visual projection by RowId; parent visual's page must belong to asset_id. Null optional fields unchanged; empty string clears nullable strings.")]
    public async Task<string> UpdateVisualProjection(
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualProjectionTools> logger,
        [Description("The session ID.")] string session_id,
        [Description("OpenBI AssetInfo.Id; projection's visual page must belong to this asset.")] string asset_id,
        [Description("Projection row ID (same as projection_id in get/delete).")] int id_visual_projection,
        [Description("New projection_name; null unchanged.")] string? projection_name = null,
        [Description("New expression code; null unchanged, empty clears.")] string? expression = null,
        [Description("New expression language; null unchanged.")] string? expression_language = null,
        [Description("New expression type; null unchanged.")] string? expression_type = null,
        [Description("New is_active; null unchanged.")] bool? is_active = null,
        [Description("New order; null unchanged.")] int? order = null,
        [Description("New type; null unchanged, empty clears.")] string? type = null,
        [Description("New is_dimension; null unchanged.")] bool? is_dimension = null,
        [Description("New is_measure; null unchanged.")] bool? is_measure = null,
        [Description("New id_column_reference; null unchanged, empty clears.")] string? id_column_reference = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbProj = await ctx.VisualProjections
            .Include(p => p.Visual)
            .ThenInclude(v => v.Page)
            .FirstOrDefaultAsync(p => p.RowId == id_visual_projection, ct);
        if (dbProj is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual projection {id_visual_projection} not found." });

        if (dbProj.Visual?.PageId is null || dbProj.Visual.Page is null)
            return JsonSerializer.Serialize(new { status = "Error", message = "Visual projection has no owning page; cannot verify asset." });

        if (dbProj.Visual.Page.AssetInfoId != asset_id)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual projection {id_visual_projection} not found for this asset." });

        if (projection_name is not null)
        {
            if (projection_name != dbProj.ProjectionName)
            {
                var nameTaken = await ctx.VisualProjections.AnyAsync(
                    p => p.VisualId == dbProj.VisualId && p.RowId != dbProj.RowId && p.ProjectionName == projection_name,
                    ct);
                if (nameTaken)
                    return JsonSerializer.Serialize(new { status = "Error", message = $"Another projection already uses name '{projection_name}'." });
            }

            dbProj.ProjectionName = projection_name;
        }

        if (expression is not null)
            dbProj.ExpressionJson = expression.Length == 0
                ? null
                : ToExpressionJson(expression, expression_language, expression_type);

        if (is_active.HasValue)
            dbProj.IsActive = is_active.Value;

        if (order.HasValue)
            dbProj.Order = order.Value;

        if (type is not null)
            dbProj.Type = type.Length == 0 ? null : type;

        if (is_dimension.HasValue)
            dbProj.IsDimension = is_dimension.Value;

        if (is_measure.HasValue)
            dbProj.IsMeasure = is_measure.Value;

        if (id_column_reference is not null)
            dbProj.IdColumnReference = id_column_reference.Length == 0 ? null : id_column_reference;

        await ctx.SaveChangesAsync(ct);
        logger.LogInformation("update_visual_projection: {RowId}", id_visual_projection);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    [McpServerTool(Name = "delete_visual_projection")]
    [Description("Deletes a visual projection by its row ID.")]
    public async Task<string> DeleteVisualProjection(
        [Description("The session ID.")] string session_id,
        [Description("The projection row ID to delete.")] int projection_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualProjectionTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbProj = await ctx.VisualProjections.FirstOrDefaultAsync(p => p.RowId == projection_id, ct);
        if (dbProj is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual projection {projection_id} not found." });

        ctx.VisualProjections.Remove(dbProj);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("delete_visual_projection: {RowId}", projection_id);
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
