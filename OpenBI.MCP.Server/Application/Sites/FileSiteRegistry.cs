using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenBI.Interfaces.Sites;

namespace OpenBI.MCP.Server.Application.Sites;

/// <summary>
/// Site registry loaded from <see cref="SitesDirectoryName"/> under the application base directory.
/// </summary>
public sealed class FileSiteRegistry : ISiteRegistry
{
    public const string SitesDirectoryName = "sites";

    private readonly IReadOnlyDictionary<string, RegisteredSite> _byId;

    private FileSiteRegistry(IReadOnlyDictionary<string, RegisteredSite> byId)
    {
        _byId = byId;
    }

    public IReadOnlyList<RegisteredSite> All =>
        _byId.Values.OrderBy(s => s.SiteName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.IdSite, StringComparer.Ordinal).ToList();

    public bool TryGet(string idSite, out RegisteredSite? site)
    {
        if (_byId.TryGetValue(idSite, out var s))
        {
            site = s;
            return true;
        }

        site = null;
        return false;
    }

    public static FileSiteRegistry Load(string baseDirectory, ILogger<FileSiteRegistry> logger)
    {
        var sitesDir = Path.Combine(baseDirectory, SitesDirectoryName);
        var map = new Dictionary<string, RegisteredSite>(StringComparer.Ordinal);

        if (!Directory.Exists(sitesDir))
        {
            logger.LogWarning("Sites directory not found at {Path}. No sites will be registered.", sitesDir);
            return new FileSiteRegistry(map);
        }

        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        foreach (var file in Directory.EnumerateFiles(sitesDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(file);
                var site = JsonSerializer.Deserialize<RegisteredSite>(json, opts);
                if (site == null || string.IsNullOrWhiteSpace(site.IdSite))
                {
                    logger.LogWarning("Skipping site file {File}: missing idSite.", file);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(site.SiteConnectionFactoryName))
                {
                    logger.LogWarning("Skipping site {IdSite} in {File}: SiteConnectionFactoryName is required.", site.IdSite, file);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(site.PlatformSecretsPath))
                {
                    logger.LogWarning("Skipping site {IdSite} in {File}: PlatformSecretsPath is required.", site.IdSite, file);
                    continue;
                }

                var rawScope = site.SiteConnectionFactoryScope?.Trim();
                string normalizedScope;
                if (string.IsNullOrEmpty(rawScope))
                {
                    normalizedScope = "Singleton";
                }
                else if (string.Equals(rawScope, "Singleton", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedScope = "Singleton";
                }
                else if (string.Equals(rawScope, "Scoped", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedScope = "Scoped";
                }
                else
                {
                    logger.LogWarning(
                        "Skipping site {IdSite} in {File}: SiteConnectionFactoryScope '{Scope}' is invalid. Expected 'Singleton' or 'Scoped'.",
                        site.IdSite, file, rawScope);
                    continue;
                }

                var registered = new RegisteredSite
                {
                    IdSite = site.IdSite,
                    SiteName = site.SiteName,
                    IdPlatform = site.IdPlatform,
                    PlatformName = site.PlatformName,
                    PlatformSecretsPath = site.PlatformSecretsPath,
                    SiteConnectionFactoryName = site.SiteConnectionFactoryName,
                    SiteConnectionFactoryScope = normalizedScope,
                    SiteConverterFactoryName = site.SiteConverterFactoryName
                };
                map[registered.IdSite] = registered;
                logger.LogInformation(
                    "Registered site {IdSite} ({Name}) [{Scope}] from {File}",
                    registered.IdSite, registered.SiteName, normalizedScope, file);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load site file {File}", file);
            }
        }

        logger.LogInformation("Site registry loaded: {Count} site(s).", map.Count);
        return new FileSiteRegistry(map);
    }
}
