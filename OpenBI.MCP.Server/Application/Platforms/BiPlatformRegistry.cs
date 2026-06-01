using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace OpenBI.MCP.Server.Application.Platforms;

/// <summary>
/// Cached BI platform definitions from <see cref="PlatformsDirectoryName"/> (one subfolder per platform with <c>info.json</c>, <c>visualtypes.json</c>, and optional <c>assetTypes/*.md</c>).
/// </summary>
public sealed class BiPlatformRegistry
{
    public const string PlatformsDirectoryName = "platforms";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IReadOnlyDictionary<string, BiPlatformEntry> _byId;

    private BiPlatformRegistry(IReadOnlyDictionary<string, BiPlatformEntry> byId)
    {
        _byId = byId;
    }

    public IReadOnlyList<BiPlatformInfo> AllPlatforms =>
        _byId.Values
            .Select(e => e.Info)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

    public bool TryGetPlatform(string platformId, out BiPlatformEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(platformId))
        {
            entry = null;
            return false;
        }

        var key = platformId.Trim();
        if (_byId.TryGetValue(key, out var e))
        {
            entry = e;
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Validates <paramref name="assetTypeId"/> for MCP parameters: no path traversal, wildcards, or invalid file-name characters.
    /// </summary>
    public static bool IsSafeAssetTypeIdParameter(string? assetTypeId)
    {
        if (string.IsNullOrWhiteSpace(assetTypeId))
            return false;

        if (assetTypeId.Contains("..", StringComparison.Ordinal))
            return false;

        if (assetTypeId.IndexOfAny(['*', '?']) >= 0)
            return false;

        if (assetTypeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return true;
    }

    public static BiPlatformRegistry Load(string baseDirectory, ILogger<BiPlatformRegistry> logger)
    {
        var map = new Dictionary<string, BiPlatformEntry>(StringComparer.Ordinal);
        var platformsRoot = Path.Combine(baseDirectory, PlatformsDirectoryName);

        if (!Directory.Exists(platformsRoot))
        {
            logger.LogWarning("Platforms directory not found at {Path}. No BI platforms will be registered.", platformsRoot);
            return new BiPlatformRegistry(map);
        }

        foreach (var platformDir in Directory.EnumerateDirectories(platformsRoot))
        {
            var folderName = Path.GetFileName(platformDir);
            var infoPath = Path.Combine(platformDir, "info.json");
            var visualPath = Path.Combine(platformDir, "visualtypes.json");

            if (!File.Exists(infoPath))
            {
                logger.LogWarning("Skipping platform folder {Folder}: missing info.json.", folderName);
                continue;
            }

            if (!File.Exists(visualPath))
            {
                logger.LogWarning("Skipping platform folder {Folder}: missing visualtypes.json.", folderName);
                continue;
            }

            try
            {
                var infoJson = File.ReadAllText(infoPath);
                var parsed = JsonSerializer.Deserialize<BiPlatformInfo>(infoJson, JsonOpts);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id) || string.IsNullOrWhiteSpace(parsed.Name))
                {
                    logger.LogWarning("Skipping {InfoPath}: id and name are required.", infoPath);
                    continue;
                }

                var id = parsed.Id.Trim();
                var name = parsed.Name.Trim();
                var info = new BiPlatformInfo { Id = id, Name = name };

                if (!string.Equals(folderName, id, StringComparison.OrdinalIgnoreCase))
                    logger.LogInformation(
                        "Platform folder name {Folder} differs from info.json id {Id} (using id as key).",
                        folderName, id);

                var visualJson = File.ReadAllText(visualPath);
                var visuals = DeserializeVisualTypes(visualJson, logger, visualPath);
                if (visuals == null)
                    continue;

                var withPlatform = visuals
                    .Select(v => new BiVisualTypeDefinition
                    {
                        ObjectType = v.ObjectType,
                        VisualType = v.VisualType,
                        OpenBIVisualType = v.OpenBIVisualType,
                        VisualDescription = v.VisualDescription,
                        VisualTypeProjection = v.VisualTypeProjection,
                        ProjectionDescription = v.ProjectionDescription,
                        Order = v.Order,
                        ProjectionAllowsMultipleValues = v.ProjectionAllowsMultipleValues,
                        PlatformId = id
                    })
                    .OrderBy(v => v.Order)
                    .ThenBy(v => v.VisualType, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var assetTypes = LoadAssetTypes(platformDir, logger);

                var entry = new BiPlatformEntry
                {
                    Info = info,
                    VisualTypes = withPlatform,
                    AssetTypes = assetTypes
                };

                if (map.ContainsKey(id))
                {
                    logger.LogWarning("Duplicate platform id {Id}; skipping {Path}.", id, infoPath);
                    continue;
                }

                map[id] = entry;
                logger.LogInformation(
                    "Registered BI platform {Id} ({Name}) with {VisualCount} visual type(s) and {AssetTypeCount} asset type instruction file(s) from {Folder}.",
                    id, name, withPlatform.Count, assetTypes.Count, folderName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load platform from folder {Folder}", folderName);
            }
        }

        logger.LogInformation("BI platform registry loaded: {Count} platform(s).", map.Count);
        return new BiPlatformRegistry(map);
    }

    private static IReadOnlyList<BiPlatformAssetTypeInfo> LoadAssetTypes(string platformDir, ILogger logger)
    {
        var assetTypesDir = Path.Combine(platformDir, "assetTypes");
        if (!Directory.Exists(assetTypesDir))
            return Array.Empty<BiPlatformAssetTypeInfo>();

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<BiPlatformAssetTypeInfo>();

        foreach (var path in Directory.GetFiles(assetTypesDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(stem))
            {
                logger.LogWarning("Skipping empty asset type file name under {Dir}.", assetTypesDir);
                continue;
            }

            if (!IsSafeAssetTypeIdParameter(stem))
            {
                logger.LogWarning("Skipping asset type file with invalid stem {Stem} under {Dir}.", stem, assetTypesDir);
                continue;
            }

            if (seen.TryGetValue(stem, out var firstPath))
            {
                logger.LogWarning(
                    "Duplicate asset type id {Id} (case-insensitive); keeping {First}, skipping {Second}.",
                    stem, firstPath, path);
                continue;
            }

            seen[stem] = path;

            string? title = null;
            try
            {
                title = TryReadFirstMarkdownHeading(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read title from {Path}; listing without title.", path);
            }

            list.Add(new BiPlatformAssetTypeInfo
            {
                Id = stem,
                Title = title,
                InstructionsPath = Path.GetFullPath(path)
            });
        }

        return list
            .OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static string? TryReadFirstMarkdownHeading(string path)
    {
        using var reader = new StreamReader(path);
        for (var i = 0; i < 100; i++)
        {
            var line = reader.ReadLine();
            if (line is null)
                break;

            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
                continue;

            if (!trimmed.StartsWith('#'))
                continue;

            // H1 only (skip ## and deeper)
            if (trimmed.StartsWith("##", StringComparison.Ordinal))
                continue;

            var rest = trimmed.AsSpan(1).TrimStart();
            if (rest.Length == 0)
                continue;

            return rest.ToString();
        }

        return null;
    }

    private static List<BiVisualTypeDefinition>? DeserializeVisualTypes(string json, ILogger logger, string visualPath)
    {
        try
        {
            var trimmed = json.TrimStart();
            if (trimmed.StartsWith('['))
            {
                var list = JsonSerializer.Deserialize<List<BiVisualTypeDefinition>>(json, JsonOpts);
                return list ?? new List<BiVisualTypeDefinition>();
            }

            var wrapper = JsonSerializer.Deserialize<VisualTypesFileWrapper>(json, JsonOpts);
            if (wrapper?.VisualTypes != null)
                return wrapper.VisualTypes;

            logger.LogWarning("{Path}: expected a JSON array or an object with visualTypes array.", visualPath);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON in {Path}", visualPath);
            return null;
        }
    }

    private sealed class VisualTypesFileWrapper
    {
        public List<BiVisualTypeDefinition>? VisualTypes { get; set; }
    }
}
