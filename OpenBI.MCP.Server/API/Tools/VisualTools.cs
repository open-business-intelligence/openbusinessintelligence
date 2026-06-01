using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.Interfaces.Sites;
using OpenBI.MCP.Server.Application.Platforms;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.MCP.Server.Infrastructure.Persistence.Entities;
using OpenBI.MCP.Server.Infrastructure.Persistence.Mappers;
using System.ComponentModel;
using System.Text.Json;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class VisualTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "get_visuals", ReadOnly = true)]
    [Description("Returns all top-level visuals for a given page.")]
    public async Task<string> GetVisuals(
        [Description("The session ID.")] string session_id,
        [Description("The parent page ID.")] string page_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbVisuals = await ctx.Visuals.AsNoTracking()
            .Where(v => v.PageId == page_id)
            .ToListAsync(ct);
        var visuals = dbVisuals.Select(OpenBIMapper.MapDbVisual).ToList();
        return JsonSerializer.Serialize(new { visuals }, JsonOpts);
    }

    [McpServerTool(Name = "get_visual", ReadOnly = true)]
    [Description("Returns a single visual by ID, including its projections and filters.")]
    public async Task<string> GetVisual(
        [Description("The session ID.")] string session_id,
        [Description("The visual ID.")] string visual_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbVisual = await ctx.Visuals.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visual_id, ct);
        if (dbVisual is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual '{visual_id}' not found." });

        var visual = OpenBIMapper.MapDbVisual(dbVisual);

        var dbProjections = await ctx.VisualProjections.AsNoTracking()
            .Where(p => p.VisualId == visual_id)
            .ToListAsync(ct);
        visual.VisualProjections = dbProjections.Select(OpenBIMapper.MapDbVisualProjection).ToList();

        var dbFilters = await ctx.Filters.AsNoTracking()
            .Where(f => f.VisualId == visual_id)
            .ToListAsync(ct);
        visual.VisualLevelFilters = dbFilters.Select(OpenBIMapper.MapDbFilter).ToList();

        var dbChildren = await ctx.Visuals.AsNoTracking()
            .Where(v => v.ParentVisualId == visual_id)
            .ToListAsync(ct);
        visual.Children = dbChildren.Select(OpenBIMapper.MapDbVisual).ToList();

        return JsonSerializer.Serialize(visual, JsonOpts);
    }

    [McpServerTool(Name = "create_visual")]
    [Description("Creates a new visual on a page (or as a child of another visual). Returns the generated visual ID.")]
    public async Task<string> CreateVisual(
        [Description("The session ID.")] string session_id,
        [Description("The parent page ID.")] string page_id,
        [Description("Display name.")] string friendly_name,
        [Description("Visual category: Static, Interaction, Container, Chart.")] string category,
        [Description("Visual type string (e.g. 'barChart', 'table', 'slicer').")] string visual_type,
        [Description("X position.")] decimal x,
        [Description("Y position.")] decimal y,
        [Description("Z index.")] decimal z,
        [Description("Width.")] decimal width,
        [Description("Height.")] decimal height,
        [Description("Optional description.")] string? description,
        [Description("Optional parent visual ID for nested visuals.")] string? parent_visual_id,
        ISiteRegistry siteRegistry,
        BiPlatformRegistry biPlatformRegistry,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));

        if (!Enum.TryParse<OpenBI.Models.VisualCategories>(category, true, out var cat))
            return JsonSerializer.Serialize(new { status = "Error", message = $"Invalid category: '{category}'." });

        RegisteredSite site = null;
        if(!siteRegistry.TryGet(currentSessionManager.Current.IdSite, out site))
            return JsonSerializer.Serialize(new { status = "Error", message = $"Cannot find current session site." });

        var openBIVisualType = ResolveOpenBIVisualType(site.IdPlatform, visual_type, biPlatformRegistry);
        var visualId = Guid.NewGuid().ToString();
        var dbVisual = new DbVisual
        {
            Id = visualId,
            FriendlyName = friendly_name,
            Category = (int)cat,
            Type = visual_type,
            OpenBIVisualType = openBIVisualType,
            X = x,
            Y = y,
            Z = z,
            Width = width,
            Height = height,
            Description = description,
            PageId = page_id,
            //ParentVisualId = parent_visual_id
        };

        ctx.Visuals.Add(dbVisual);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("create_visual: {VisualId} '{Name}' on page {PageId}", visualId, friendly_name, page_id);
        return JsonSerializer.Serialize(new { status = "Success", visualId });
    }

    [McpServerTool(Name = "update_visual")]
    [Description("Updates a visual whose page belongs to the session asset. Null optional fields leave values unchanged; empty string clears description. ParentVisualId is not used for asset scoping.")]
    public async Task<string> UpdateVisual(
        ISiteRegistry siteRegistry,
        BiPlatformRegistry biPlatformRegistry,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualTools> logger,
        [Description("The session ID.")] string session_id,
        [Description("OpenBI AssetInfo.Id; visual's page must belong to this asset.")] string asset_id,
        [Description("The visual ID to update.")] string id_visual,
        [Description("New friendly name; null unchanged.")] string? friendly_name = null,
        [Description("New category: Static, Interaction, Container, Chart; null unchanged.")] string? category = null,
        [Description("New visual_type; null unchanged.")] string? visual_type = null,
        [Description("New X; null unchanged.")] decimal? x = null,
        [Description("New Y; null unchanged.")] decimal? y = null,
        [Description("New Z; null unchanged.")] decimal? z = null,
        [Description("New width; null unchanged.")] decimal? width = null,
        [Description("New height; null unchanged.")] decimal? height = null,
        [Description("New description; null unchanged, empty clears.")] string? description = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbVisual = await ctx.Visuals
            .Include(v => v.Page)
            .FirstOrDefaultAsync(v => v.Id == id_visual, ct);
        if (dbVisual is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual '{id_visual}' not found." });

        if (dbVisual.PageId is null || dbVisual.Page is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual '{id_visual}' has no page; cannot verify asset." });

        if (dbVisual.Page.AssetInfoId != asset_id)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual '{id_visual}' not found for this asset." });

        if (friendly_name is not null)
            dbVisual.FriendlyName = friendly_name;

        if (category is not null)
        {
            if (!Enum.TryParse<OpenBI.Models.VisualCategories>(category, true, out var cat))
                return JsonSerializer.Serialize(new { status = "Error", message = $"Invalid category: '{category}'." });
            dbVisual.Category = (int)cat;
        }

        if (visual_type is not null)
        {
            dbVisual.Type = visual_type;

            RegisteredSite site = null;
            if (!siteRegistry.TryGet(currentSessionManager.Current.IdSite, out site))
                return JsonSerializer.Serialize(new { status = "Error", message = $"Cannot find current session site." });

            var openBIVisualType = ResolveOpenBIVisualType(site.IdPlatform, visual_type, biPlatformRegistry);

            dbVisual.OpenBIVisualType = openBIVisualType;
        }

        if (x.HasValue)
            dbVisual.X = x.Value;
        if (y.HasValue)
            dbVisual.Y = y.Value;
        if (z.HasValue)
            dbVisual.Z = z.Value;
        if (width.HasValue)
            dbVisual.Width = width.Value;
        if (height.HasValue)
            dbVisual.Height = height.Value;

        if (description is not null)
            dbVisual.Description = description.Length == 0 ? null : description;

        await ctx.SaveChangesAsync(ct);
        logger.LogInformation("update_visual: {VisualId}", id_visual);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    private static string ResolveOpenBIVisualType(string idBiPlatform, string visualType, BiPlatformRegistry biPlatformRegistry)
    {
        if (string.IsNullOrWhiteSpace(visualType))
            return "Unknown";

        if (!biPlatformRegistry.TryGetPlatform(idBiPlatform, out var entry) || entry is null)
            return "Unknown";

        var match = entry.VisualTypes
            .FirstOrDefault(v => string.Equals(v.VisualType, visualType, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(match?.OpenBIVisualType) ? "Unknown" : match.OpenBIVisualType;
    }

    [McpServerTool(Name = "delete_visual")]
    [Description("Deletes a visual by ID, including its projections, filters, and children.")]
    public async Task<string> DeleteVisual(
        [Description("The session ID.")] string session_id,
        [Description("The visual ID to delete.")] string visual_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<VisualTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbVisual = await ctx.Visuals.FirstOrDefaultAsync(v => v.Id == visual_id, ct);
        if (dbVisual is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Visual '{visual_id}' not found." });

        ctx.Visuals.Remove(dbVisual);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("delete_visual: {VisualId}", visual_id);
        return JsonSerializer.Serialize(new { status = "Success" });
    }
}
