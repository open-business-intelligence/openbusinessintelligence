using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.MCP.Server.Infrastructure.Persistence;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class MetadataTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "add_metadata")]
    [Description("Adds or updates a key/value metadata entry in AssetInfo.AdditionalMetadata for the given asset in the current session.")]
    public async Task<string> AddMetadata(
        [Description("Session id from create_session.")] string session_id,
        [Description("Asset id from list_assets (AssetInfo id in the session database).")] string asset_id,
        [Description("Metadata key to add/update.")] string metadata_name,
        [Description("Metadata value to set for the key.")] string metadata_value,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<MetadataTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        if (!Guid.TryParse(session_id, out var sessionGuid))
            return JsonSerializer.Serialize(new { status = "Error", message = "Invalid session_id." }, JsonOpts);

        if (string.IsNullOrWhiteSpace(asset_id))
            return JsonSerializer.Serialize(new { status = "Error", message = "asset_id is required." }, JsonOpts);

        if (string.IsNullOrWhiteSpace(metadata_name))
            return JsonSerializer.Serialize(new { status = "Error", message = "metadata_name is required." }, JsonOpts);

        await using var ctx = store.GetContext(sessionGuid);
        var assetId = asset_id.Trim();
        var key = metadata_name.Trim();

        var row = await ctx.AssetInfos.FirstOrDefaultAsync(a => a.Id == assetId, ct).ConfigureAwait(false);
        if (row is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"No asset found with id '{assetId}' in this session." }, JsonOpts);

        var deserialize = DeserializeMetadata(row.AdditionalMetadataJson, forUpdate: true);
        if (deserialize.ErrorJson is not null)
            return deserialize.ErrorJson;

        var metadata = deserialize.Metadata!;
        var existing = metadata.FirstOrDefault(m =>
            string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            metadata.Add(new AdditionalMetadata { Name = key, Value = metadata_value });
        }
        else
        {
            existing.Value = metadata_value;
            existing.Name = key;
        }

        row.AdditionalMetadataJson = JsonSerializer.Serialize(metadata, JsonOpts);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        logger.LogInformation(
            "add_metadata: session {SessionId} asset {AssetId} key {MetadataName}",
            sessionGuid, assetId, key);

        return JsonSerializer.Serialize(new
        {
            status = "Success",
            assetId,
            metadataName = key
        }, JsonOpts);
    }

    [McpServerTool(Name = "get_metadata_list", ReadOnly = true)]
    [Description("Returns metadata key names only for the given asset's AssetInfo.AdditionalMetadata in the current session.")]
    public async Task<string> GetMetadataList(
        [Description("Session id from create_session.")] string session_id,
        [Description("Asset id from list_assets (AssetInfo id in the session database).")] string asset_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<MetadataTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        if (!Guid.TryParse(session_id, out var sessionGuid))
            return JsonSerializer.Serialize(new { status = "Error", message = "Invalid session_id." }, JsonOpts);

        if (string.IsNullOrWhiteSpace(asset_id))
            return JsonSerializer.Serialize(new { status = "Error", message = "asset_id is required." }, JsonOpts);

        await using var ctx = store.GetContext(sessionGuid);
        var assetId = asset_id.Trim();

        var load = await LoadAssetMetadataReadOnlyAsync(ctx, assetId, ct).ConfigureAwait(false);
        if (load.ErrorJson is not null)
            return load.ErrorJson;

        var metadataKeys = CollectMetadataKeys(load.Metadata!);

        logger.LogInformation(
            "get_metadata_list: session {SessionId} asset {AssetId} count {Count}",
            sessionGuid, assetId, metadataKeys.Count);

        return JsonSerializer.Serialize(new { assetId, metadataKeys }, JsonOpts);
    }

    [McpServerTool(Name = "read_metadata", ReadOnly = true)]
    [Description("Returns the metadata value for a single key on the given asset's AssetInfo.AdditionalMetadata.")]
    public async Task<string> ReadMetadata(
        [Description("Session id from create_session.")] string session_id,
        [Description("Asset id from list_assets (AssetInfo id in the session database).")] string asset_id,
        [Description("Metadata key to read.")] string metadata_name,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<MetadataTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        if (!Guid.TryParse(session_id, out var sessionGuid))
            return JsonSerializer.Serialize(new { status = "Error", message = "Invalid session_id." }, JsonOpts);

        if (string.IsNullOrWhiteSpace(asset_id))
            return JsonSerializer.Serialize(new { status = "Error", message = "asset_id is required." }, JsonOpts);

        if (string.IsNullOrWhiteSpace(metadata_name))
            return JsonSerializer.Serialize(new { status = "Error", message = "metadata_name is required." }, JsonOpts);

        await using var ctx = store.GetContext(sessionGuid);
        var assetId = asset_id.Trim();
        var key = metadata_name.Trim();

        var load = await LoadAssetMetadataReadOnlyAsync(ctx, assetId, ct).ConfigureAwait(false);
        if (load.ErrorJson is not null)
            return load.ErrorJson;

        var entry = load.Metadata!.FirstOrDefault(m =>
            string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"Metadata key '{key}' not found on asset '{assetId}'."
            }, JsonOpts);
        }

        logger.LogInformation(
            "read_metadata: session {SessionId} asset {AssetId} key {MetadataName}",
            sessionGuid, assetId, entry.Name);

        return JsonSerializer.Serialize(new
        {
            assetId,
            metadataName = entry.Name,
            metadataValue = entry.Value
        }, JsonOpts);
    }

    private static async Task<(List<AdditionalMetadata>? Metadata, string? ErrorJson)> LoadAssetMetadataReadOnlyAsync(
        OpenBIDbContext ctx,
        string assetId,
        CancellationToken ct)
    {
        var row = await ctx.AssetInfos.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assetId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return (null, JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"No asset found with id '{assetId}' in this session."
            }, JsonOpts));
        }

        return DeserializeMetadata(row.AdditionalMetadataJson, forUpdate: false);
    }

    private static (List<AdditionalMetadata>? Metadata, string? ErrorJson) DeserializeMetadata(
        string? additionalMetadataJson,
        bool forUpdate)
    {
        try
        {
            var metadata = string.IsNullOrWhiteSpace(additionalMetadataJson)
                ? new List<AdditionalMetadata>()
                : JsonSerializer.Deserialize<List<AdditionalMetadata>>(additionalMetadataJson, JsonOpts) ??
                  new List<AdditionalMetadata>();

            return (metadata, null);
        }
        catch (JsonException)
        {
            var message = forUpdate
                ? "Asset metadata is not valid JSON and cannot be updated."
                : "Asset metadata is not valid JSON and cannot be read.";

            return (null, JsonSerializer.Serialize(new { status = "Error", message }, JsonOpts));
        }
    }

    private static List<string> CollectMetadataKeys(List<AdditionalMetadata> metadata)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new List<string>();

        foreach (var item in metadata)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                continue;

            if (!seen.Add(item.Name))
                continue;

            keys.Add(item.Name);
        }

        keys.Sort(StringComparer.OrdinalIgnoreCase);
        return keys;
    }
}
