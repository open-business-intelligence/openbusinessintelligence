using System.Text.Json;
using OpenBI;
using OpenBI.MCP.Server.Infrastructure.Persistence.Entities;
using OpenBI.Models;
using static OpenBI.Column;

namespace OpenBI.MCP.Server.Infrastructure.Persistence.Mappers;

public sealed class AssetGraphLoadResult
{
    public List<DbAssetInfo> AssetInfos { get; } = new();
    public List<DbTable> Tables { get; } = new();
    public List<DbColumn> Columns { get; } = new();
    public List<DbRelationship> Relationships { get; } = new();
    public List<DbPage> Pages { get; } = new();
    public List<DbVisual> Visuals { get; } = new();
    public List<DbVisualProjection> VisualProjections { get; } = new();
    public List<DbFilter> Filters { get; } = new();
    public List<DbRefreshTask> RefreshTasks { get; } = new();
    public List<DbDataSourceConnection> DataSourceConnections { get; } = new();
    public List<DbAssetDependency> AssetDependencies { get; } = new();
}

public static class OpenBIMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region Asset -> DB (Load)

    /// <summary>
    /// Maps a root asset and nested <see cref="Asset.Dependencies"/> into DB rows.
    /// Skips content for asset ids already in <paramref name="existingAssetIds"/>; skips edges already in <paramref name="existingEdges"/>.
    /// First visit wins for structural content (tables, pages, etc.).
    /// </summary>
    public static AssetGraphLoadResult AssetGraphToDb(
        Asset root,
        IReadOnlySet<string> existingAssetIds,
        IReadOnlySet<(string DependentId, string DependsOnId)> existingEdges)
    {
        if (string.IsNullOrWhiteSpace(root.Info?.Id))
            throw new InvalidOperationException("Root Asset.Info.Id is required.");

        var result = new AssetGraphLoadResult();
        var seenContentIds = new HashSet<string>(existingAssetIds, StringComparer.Ordinal);
        var seenEdgeKeys = new HashSet<(string, string)>(
            existingEdges.Select(e => (e.DependentId, e.DependsOnId)));

        void Visit(Asset asset, string? parentId)
        {
            if (asset.Info?.Id is not { Length: > 0 } id)
                return;

            if (!seenContentIds.Contains(id))
            {
                seenContentIds.Add(id);
                result.AssetInfos.Add(MapAssetInfo(asset));

                if (HasStructuralContent(asset))
                    AppendAssetContent(asset, id, result);
            }

            if (parentId is not null &&
                seenEdgeKeys.Add((parentId, id)))
            {
                result.AssetDependencies.Add(new DbAssetDependency
                {
                    DependentAssetId = parentId,
                    DependsOnAssetId = id
                });
            }

            foreach (var dep in asset.Dependencies ?? Enumerable.Empty<Asset>())
                Visit(dep, id);
        }

        Visit(root, null);
        return result;
    }

    /// <summary>
    /// Maps a single asset's own tables, pages, etc. Does not traverse <see cref="Asset.Dependencies"/>; use <see cref="AssetGraphToDb"/> for that.
    /// </summary>
    public static (DbAssetInfo AssetInfo,
                   List<DbTable> Tables,
                   List<DbColumn> Columns,
                   List<DbRelationship> Relationships,
                   List<DbPage> Pages,
                   List<DbVisual> Visuals,
                   List<DbVisualProjection> Projections,
                   List<DbFilter> Filters,
                   List<DbRefreshTask> RefreshTasks,
                   List<DbDataSourceConnection> DataSourceConnections)
        AssetToDb(Asset asset)
    {
        var info = asset.Info ?? throw new InvalidOperationException("Asset.Info is required.");
        var assetInfoId = info.Id;
        var dbAssetInfo = MapAssetInfo(asset);
        var agg = new AssetGraphLoadResult();
        AppendAssetContent(asset, assetInfoId, agg);
        return (dbAssetInfo, agg.Tables, agg.Columns, agg.Relationships, agg.Pages, agg.Visuals, agg.VisualProjections, agg.Filters, agg.RefreshTasks, agg.DataSourceConnections);
    }

    private static bool HasStructuralContent(Asset asset) =>
        (asset.DataModel?.Tables?.Count > 0) ||
        (asset.Layout?.Pages?.Count > 0) ||
        (asset.DataModel?.Relationships?.Count > 0) ||
        (asset.RefreshTasks?.Count > 0) ||
        (asset.DataSourceConnections?.Count > 0);

    private static void AppendAssetContent(Asset asset, string assetInfoId, AssetGraphLoadResult into)
    {
        foreach (var table in asset.DataModel?.Tables ?? [])
        {
            into.Tables.Add(MapTable(table, assetInfoId));
            foreach (var col in table.Columns ?? [])
                into.Columns.Add(MapColumn(col, table.Id));
        }

        foreach (var r in asset.DataModel?.Relationships ?? [])
            into.Relationships.Add(MapRelationship(r, assetInfoId));

        foreach (var page in asset.Layout?.Pages ?? [])
        {
            into.Pages.Add(MapPage(page, assetInfoId));

            foreach (var filter in page.PageLevelFilters ?? [])
                into.Filters.Add(MapFilter(filter, pageId: page.Id, visualId: null));

            FlattenVisuals(page.Visuals, page.Id, null, into.Visuals, into.VisualProjections, into.Filters);
        }

        foreach (var rt in asset.RefreshTasks ?? [])
            into.RefreshTasks.Add(MapRefreshTask(rt, assetInfoId));

        foreach (var dsc in asset.DataSourceConnections ?? [])
            into.DataSourceConnections.Add(MapDataSourceConnection(dsc, assetInfoId));
    }

    private static void FlattenVisuals(
        IEnumerable<Visual>? visuals,
        string pageId,
        string? parentVisualId,
        List<DbVisual> dbVisuals,
        List<DbVisualProjection> dbProjections,
        List<DbFilter> dbFilters)
    {
        foreach (var visual in visuals ?? [])
        {
            dbVisuals.Add(MapVisual(visual, pageId, parentVisualId));

            foreach (var proj in visual.VisualProjections ?? [])
                dbProjections.Add(MapVisualProjection(proj, visual.Id));

            foreach (var filter in visual.VisualLevelFilters ?? [])
                dbFilters.Add(MapFilter(filter, pageId: null, visualId: visual.Id));

            FlattenVisuals(visual.Children, pageId, visual.Id, dbVisuals, dbProjections, dbFilters);
        }
    }

    #endregion

    #region DB -> Asset (Unload)

    public static Asset DbToAsset(
        DbAssetInfo dbInfo,
        List<DbTable> dbTables,
        List<DbColumn> dbColumns,
        List<DbRelationship> dbRelationships,
        List<DbPage> dbPages,
        List<DbVisual> dbVisuals,
        List<DbVisualProjection> dbProjections,
        List<DbFilter> dbFilters,
        List<DbRefreshTask> dbRefreshTasks,
        List<DbDataSourceConnection> dbDataSourceConnections)
    {
        var asset = new Asset
        {
            IdSite = dbInfo.IdSite,
            Info = MapDbAssetInfo(dbInfo),
            DataModel = new DataModel
            {
                Tables = dbTables.Select(t =>
                {
                    var table = MapDbTable(t);
                    table.Columns = dbColumns
                        .Where(c => c.TableId == t.Id)
                        .Select(MapDbColumn)
                        .ToList();
                    return table;
                }).ToList(),
                Relationships = dbRelationships.Select(MapDbRelationship).ToList()
            },
            Layout = new Layout
            {
                Pages = dbPages.Select(p =>
                {
                    var page = MapDbPage(p);
                    page.PageLevelFilters = dbFilters
                        .Where(f => f.PageId == p.Id && f.VisualId == null)
                        .Select(MapDbFilter)
                        .ToList();

                    var pageVisuals = dbVisuals.Where(v => v.PageId == p.Id).ToList();
                    page.Visuals = BuildVisualTree(pageVisuals, null, dbProjections, dbFilters);
                    return page;
                }).ToList()
            },
            RefreshTasks = dbRefreshTasks.Select(MapDbRefreshTask).ToList(),
            DataSourceConnections = dbDataSourceConnections.Select(MapDbDataSourceConnection).ToList(),
            Dependencies = []
        };

        return asset;
    }

    private static List<Visual> BuildVisualTree(
        List<DbVisual> allVisuals,
        string? parentId,
        List<DbVisualProjection> allProjections,
        List<DbFilter> allFilters)
    {
        return allVisuals
            .Where(v => v.ParentVisualId == parentId)
            .Select(v =>
            {
                var visual = MapDbVisual(v);
                visual.VisualProjections = allProjections
                    .Where(p => p.VisualId == v.Id)
                    .Select(MapDbVisualProjection)
                    .ToList();
                visual.VisualLevelFilters = allFilters
                    .Where(f => f.VisualId == v.Id)
                    .Select(MapDbFilter)
                    .ToList();
                visual.Children = BuildVisualTree(allVisuals, v.Id, allProjections, allFilters);
                return visual;
            }).ToList();
    }

    #endregion

    #region Individual entity mappers: OpenBI -> DB

    public static DbAssetInfo MapAssetInfo(AssetInfo info) => new()
    {
        Id = info.Id,
        ExternalType = info.ExternalType ?? string.Empty,
        Type = (int)info.Type,
        Name = info.Name ?? string.Empty,
        Description = info.Description ?? string.Empty,
        LatestUpdate = info.LatestUpdate,
        LatestUpdater = info.LatestUpdater,
        AdditionalMetadataJson = SerializeJson(info.AdditionalMetadata),
    };

    public static DbAssetInfo MapAssetInfo(Asset asset)
    {
        var info = asset.Info ?? throw new InvalidOperationException("Asset.Info is required.");
        var mapped = MapAssetInfo(info);
        mapped.IdSite = asset.IdSite;
        return mapped;
    }

    public static DbTable MapTable(Table table, string assetInfoId) => new()
    {
        Id = table.Id,
        Type = table.Type,
        Name = table.Name,
        ExpressionJson = SerializeJson(table.Expression),
        AdditionalMetadataJson = SerializeJson(table.AdditionalMetadata),
        AssetInfoId = assetInfoId
    };

    public static DbColumn MapColumn(Column col, string tableId) => new()
    {
        Id = col.Id,
        Type = col.Type,
        Name = col.Name,
        Description = col.Description,
        DataType = (int)col.DataType,
        SummarizeBy = col.SummarizeBy,
        ExpressionJson = SerializeJson(col.Expression),
        FormatString = col.FormatString,
        DataCategory = col.DataCategory,
        IsKey = col.IsKey,
        IsUnique = col.IsUnique,
        IsNullable = col.IsNullable,
        IsDimension = col.IsDimension,
        IsMeasure = col.IsMeasure,
        ColumnsReferencesJson = SerializeJson(col.ColumnsReferences),
        AdditionalMetadataJson = SerializeJson(col.AdditionalMetadata),
        TableId = tableId
    };

    public static DbRelationship MapRelationship(Relationship rel, string assetInfoId) => new()
    {
        Name = rel.Name,
        IdColumnFrom = rel.IdColumnFrom,
        IdColumnTo = rel.IdColumnTo,
        Type = (int)rel.Type,
        ExpressionJson = SerializeJson(rel.Expression),
        AdditionalMetadataJson = SerializeJson(rel.AdditionalMetadata),
        AssetInfoId = assetInfoId
    };

    public static DbPage MapPage(Page page, string assetInfoId) => new()
    {
        Id = page.Id,
        FriendlyName = page.Name,
        Order = page.Order,
        Width = page.Width,
        Height = page.Height,
        IsEnabled = page.IsEnabled,
        PageImagePath = page.PageImagePath,
        Description = page.Description,
        AdditionalMetadataJson = SerializeJson(page.AdditionalMetadata),
        AssetInfoId = assetInfoId
    };

    public static DbVisual MapVisual(Visual visual, string pageId, string? parentVisualId) => new()
    {
        Id = visual.Id,
        FriendlyName = visual.Name,
        Category = (int)visual.Category,
        Type = visual.Type,
        OpenBIVisualType = visual.OpenBIVisualType,
        X = visual.X,
        Y = visual.Y,
        Z = visual.Z,
        Width = visual.Width,
        Height = visual.Height,
        Description = visual.Description,
        AdditionalMetadataJson = SerializeJson(visual.AdditionalMetadata),
        PageId = parentVisualId == null ? pageId : null,
        ParentVisualId = parentVisualId
    };

    public static DbVisualProjection MapVisualProjection(VisualProjection proj, string visualId) => new()
    {
        ProjectionName = proj.ProjectionName,
        ExpressionJson = SerializeJson(proj.Expression),
        IsActive = proj.IsActive,
        Type = proj.Type,
        IsDimension = proj.IsDimension,
        IsMeasure = proj.IsMeasure,
        IdColumnReference = proj.IdColumnReference,
        Order = proj.Order,
        VisualId = visualId
    };

    public static DbFilter MapFilter(Filter filter, string? pageId, string? visualId) => new()
    {
        OriginalId = filter.Id,
        IdColumn = filter.IdColumn,
        Function = (int)filter.Function,
        FunctionName = filter.FunctionName,
        LogicalOperator = filter.LogicalOperator,
        ExpressionJson = SerializeJson(filter.Expression),
        IsGroup = filter.IsGroup,
        ValuesJson = SerializeJson(filter.Values),
        ChildrenJson = SerializeJson(filter.Children),
        PageId = pageId,
        VisualId = visualId
    };

    public static DbRefreshTask MapRefreshTask(RefreshTask task, string assetInfoId) => new()
    {
        Id = task.Id,
        TriggersJson = SerializeJson(task.Triggers),
        AdditionalMetadataJson = SerializeJson(task.AdditionalMetadata),
        AssetInfoId = assetInfoId
    };

    public static DbDataSourceConnection MapDataSourceConnection(DataSourceConnection dsc, string assetInfoId) => new()
    {
        ExternalId = dsc.ExternalId,
        Name = dsc.Name,
        Type = dsc.Type,
        ParametersJson = SerializeJson(dsc.Parameters),
        AssetInfoId = assetInfoId
    };

    #endregion

    #region Individual entity mappers: DB -> OpenBI

    public static AssetInfo MapDbAssetInfo(DbAssetInfo db) => new()
    {
        Id = db.Id,
        ExternalType = db.ExternalType,
        Type = (AssetType)db.Type,
        Name = db.Name,
        Description = db.Description,
        LatestUpdate = db.LatestUpdate,
        LatestUpdater = db.LatestUpdater,
        AdditionalMetadata = DeserializeJson<List<AdditionalMetadata>>(db.AdditionalMetadataJson)
    };

    public static Table MapDbTable(DbTable db) => new()
    {
        Id = db.Id,
        Type = db.Type,
        Name = db.Name,
        Expression = DeserializeJson<Expression>(db.ExpressionJson),
        AdditionalMetadata = DeserializeJson<List<AdditionalMetadata>>(db.AdditionalMetadataJson)
    };

    public static Column MapDbColumn(DbColumn db) => new()
    {
        Id = db.Id,
        Type = db.Type,
        Name = db.Name,
        Description = db.Description,
        DataType = (ColumnDataType)db.DataType,
        SummarizeBy = db.SummarizeBy,
        Expression = DeserializeJson<Expression>(db.ExpressionJson),
        FormatString = db.FormatString,
        DataCategory = db.DataCategory,
        IsKey = db.IsKey,
        IsUnique = db.IsUnique,
        IsNullable = db.IsNullable,
        IsDimension = db.IsDimension,
        IsMeasure = db.IsMeasure,
        ColumnsReferences = DeserializeJson<List<ColumnsReference>>(db.ColumnsReferencesJson) ?? [],
        AdditionalMetadata = DeserializeJson<List<AdditionalMetadata>>(db.AdditionalMetadataJson)
    };

    public static Relationship MapDbRelationship(DbRelationship db) => new()
    {
        Name = db.Name,
        IdColumnFrom = db.IdColumnFrom,
        IdColumnTo = db.IdColumnTo,
        Type = (RelationshipDirection)db.Type,
        Expression = DeserializeJson<Expression>(db.ExpressionJson),
        AdditionalMetadata = DeserializeJson<List<AdditionalMetadata>>(db.AdditionalMetadataJson)
    };

    public static Page MapDbPage(DbPage db) => new()
    {
        Id = db.Id,
        Name = db.FriendlyName,
        Order = db.Order,
        Width = db.Width,
        Height = db.Height,
        IsEnabled = db.IsEnabled,
        PageImagePath = db.PageImagePath,
        Description = db.Description,
        AdditionalMetadata = DeserializeJson<List<AdditionalMetadata>>(db.AdditionalMetadataJson)
    };

    public static Visual MapDbVisual(DbVisual db) => new()
    {
        Id = db.Id,
        Name = db.FriendlyName,
        Category = (VisualCategories)db.Category,
        Type = db.Type,
        OpenBIVisualType = db.OpenBIVisualType,
        X = db.X,
        Y = db.Y,
        Z = db.Z,
        Width = db.Width,
        Height = db.Height,
        Description = db.Description,
        AdditionalMetadata = DeserializeJson<List<AdditionalMetadata>>(db.AdditionalMetadataJson)
    };

    public static VisualProjection MapDbVisualProjection(DbVisualProjection db) => new()
    {
        ProjectionName = db.ProjectionName,
        Expression = DeserializeJson<Expression>(db.ExpressionJson),
        IsActive = db.IsActive,
        Type = db.Type,
        IsDimension = db.IsDimension,
        IsMeasure = db.IsMeasure,
        IdColumnReference = db.IdColumnReference,
        Order = db.Order
    };

    public static Filter MapDbFilter(DbFilter db) => new()
    {
        Id = db.OriginalId,
        IdColumn = db.IdColumn,
        Function = (FilterFunctionType)db.Function,
        FunctionName = db.FunctionName,
        LogicalOperator = db.LogicalOperator,
        Expression = DeserializeJson<Expression>(db.ExpressionJson),
        IsGroup = db.IsGroup,
        Values = DeserializeJson<List<string>>(db.ValuesJson) ?? [],
        Children = DeserializeJson<List<Filter>>(db.ChildrenJson)
    };

    public static RefreshTask MapDbRefreshTask(DbRefreshTask db) => new()
    {
        Id = db.Id,
        Triggers = DeserializeJson<List<RefreshTrigger>>(db.TriggersJson),
        AdditionalMetadata = DeserializeJson<List<AdditionalMetadata>>(db.AdditionalMetadataJson)
    };

    public static DataSourceConnection MapDbDataSourceConnection(DbDataSourceConnection db) => new()
    {
        ExternalId = db.ExternalId,
        Name = db.Name,
        Type = db.Type,
        Parameters = DeserializeJson<Dictionary<string, string>>(db.ParametersJson)
    };

    #endregion

    #region JSON helpers

    private static string? SerializeJson<T>(T? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOpts);

    private static T? DeserializeJson<T>(string? json) where T : class =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<T>(json, JsonOpts);

    #endregion
}
