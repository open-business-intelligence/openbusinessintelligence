using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenBI.Converters.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.Interfaces.Sites;
using OpenBI.MCP.Server.Infrastructure.Persistence;
using OpenBI.Patching;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class UploadAssetTool
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "upload_asset")]
        [Description(
        "Converts the OpenBI asset for the given session asset id to a platform artifact and uploads it to the BI site (workspace) via the site connection. Requires create_session, siteConverterFactoryName in site config, and folder_id (workspace id from get_folders). Optional id_asset is the external/OpenBI asset id used for update; omit for create.")]
    public async Task<string> UploadAsset(
        [Description("Session id from create_session.")] string session_id,
        [Description("Asset id from list_assets (AssetInfo id in the session database).")] string asset_id,
        [Description("Workspace / folder id from get_folders (SiteFolderInfo.Id). Required.")] string folder_id,
        SessionStore store,
        SessionArtifactStore artifactStore,
        ISiteRegistry registry,
        SiteConverterFactoryActivator converterActivator,
        SiteConnectionSession siteConnections,
        ICurrentSessionManager currentSessionManager,
        ILogger<UploadAssetTool> logger,
        IOpenBIAssetComparer comparer,
        [Description("External/OpenBI asset id for update; omit or null to create a new asset in the workspace.")]
        string? id_asset = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        if (!Guid.TryParse(session_id, out var sessionGuid))
            return JsonSerializer.Serialize(new { status = "Error", message = "Invalid session_id." }, JsonOpts);

        if (string.IsNullOrWhiteSpace(folder_id))
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = "folder_id is required (workspace id). Call get_folders for this session, choose a workspace, and pass its id as folder_id."
            }, JsonOpts);
        }

        if (string.IsNullOrWhiteSpace(asset_id))
            return JsonSerializer.Serialize(new { status = "Error", message = "asset_id is required." }, JsonOpts);

        await using var ctx = store.GetContext(sessionGuid);
        var asset = await SessionAssetLoader.LoadAssetAsync(ctx, asset_id.Trim(), ct).ConfigureAwait(false);
        if (asset is null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"No asset found with id '{asset_id}' in this session."
            }, JsonOpts);
        }

        if (string.IsNullOrWhiteSpace(asset.IdSite))
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = "Asset.IdSite is required for upload. Re-create or import the asset with idSite set."
            }, JsonOpts);
        }

        if (!registry.TryGet(asset.IdSite, out var site) || site is null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Asset site '{asset.IdSite}' is not registered. Call list_sites and verify the site configuration."
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

        var externalType = asset.Info?.ExternalType?.Trim();
        if (string.IsNullOrEmpty(externalType))
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = "Asset.Info.ExternalType is required for conversion."
            }, JsonOpts);
        }

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

        // ── Determine upload bytes: patch flow if artifact was previously downloaded, full convert otherwise ──
        byte[] artifactBytes;
        var storedArtifact = await artifactStore.TryLoadArtifactAsync(sessionGuid, asset_id.Trim(), ct).ConfigureAwait(false);

        if (storedArtifact is not null)
        {
            // PATCH FLOW — asset was downloaded this session; apply only the delta.
            logger.LogInformation("upload_asset: patch flow for asset {AssetId} (stored artifact {Bytes} bytes)", asset_id, storedArtifact.Length);

            OpenBI.Asset originalAsset;
            try
            {
                originalAsset = await converter.FromArtifactToOpenBIAsync(storedArtifact).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FromArtifactToOpenBIAsync failed during patch for asset {AssetId}", asset_id);
                return JsonSerializer.Serialize(new
                {
                    status = "Error",
                    message = $"Failed to parse stored artifact for comparison: {ex.Message}"
                }, JsonOpts);
            }

            var changes = comparer.Compare(originalAsset, asset);

            if (changes.Count == 0)
            {
                logger.LogInformation("upload_asset: no changes detected for asset {AssetId}, skipping upload", asset_id);
                return JsonSerializer.Serialize(new { status = "NoChanges", message = "Session asset is identical to the downloaded artifact. Nothing to upload." }, JsonOpts);
            }

            logger.LogInformation("upload_asset: {Count} changes detected for asset {AssetId}", changes.Count, asset_id);

            OpenBIPatchResult patchResult;
            try
            {
                patchResult = await converter.FromOpenBIPatchArtifactAsync(changes, storedArtifact).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FromOpenBIPatchArtifactAsync failed for asset {AssetId}", asset_id);
                return JsonSerializer.Serialize(new
                {
                    status = "Error",
                    message = $"Patch failed: {ex.Message}"
                }, JsonOpts);
            }

            if (!patchResult.IsSuccess)
            {
                var errors = patchResult.Errors.Select(e => new
                {
                    entity   = e.Entity.ToString(),
                    id       = e.Id,
                    property = e.Property,
                    op       = e.Op.ToString(),
                    message  = e.Message
                });
                return JsonSerializer.Serialize(new
                {
                    status  = "Error",
                    message = "One or more changes could not be applied to the artifact. No upload performed.",
                    errors
                }, JsonOpts);
            }

            artifactBytes = patchResult.Artifact;

            // Update the stored artifact so subsequent uploads diff against the latest version.
            await artifactStore.SaveArtifactAsync(sessionGuid, asset_id.Trim(), artifactBytes, ct).ConfigureAwait(false);
        }
        else
        {
            // FULL CONVERT FLOW — new asset, no prior download.
            logger.LogInformation("upload_asset: full convert flow for asset {AssetId}", asset_id);
            try
            {
                artifactBytes = await converter.FromOpenBIToArtifactAsync(asset).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FromOpenBIToArtifactAsync failed for asset {AssetId} externalType {ExternalType}", asset_id, externalType);
                return JsonSerializer.Serialize(new
                {
                    status = "Error",
                    message = $"Failed to convert OpenBI asset to artifact: {ex.Message}"
                }, JsonOpts);
            }

            if (artifactBytes is null || artifactBytes.Length == 0)
                return JsonSerializer.Serialize(new { status = "Error", message = "Converter produced an empty artifact." }, JsonOpts);
        }

        // ── Upload ─────────────────────────────────────────────────────────────────────────────────
        var connection = await siteConnections.OpenConnectionAsync(site, ct).ConfigureAwait(false);
        string? remoteId;
        try
        {
            // For the patch flow, fall back to asset.Info?.Id as the remote id when id_asset is not provided.
            var idRemote = id_asset;
            //var idRemote = !string.IsNullOrWhiteSpace(id_asset)
            //    ? id_asset.Trim()
            //    : (storedArtifact is not null ? asset.Info?.Id : null);

            remoteId = await connection
                .UploadAssetArtifactAsync(folder_id.Trim(), idRemote, artifactBytes)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Upload not supported for site {IdSite}", site.IdSite);
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Upload is not supported for this site connection: {ex.Message}"
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UploadAssetArtifactAsync failed for site {IdSite}", site.IdSite);
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Upload failed: {ex.Message}"
            }, JsonOpts);
        }

        logger.LogInformation(
            "upload_asset: session {SessionId} asset {AssetId} folder {FolderId} remoteId {RemoteId}",
            sessionGuid, asset_id, folder_id, remoteId);

        return JsonSerializer.Serialize(new
        {
            status = "Success",
            remoteAssetId = remoteId,
            folderId = folder_id.Trim()
        }, JsonOpts);
    }
}
