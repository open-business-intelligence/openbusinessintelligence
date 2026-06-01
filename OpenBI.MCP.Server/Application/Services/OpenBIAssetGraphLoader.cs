using Microsoft.EntityFrameworkCore;
using OpenBI;
using OpenBI.MCP.Server.Infrastructure.Persistence;
using OpenBI.MCP.Server.Infrastructure.Persistence.Mappers;

namespace OpenBI.MCP.Server.Application.Services;

public static class OpenBIAssetGraphLoader
{
    /// <summary>
    /// Builds the asset graph and attaches new rows to <paramref name="ctx"/> (does not call SaveChanges).
    /// </summary>
    public static async Task<AssetGraphLoadResult> StageAssetGraphAsync(OpenBIDbContext ctx, Asset asset, CancellationToken ct)
    {
        var existingIds = await ctx.AssetInfos.AsNoTracking().Select(a => a.Id).ToListAsync(ct);
        var edgeRows = await ctx.AssetDependencies.AsNoTracking()
            .Select(e => new { e.DependentAssetId, e.DependsOnAssetId })
            .ToListAsync(ct);

        var graph = OpenBIMapper.AssetGraphToDb(
            asset,
            existingIds.ToHashSet(),
            edgeRows.Select(e => (e.DependentAssetId, e.DependsOnAssetId)).ToHashSet());

        foreach (var a in graph.AssetInfos)
            ctx.AssetInfos.Add(a);
        ctx.Tables.AddRange(graph.Tables);
        ctx.Columns.AddRange(graph.Columns);
        ctx.Relationships.AddRange(graph.Relationships);
        ctx.Pages.AddRange(graph.Pages);
        ctx.Visuals.AddRange(graph.Visuals);
        ctx.VisualProjections.AddRange(graph.VisualProjections);
        ctx.Filters.AddRange(graph.Filters);
        ctx.RefreshTasks.AddRange(graph.RefreshTasks);
        ctx.DataSourceConnections.AddRange(graph.DataSourceConnections);
        ctx.AssetDependencies.AddRange(graph.AssetDependencies);

        return graph;
    }
}
