using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.Interfaces.Sites;
using OpenBI.Connectors.Interfaces.Models;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class FolderTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "get_folders", ReadOnly = true)]
    [Description("Lists folders (e.g. workspaces) for the specified BI site.")]
    public async Task<string> GetFolders(
        [Description("The session ID returned by create_session.")] string session_id,
        [Description("Site id from list_sites.")] string id_site,
        SessionStore store,
        ISiteRegistry registry,
        SiteConnectionSession siteConnections,
        ICurrentSessionManager currentSessionManager,
        ILogger<FolderTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        var sessionGuid = Guid.Parse(session_id);
        using var _ = store.GetContext(sessionGuid);
        if (!registry.TryGet(id_site, out var site) || site is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Site '{id_site}' is not registered." }, JsonOpts);

        var conn = await siteConnections.OpenConnectionAsync(site, ct).ConfigureAwait(false);
        var folders = await conn.GetSiteFoldersAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "Success", folders }, JsonOpts);
    }

    [McpServerTool(Name = "create_folder")]
    [Description("Creates a folder (e.g. workspace) on the specified BI site.")]
    public async Task<string> CreateFolder(
        [Description("The session ID returned by create_session.")] string session_id,
        [Description("Site id from list_sites.")] string id_site,
        [Description("Display name for the new folder.")] string name,
        SessionStore store,
        ISiteRegistry registry,
        SiteConnectionSession siteConnections,
        ICurrentSessionManager currentSessionManager,
        ILogger<FolderTools> logger,
        [Description("Optional description.")] string? description = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        var sessionGuid = Guid.Parse(session_id);
        using var _ = store.GetContext(sessionGuid);
        if (!registry.TryGet(id_site, out var site) || site is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Site '{id_site}' is not registered." }, JsonOpts);

        var conn = await siteConnections.OpenConnectionAsync(site, ct).ConfigureAwait(false);
        var request = new CreateSiteFolderRequest { Name = name, Description = description };
        var folder = await conn.CreateSiteFolderAsync(request, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "Success", folder }, JsonOpts);
    }
}
