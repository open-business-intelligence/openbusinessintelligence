using Microsoft.EntityFrameworkCore;
using OpenBI;
using OpenBI.MCP.Server.Infrastructure.Persistence.Entities;
using OpenBI.MCP.Server.Infrastructure.Persistence.Mappers;

namespace OpenBI.MCP.Server.Infrastructure.Persistence;

/// <summary>
/// Loads a single root <see cref="Asset"/> from the session database by <see cref="DbAssetInfo.Id"/>.
/// </summary>
public static class SessionAssetLoader
{
    /// <summary>
    /// Loads structural content for one asset. Returns <c>null</c> if no <see cref="DbAssetInfo"/> exists for <paramref name="assetId"/>.
    /// </summary>
    public static async Task<Asset?> LoadAssetAsync(OpenBIDbContext ctx, string assetId, CancellationToken cancellationToken = default)
    {
        var dbInfo = await ctx.AssetInfos.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            .ConfigureAwait(false);
        if (dbInfo is null)
            return null;

        var dbTables = await ctx.Tables.AsNoTracking()
            .Where(t => t.AssetInfoId == assetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var tableIds = dbTables.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        var dbColumns = tableIds.Count == 0
            ? []
            : await ctx.Columns.AsNoTracking()
                .Where(c => tableIds.Contains(c.TableId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var dbRelationships = await ctx.Relationships.AsNoTracking()
            .Where(r => r.AssetInfoId == assetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dbPages = await ctx.Pages.AsNoTracking()
            .Where(p => p.AssetInfoId == assetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var pageIds = dbPages.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        var dbVisuals = pageIds.Count == 0
            ? []
            : await ctx.Visuals.AsNoTracking()
                .Where(v => v.PageId != null && pageIds.Contains(v.PageId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var visualIds = dbVisuals.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);

        var dbProjections = visualIds.Count == 0
            ? []
            : await ctx.VisualProjections.AsNoTracking()
                .Where(p => visualIds.Contains(p.VisualId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        List<DbFilter> dbFilters;
        if (pageIds.Count == 0 && visualIds.Count == 0)
            dbFilters = [];
        else
        {
            dbFilters = await ctx.Filters.AsNoTracking()
                .Where(f =>
                    (f.PageId != null && pageIds.Contains(f.PageId)) ||
                    (f.VisualId != null && visualIds.Contains(f.VisualId)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var dbRefreshTasks = await ctx.RefreshTasks.AsNoTracking()
            .Where(r => r.AssetInfoId == assetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dbDataSourceConnections = await ctx.DataSourceConnections.AsNoTracking()
            .Where(dsc => dsc.AssetInfoId == assetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return OpenBIMapper.DbToAsset(
            dbInfo,
            dbTables,
            dbColumns,
            dbRelationships,
            dbPages,
            dbVisuals,
            dbProjections,
            dbFilters,
            dbRefreshTasks,
            dbDataSourceConnections);
    }
}
