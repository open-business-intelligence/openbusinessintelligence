using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using OpenBI.MCP.Server.Application.Platforms;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class BiPlatformTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [McpServerTool(Name = "list_bi_platforms", ReadOnly = true)]
    [Description("Lists BI platforms registered under platforms/*/info.json (id and display name). Use list_bi_platform_visual_types with a platform id for visual catalog details.")]
    public Task<string> ListBiPlatforms(
        BiPlatformRegistry registry,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var platforms = registry.AllPlatforms.Select(p => new { id = p.Id, name = p.Name }).ToList();
        return Task.FromResult(JsonSerializer.Serialize(platforms, JsonOpts));
    }

    [McpServerTool(Name = "list_bi_platform_visual_types", ReadOnly = true)]
    [Description("Returns the visual type catalog for one BI platform (from platforms/{platform_id}/visualtypes.json). platform_id must match info.json id (e.g. from list_bi_platforms).")]
    public Task<string> ListBiPlatformVisualTypes(
        [Description("Platform id from list_bi_platforms / platforms/*/info.json id field.")] string platform_id,
        BiPlatformRegistry registry,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(platform_id) || !registry.TryGetPlatform(platform_id, out var entry) || entry is null)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = $"Unknown or invalid platform_id: {platform_id}. Call list_bi_platforms first."
            }, JsonOpts));
        }

        var visualTypes = entry.VisualTypes.Select(v => new
        {
            objectType = v.ObjectType,
            visualType = v.VisualType,
            openBIVisualType = v.OpenBIVisualType,
            visualDescription = v.VisualDescription,
            visualTypeProjection = v.VisualTypeProjection,
            projectionDescription = v.ProjectionDescription,
            order = v.Order,
            projectionAllowsMultipleValues = v.ProjectionAllowsMultipleValues,
            platformId = v.PlatformId
        }).ToList();

        var payload = new
        {
            platformId = entry.Info.Id,
            platformName = entry.Info.Name,
            visualTypes
        };

        return Task.FromResult(JsonSerializer.Serialize(payload, JsonOpts));
    }

    [McpServerTool(Name = "list_bi_platform_asset_types", ReadOnly = true)]
    [Description("Lists OpenBI compilation instruction documents (Markdown) per asset kind for a platform (platforms/{platform_id}/assetTypes/*.md). After the user chooses an asset type, you MUST call get_bi_platform_asset_type_instructions before any other OpenBI action.")]
    public Task<string> ListBiPlatformAssetTypes(
        [Description("Platform id from list_bi_platforms / platforms/*/info.json id field.")] string platform_id,
        BiPlatformRegistry registry,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(platform_id) || !registry.TryGetPlatform(platform_id, out var entry) || entry is null)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = $"Unknown or invalid platform_id: {platform_id}. Call list_bi_platforms first."
            }, JsonOpts));
        }

        var assetTypes = entry.AssetTypes.Select(a => new { id = a.Id, title = a.Title }).ToList();

        var payload = new
        {
            platformId = entry.Info.Id,
            platformName = entry.Info.Name,
            assetTypes
        };

        return Task.FromResult(JsonSerializer.Serialize(payload, JsonOpts));
    }

    [McpServerTool(Name = "get_bi_platform_asset_type_instructions", ReadOnly = true)]
    [Description("Returns the full Markdown body for compiling OpenBI assets of one kind for a platform (from platforms/{platform_id}/assetTypes/{asset_type_id}.md).")]
    public async Task<string> GetBiPlatformAssetTypeInstructions(
        [Description("Platform id from list_bi_platforms.")] string platform_id,
        [Description("Asset type id from list_bi_platform_asset_types (filename stem, e.g. Report).")] string asset_type_id,
        BiPlatformRegistry registry,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(platform_id) || !registry.TryGetPlatform(platform_id, out var entry) || entry is null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Unknown or invalid platform_id: {platform_id}. Call list_bi_platforms first."
            }, JsonOpts);
        }

        if (!BiPlatformRegistry.IsSafeAssetTypeIdParameter(asset_type_id))
        {
            return JsonSerializer.Serialize(new
            {
                error = "Invalid asset_type_id: use only safe file-name characters (no path separators, wildcards, or '..')."
            }, JsonOpts);
        }

        var key = asset_type_id.Trim();
        var asset = entry.AssetTypes.FirstOrDefault(a =>
            string.Equals(a.Id, key, StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Unknown asset_type_id '{key}' for platform {entry.Info.Id}. Call list_bi_platform_asset_types first."
            }, JsonOpts);
        }

        string markdown;
        try
        {
            markdown = await File.ReadAllTextAsync(asset.InstructionsPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to read instructions file: {ex.Message}"
            }, JsonOpts);
        }

        var payload = new
        {
            platformId = entry.Info.Id,
            assetTypeId = asset.Id,
            instructionsMarkdown = markdown,
            contentEncoding = "utf-8"
        };

        return JsonSerializer.Serialize(payload, JsonOpts);
    }
}
