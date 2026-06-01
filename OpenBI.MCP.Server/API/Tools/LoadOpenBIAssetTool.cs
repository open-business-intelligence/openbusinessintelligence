using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class LoadOpenBIAssetTool
{
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [McpServerTool(Name = "load_openbi_asset")]
    [Description("Loads an OpenBI asset from a JSON file into the session's SQLite database. The file must be a valid OpenBI Asset JSON.")]
    public async Task<string> LoadOpenBIAsset(
        [Description("The session ID returned by create_session.")] string session_id,
        [Description("Absolute file path to the OpenBI asset JSON file.")] string file_path,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<LoadOpenBIAssetTool> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        if (!File.Exists(file_path))
            return JsonSerializer.Serialize(new { status = "Error", message = $"File not found: {file_path}" });

        var json = await File.ReadAllTextAsync(file_path, ct);
        var asset = JsonSerializer.Deserialize<Asset>(json, ReadOpts);
        if (asset is null)
            return JsonSerializer.Serialize(new { status = "Error", message = "Failed to deserialize asset." });

        var sessionGuid = Guid.Parse(session_id);
        using var ctx = store.GetContext(sessionGuid);

        var graph = await OpenBIAssetGraphLoader.StageAssetGraphAsync(ctx, asset, ct);

        await ctx.SaveChangesAsync(ct);

        logger.LogInformation(
            "load_openbi_asset: loaded {Assets} assets, {Deps} dependency edges, {Tables} tables, {Pages} pages, {Visuals} visuals from {Path}",
            graph.AssetInfos.Count, graph.AssetDependencies.Count, graph.Tables.Count, graph.Pages.Count, graph.Visuals.Count, file_path);

        return JsonSerializer.Serialize(new
        {
            status = "Success",
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
        });
    }
}
