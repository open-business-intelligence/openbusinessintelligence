using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class AssetTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [McpServerTool(Name = "list_assets", ReadOnly = true)]
    [Description("Lists all OpenBI assets stored in the session (one row per AssetInfo.Id external id), ordered by name then id.")]
    public async Task<string> ListAssets(
        [Description("The session ID.")] string session_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<AssetTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var assets = await ctx.AssetInfos.AsNoTracking()
            .OrderBy(a => a.Name)
            .ThenBy(a => a.Id)
            .Select(a => new
            {
                id = a.Id,
                idSite = a.IdSite,
                name = a.Name,
                type = (AssetType)a.Type,
                externalType = a.ExternalType,
                description = a.Description,
                latestUpdate = a.LatestUpdate,
                latestUpdater = a.LatestUpdater
            })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { assets }, JsonOpts);
    }

    [McpServerTool(Name = "update_asset")]
    [Description("Updates name and/or description on a session OpenBI asset (AssetInfo.Id = asset_id). Null optional fields leave values unchanged.")]
    public async Task<string> UpdateAsset(
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<AssetTools> logger,
        [Description("The session ID.")] string session_id,
        [Description("OpenBI AssetInfo.Id of the asset to update.")] string asset_id,
        [Description("New asset name; null unchanged.")] string? name = null,
        [Description("New description; null unchanged.")] string? description = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var asset = await ctx.AssetInfos.FirstOrDefaultAsync(a => a.Id == asset_id, ct);
        if (asset is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Asset '{asset_id}' not found." });

        if (name is not null)
            asset.Name = name;

        if (description is not null)
            asset.Description = description;

        await ctx.SaveChangesAsync(ct);
        logger.LogInformation("update_asset: {AssetId}", asset_id);
        return JsonSerializer.Serialize(new { status = "Success" });
    }

    [McpServerTool(Name = "list_asset_dependencies", ReadOnly = true)]
    [Description("Lists direct asset-to-asset dependencies: each row means the dependent asset requires the referenced asset (X needs Y).")]
    public async Task<string> ListAssetDependencies(
        [Description("The session ID.")] string session_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<AssetTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var deps = await ctx.AssetDependencies.AsNoTracking().ToListAsync(ct);
        var nameById = await ctx.AssetInfos.AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var dependencies = deps.Select(d => new
        {
            dependentAssetId = d.DependentAssetId,
            dependsOnAssetId = d.DependsOnAssetId,
            dependentName = nameById.GetValueOrDefault(d.DependentAssetId),
            dependsOnName = nameById.GetValueOrDefault(d.DependsOnAssetId)
        }).ToList();

        return JsonSerializer.Serialize(new { dependencies }, JsonOpts);
    }
}
