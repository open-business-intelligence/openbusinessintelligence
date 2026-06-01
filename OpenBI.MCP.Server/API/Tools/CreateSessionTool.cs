using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.Interfaces.Sites;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class CreateSessionTool
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "create_session")]
    [Description("Creates a new OpenBI session: allocates a SQLite database under sessions/ and binds it to id_site. Does not create assets. Use create_asset for empty assets or load_openbi_asset to import JSON from disk.")]
    public Task<string> CreateSession(
        [Description("Site id from list_sites (must match IdSite in sites/*.json).")] string id_site,
        ISiteRegistry registry,
        SessionStore store,
        ILogger<CreateSessionTool> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (registry.All.Count == 0)
            return Task.FromResult(JsonSerializer.Serialize(new { status = "Error", message = "No sites configured. Add JSON files under the sites folder next to the executable." }, JsonOpts));

        if (string.IsNullOrWhiteSpace(id_site) || !registry.TryGet(id_site, out _))
            return Task.FromResult(JsonSerializer.Serialize(new { status = "Error", message = $"Unknown or invalid id_site: {id_site}. Call list_sites first." }, JsonOpts));

        var sessionId = store.CreateSession(id_site);

        logger.LogInformation("create_session: {SessionId} for site {IdSite}", sessionId, id_site);

        return Task.FromResult(JsonSerializer.Serialize(new { status = "Success", sessionId = sessionId.ToString(), idSite = id_site }, JsonOpts));
    }
}
