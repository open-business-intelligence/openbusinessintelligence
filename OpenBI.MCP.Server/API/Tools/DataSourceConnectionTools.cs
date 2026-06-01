using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenBI.Interfaces;
using OpenBI.MCP.Server.Application.Services;
using OpenBI.MCP.Server.Infrastructure.Persistence.Entities;
using OpenBI.MCP.Server.Infrastructure.Persistence.Mappers;

namespace OpenBI.MCP.Server.API.Tools;

[McpServerToolType]
public sealed class DataSourceConnectionTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "get_data_source_connections", ReadOnly = true)]
    [Description("Returns all data source connections for a given asset in the session.")]
    public async Task<string> GetDataSourceConnections(
        [Description("The session ID.")] string session_id,
        [Description("The asset ID.")] string asset_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<DataSourceConnectionTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var assetExists = await ctx.AssetInfos.AsNoTracking().AnyAsync(a => a.Id == asset_id, ct);
        if (!assetExists)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Asset '{asset_id}' not found." });

        var dbRows = await ctx.DataSourceConnections.AsNoTracking()
            .Where(d => d.AssetInfoId == asset_id)
            .OrderBy(d => d.Name)
            .ThenBy(d => d.RowId)
            .ToListAsync(ct);

        var dataSourceConnections = dbRows.Select(d => new
        {
            rowId = d.RowId,
            externalId = d.ExternalId,
            name = d.Name,
            type = d.Type,
            parameters = OpenBIMapper.MapDbDataSourceConnection(d).Parameters
        }).ToList();

        return JsonSerializer.Serialize(new { dataSourceConnections }, JsonOpts);
    }

    [McpServerTool(Name = "get_data_source_connection", ReadOnly = true)]
    [Description("Returns a single data source connection by row ID for a given asset.")]
    public async Task<string> GetDataSourceConnection(
        [Description("The session ID.")] string session_id,
        [Description("The asset ID.")] string asset_id,
        [Description("The data source connection row ID (integer).")] int row_id,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<DataSourceConnectionTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));
        var dbRow = await ctx.DataSourceConnections.AsNoTracking()
            .FirstOrDefaultAsync(d => d.AssetInfoId == asset_id && d.RowId == row_id, ct);
        if (dbRow is null)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Data source connection {row_id} not found for asset '{asset_id}'." });

        var connection = OpenBIMapper.MapDbDataSourceConnection(dbRow);
        return JsonSerializer.Serialize(new
        {
            rowId = dbRow.RowId,
            externalId = connection.ExternalId,
            name = connection.Name,
            type = connection.Type,
            parameters = connection.Parameters
        }, JsonOpts);
    }

    [McpServerTool(Name = "create_data_source_connection")]
    [Description("Creates a new data source connection for an asset in the session.")]
    public async Task<string> CreateDataSourceConnection(
        [Description("The session ID.")] string session_id,
        [Description("The asset ID.")] string asset_id,
        [Description("Connection type (e.g. MySQL, Postgres, Web, Amazon S3).")] string type,
        [Description("Friendly connection name (must be unique inside the asset).")] string name,
        [Description("Optional external ID for the connection.")] string? external_id,
        [Description("Optional key-value parameters for the connection.")] Dictionary<string, string>? parameters,
        SessionStore store,
        ICurrentSessionManager currentSessionManager,
        ILogger<DataSourceConnectionTools> logger,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        currentSessionManager.SetIdSession(session_id);

        using var ctx = store.GetContext(Guid.Parse(session_id));

        var normalizedAssetId = asset_id?.Trim();
        var normalizedType = type?.Trim();
        var normalizedName = name?.Trim();
        var normalizedExternalId = string.IsNullOrWhiteSpace(external_id) ? null : external_id.Trim();

        if (string.IsNullOrWhiteSpace(normalizedAssetId))
            return JsonSerializer.Serialize(new { status = "Error", message = "asset_id is required." });
        if (string.IsNullOrWhiteSpace(normalizedType))
            return JsonSerializer.Serialize(new { status = "Error", message = "type is required." });
        if (string.IsNullOrWhiteSpace(normalizedName))
            return JsonSerializer.Serialize(new { status = "Error", message = "name is required." });

        var assetExists = await ctx.AssetInfos.AsNoTracking().AnyAsync(a => a.Id == normalizedAssetId, ct);
        if (!assetExists)
            return JsonSerializer.Serialize(new { status = "Error", message = $"Asset '{normalizedAssetId}' not found." });

        var duplicateName = await ctx.DataSourceConnections.AsNoTracking()
            .AnyAsync(d => d.AssetInfoId == normalizedAssetId && d.Name == normalizedName, ct);
        if (duplicateName)
            return JsonSerializer.Serialize(new
            {
                status = "Error",
                message = $"A data source connection named '{normalizedName}' already exists for asset '{normalizedAssetId}'."
            });

        var dbRow = OpenBIMapper.MapDataSourceConnection(new DataSourceConnection
        {
            ExternalId = normalizedExternalId,
            Name = normalizedName,
            Type = normalizedType,
            Parameters = parameters
        }, normalizedAssetId);

        ctx.DataSourceConnections.Add(dbRow);
        await ctx.SaveChangesAsync(ct);

        logger.LogInformation("create_data_source_connection: {RowId} '{Name}' for asset {AssetId}", dbRow.RowId, normalizedName, normalizedAssetId);
        return JsonSerializer.Serialize(new
        {
            status = "Success",
            rowId = dbRow.RowId,
            assetId = normalizedAssetId,
            externalId = dbRow.ExternalId,
            name = dbRow.Name,
            type = dbRow.Type,
            parameters
        }, JsonOpts);
    }
}
