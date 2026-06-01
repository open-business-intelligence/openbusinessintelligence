using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenBI.Converters.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.Interfaces.Sites;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class DownloadAssetTool
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "download_asset")]
    [Description(
        "Downloads an asset artifact from the BI site by id/type, converts it to OpenBI format with the site converter, and imports it into the current session database.")]
    public async Task<string> DownloadAsset(
        [Description("Session id from create_session.")] string session_id,
        [Description("Site id from list_sites used for the remote download.")] string id_site,
        [Description("Remote external asset id to download from the BI site (same semantic as OpenBI AssetInfo.Id).")] string asset_id,
        [Description("Remote asset type (e.g. Report, SemanticModel, Dataflow, PaginatedReport).")] string asset_type,
        SessionStore store,
        SessionArtifactStore artifactStore,
        ISiteRegistry registry,
        SiteConverterFactoryActivator converterActivator,
        SiteConnectionSession siteConnections,
        ICurrentSessionManager currentSessionManager,
        ILogger<DownloadAssetTool> logger,
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

        if (string.IsNullOrWhiteSpace(site.SiteConverterFactoryName))
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = "siteConverterFactoryName is not configured for this site. Add it to the site JSON (assembly-qualified IOpenBIConverterFactory type)."
            }, JsonOpts);
        }

        if (string.IsNullOrWhiteSpace(asset_id))
            return JsonSerializer.Serialize(new { status = "Error", message = "asset_id is required." }, JsonOpts);

        if (string.IsNullOrWhiteSpace(asset_type))
            return JsonSerializer.Serialize(new { status = "Error", message = "asset_type is required." }, JsonOpts);

        var remoteAssetId = asset_id.Trim();
        var externalType = asset_type.Trim();

        IOpenBIConverterFactory converterFactory;
        try
        {
            converterFactory = converterActivator.GetCachedFactory(site.SiteConverterFactoryName);
        }
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new { status = "Error", message = ex.Message }, JsonOpts);
        }

        var converter = converterFactory.CreateOpenBIConverter(externalType);

        byte[] artifactBytes;
        try
        {
            var connection = await siteConnections.OpenConnectionAsync(site, ct).ConfigureAwait(false);
            artifactBytes = await connection.DownloadAssetArtifactAsync(remoteAssetId, externalType).ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Download not supported for site {IdSite}", site.IdSite);
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Download is not supported for this site connection: {ex.Message}"
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DownloadAssetArtifactAsync failed for site {IdSite}", site.IdSite);
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Download failed: {ex.Message}"
            }, JsonOpts);
        }

        if (artifactBytes is null || artifactBytes.Length == 0)
            return JsonSerializer.Serialize(new { status = "Error", message = "Downloaded artifact is empty." }, JsonOpts);

        Asset asset;
        try
        {
            asset = await converter.FromArtifactToOpenBIAsync(artifactBytes).ConfigureAwait(false);
            asset.IdSite = id_site;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FromArtifactToOpenBIAsync failed for remote asset {AssetId} type {AssetType}",
                remoteAssetId, externalType);
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Failed to convert downloaded artifact to OpenBI asset: {ex.Message}"
            }, JsonOpts);
        }

        // Cache artifact bytes so upload_asset can use the patch flow for this asset.
        if (!string.IsNullOrWhiteSpace(asset.Info?.Id))
            await artifactStore.SaveArtifactAsync(sessionGuid, asset.Info.Id, artifactBytes, ct).ConfigureAwait(false);

        await using var ctx = store.GetContext(sessionGuid);
        var graph = await OpenBIAssetGraphLoader.StageAssetGraphAsync(ctx, asset, ct).ConfigureAwait(false);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        logger.LogInformation(
            "download_asset: session {SessionId} remoteAsset {RemoteAssetId} type {AssetType} imported {Assets} assets",
            sessionGuid, remoteAssetId, externalType, graph.AssetInfos.Count);

        return JsonSerializer.Serialize(new
        {
            status = "Success",
            remoteAssetId,
            assetType = externalType,
            importedAssetId = asset.Info?.Id,
            importedAssetName = asset.Info?.Name,
            assets = graph.AssetInfos.Count,
            assetDependencies = graph.AssetDependencies.Count,
            tables = graph.Tables.Count,
            columns = graph.Columns.Count,
            pages = graph.Pages.Count,
            visuals = graph.Visuals.Count,
            relationships = graph.Relationships.Count,
            filters = graph.Filters.Count,
            refreshTasks = graph.RefreshTasks.Count,
            dataSourceConnections = graph.DataSourceConnections.Count
        }, JsonOpts);
    }
}
