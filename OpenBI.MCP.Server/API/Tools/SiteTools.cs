using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using OpenBI.Interfaces.Sites;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class SiteTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "list_sites", ReadOnly = true)]
    [Description("Lists all sites registered in the MCP server (from the sites/*.json configuration). Use this to choose id_site for site-scoped tools (create_asset/query/upload/download).")]
    public Task<string> ListSites(
        ISiteRegistry registry,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var sites = registry.All.Select(s => new
        {
            idSite = s.IdSite,
            siteName = s.SiteName,
            idPlatform = s.IdPlatform,
            platformName = s.PlatformName,
            siteConnectionFactoryName = s.SiteConnectionFactoryName,
            siteConverterFactoryName = s.SiteConverterFactoryName
        }).ToList();

        return Task.FromResult(JsonSerializer.Serialize(new { sites }, JsonOpts));
    }
}
