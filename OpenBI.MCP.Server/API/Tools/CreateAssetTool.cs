using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Platforms;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.Interfaces.Sites;
using OpenBI.MCP.Server.Infrastructure.Persistence.Entities;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class CreateAssetTool
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "create_asset")]
    [Description("Creates a new empty OpenBI asset row in the session SQLite database (AssetInfo). Use list_bi_platform_asset_types for asset_type_id. Call create_session first; load JSON from disk with load_openbi_asset instead when importing an existing asset.")]
    public async Task<string> CreateAsset(
        [Description("Session id from create_session.")] string session_id,
        [Description("Site id from list_sites (stored into the created asset IdSite).")] string id_site,
        [Description("Platform asset type id from list_bi_platform_asset_types (e.g. Report).")] string asset_type_id,
        [Description("Display name for the new asset.")] string name,
        [Description("Optional description.")] string? description,
        SessionStore store,
        ISiteRegistry registry,
        BiPlatformRegistry biPlatformRegistry,
        ICurrentSessionManager currentSessionManager,
        ILogger<CreateAssetTool> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        Guid sessionGuid;
        try
        {
            sessionGuid = Guid.Parse(session_id);
        }
        catch (Exception)
        {
            return JsonSerializer.Serialize(new { status = "Error", message = "Invalid session_id." }, JsonOpts);
        }

        if (!registry.TryGet(id_site, out var site) || site is null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Unknown or invalid id_site: {id_site}. Call list_sites first."
            }, JsonOpts);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return JsonSerializer.Serialize(new { status = "Error", message = "name is required." }, JsonOpts);
        }

        if (!BiPlatformAssetTypeResolution.TryResolve(site, biPlatformRegistry, asset_type_id, out var externalType,
                out var resolveError))
        {
            return JsonSerializer.Serialize(new { status = "Error", message = resolveError }, JsonOpts);
        }

        var assetId = Guid.NewGuid().ToString();
        var desc = description ?? string.Empty;

        await using var ctx = store.GetContext(sessionGuid);
        var row = new DbAssetInfo
        {
            Id = assetId,
            IdSite = id_site,
            Name = name.Trim(),
            Description = desc,
            ExternalType = externalType,
            Type = (int)AssetType.Report
        };
        ctx.AssetInfos.Add(row);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation(
            "create_asset: session {SessionId} asset {AssetId} ExternalType={ExternalType} name={Name}",
            sessionGuid, assetId, externalType, name);

        return JsonSerializer.Serialize(new
        {
            status = "Success",
            sessionId = sessionGuid.ToString(),
            assetId,
            idSite = row.IdSite,
            name = row.Name,
            externalType
        }, JsonOpts);
    }
}
