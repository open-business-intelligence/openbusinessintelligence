using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenBI.Connectors.Interfaces.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.Interfaces.Sites;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class AssetQueryTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "query_site_assets", ReadOnly = true)]
    [Description("Queries site assets for the specified BI site. Optional filters: asset_type and folder_id.")]
    public async Task<string> QuerySiteAssets(
        [Description("The session ID returned by create_session.")] string session_id,
        [Description("Site id from list_sites. Required for cross-asset site queries.")] string id_site,
        SessionStore store,
        ISiteRegistry registry,
        SiteConnectionSession siteConnections,
        ICurrentSessionManager currentSessionManager,
        ILogger<AssetQueryTools> logger,
        [Description("Optional site asset type filter.")] string? asset_type = null,
        [Description("Optional folder/workspace id filter.")] string? folder_id = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        if (!Guid.TryParse(session_id, out var sessionGuid))
            return JsonSerializer.Serialize(new { status = "Error", message = "Invalid session_id." }, JsonOpts);

        using var _ = store.GetContext(sessionGuid);

        if (!registry.TryGet(id_site, out var site) || site is null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Unknown or invalid id_site: {id_site}. Call list_sites first."
            }, JsonOpts);
        }

        try
        {
            var conn = await siteConnections.OpenConnectionAsync(site, ct).ConfigureAwait(false);
            var request = new QueryAssetsParameters
            {
                AssetType = string.IsNullOrWhiteSpace(asset_type) ? null : asset_type.Trim(),
                FolderId = string.IsNullOrWhiteSpace(folder_id) ? null : folder_id.Trim()
            };

            var assets = await conn.QuerySiteAssetsAsync(request).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { status = "Success", assets }, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "query_site_assets failed for site {IdSite}", id_site);
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Query site assets failed: {ex.Message}"
            }, JsonOpts);
        }
    }
}
