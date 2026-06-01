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
public sealed class PageTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "get_pages", ReadOnly = true)]
    [Description("Returns all pages in the session's OpenBI asset.")]
    public async Task<string> GetPages(
        [Description("The session ID.")] string session_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<PageTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbPages = await ctx.Pages.AsNoTracking().OrderBy(p => p.Order).ToListAsync(ct);
        var pages = dbPages.Select(OpenBIMapper.MapDbPage).ToList();
        return JsonSerializer.Serialize(new { pages }, JsonOpts);
    }

    [McpServerTool(Name = "get_page", ReadOnly = true)]
    [Description("Returns a single page by ID, including its visuals and filters.")]
    public async Task<string> GetPage(
        [Description("The session ID.")] string session_id,
        [Description("The page ID.")] string page_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<PageTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbPage = await ctx.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == page_id, ct);
        if (dbPage is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Page '{page_id}' not found." });

        var page = OpenBIMapper.MapDbPage(dbPage);

        var dbVisuals = await ctx.Visuals.AsNoTracking().Where(v => v.PageId == page_id).ToListAsync(ct);
        var dbProjections = await ctx.VisualProjections.AsNoTracking()
            .Where(p => dbVisuals.Select(v => v.Id).Contains(p.VisualId))
            .ToListAsync(ct);
        var dbFilters = await ctx.Filters.AsNoTracking()
            .Where(f => f.PageId == page_id || dbVisuals.Select(v => v.Id).Contains(f.VisualId!))
            .ToListAsync(ct);

        page.PageLevelFilters = dbFilters
            .Where(f => f.PageId == page_id && f.VisualId == null)
            .Select(OpenBIMapper.MapDbFilter)
            .ToList();

        page.Visuals = BuildVisualTree(dbVisuals, null, dbProjections, dbFilters);

        return JsonSerializer.Serialize(page, JsonOpts);
    }

    [McpServerTool(Name = "create_page")]
    [Description("Creates a new page in one of the session's OpenBI assets. Returns the generated page ID.")]
    public async Task<string> CreatePage(
        [Description("The session ID.")] string session_id,
        [Description("Asset Id. Required and must be the exact id of one of the assets in session.")] string assetId,
        [Description("Display name for the page.")] string friendly_name,
        [Description("Page order (integer).")] int order,
        [Description("Page width.")] decimal width,
        [Description("Page height.")] decimal height,
        [Description("Whether the page is enabled. Defaults to true.")] bool? is_enabled,
        [Description("Optional description.")] string? description,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<PageTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var assetInfo = await ctx.AssetInfos.FirstOrDefaultAsync(x => x.Id == assetId, ct);
        if (assetInfo is null)
            return JsonSerializer.Serialize(new { status = "Error", message = "Specified asset with id not found." });

        var pageId = Guid.NewGuid().ToString();
        var dbPage = new DbPage
        {
            Id = pageId,
            FriendlyName = friendly_name,
            Order = order,
            Width = width,
            Height = height,
            IsEnabled = is_enabled ?? true,
            Description = description,
            AssetInfoId = assetId
        };

        ctx.Pages.Add(dbPage);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("create_page: {PageId} '{Name}'", pageId, friendly_name);
        return JsonSerializer.Serialize(new { status = "Success", pageId });
    }

    [McpServerTool(Name = "update_page")]
    [Description("Updates a page in the session asset. Null optional fields leave values unchanged; empty string clears nullable description.")]
    public async Task<string> UpdatePage(
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<PageTools> logger,
        [Description("The session ID.")] string session_id,
        [Description("OpenBI AssetInfo.Id; page must belong to this asset.")] string asset_id,
        [Description("The page ID to update.")] string id_page,
        [Description("New friendly name; null unchanged.")] string? friendly_name = null,
        [Description("New order; null unchanged.")] int? order = null,
        [Description("New width; null unchanged.")] decimal? width = null,
        [Description("New height; null unchanged.")] decimal? height = null,
        [Description("New is_enabled; null unchanged.")] bool? is_enabled = null,
        [Description("New description; null unchanged, empty clears.")] string? description = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbPage = await ctx.Pages.FirstOrDefaultAsync(
            p => p.Id == id_page && p.AssetInfoId == asset_id, ct);
        if (dbPage is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Page '{id_page}' not found for this asset." });

        if (friendly_name is not null)
            dbPage.FriendlyName = friendly_name;

        if (order.HasValue)
            dbPage.Order = order.Value;

        if (width.HasValue)
            dbPage.Width = width.Value;

        if (height.HasValue)
            dbPage.Height = height.Value;

        if (is_enabled.HasValue)
            dbPage.IsEnabled = is_enabled.Value;

        if (description is not null)
            dbPage.Description = description.Length == 0 ? null : description;

        await ctx.SaveChangesAsync(ct);
        logger.LogInformation("update_page: {PageId}", id_page);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    [McpServerTool(Name = "delete_page")]
    [Description("Deletes a page and all its visuals and filters.")]
    public async Task<string> DeletePage(
        [Description("The session ID.")] string session_id,
        [Description("The page ID to delete.")] string page_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<PageTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbPage = await ctx.Pages.FirstOrDefaultAsync(p => p.Id == page_id, ct);
        if (dbPage is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Page '{page_id}' not found." });

        ctx.Pages.Remove(dbPage);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("delete_page: {PageId}", page_id);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    private static List<Visual> BuildVisualTree(
        List<DbVisual> allVisuals,
        string? parentId,
        List<DbVisualProjection> allProjections,
        List<DbFilter> allFilters)
    {
        return allVisuals
            .Where(v => v.ParentVisualId == parentId)
            .Select(v =>
            {
                var visual = OpenBIMapper.MapDbVisual(v);
                visual.VisualProjections = allProjections
                    .Where(p => p.VisualId == v.Id)
                    .Select(OpenBIMapper.MapDbVisualProjection)
                    .ToList();
                visual.VisualLevelFilters = allFilters
                    .Where(f => f.VisualId == v.Id)
                    .Select(OpenBIMapper.MapDbFilter)
                    .ToList();
                visual.Children = BuildVisualTree(allVisuals, v.Id, allProjections, allFilters);
                return visual;
            }).ToList();
    }
}
