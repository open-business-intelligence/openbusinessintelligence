using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenBI.Patching;

/// <summary>
/// Default implementation of <see cref="IOpenBIAssetComparer"/>.
/// Compares two <see cref="Asset"/> instances and produces the minimal list of
/// <see cref="OpenBIChange"/> objects needed to transform <paramref name="from"/> into <paramref name="to"/>.
/// <para>
/// All collection comparisons are id-based: items are matched by their natural key
/// (Id, or Name where Id is absent). Items present only in <paramref name="from"/> are
/// removed; items present only in <paramref name="to"/> are added; items present in both
/// are inspected property-by-property and a Replace is emitted only when something changed.
/// </para>
/// </summary>
public class OpenBIAssetComparer : IOpenBIAssetComparer
{
    /// <summary>
    /// Static entry point — preserved for backward compatibility with non-DI call sites.
    /// In DI-managed components, inject <see cref="IOpenBIAssetComparer"/> instead.
    /// </summary>
    public static IReadOnlyList<OpenBIChange> Compare(Asset from, Asset to)
    {
        var changes = new List<OpenBIChange>();
        CompareAssetRoot(from, to, changes);
        return changes.AsReadOnly();
    }

    /// <summary>Interface implementation — delegates to the static core.</summary>
    IReadOnlyList<OpenBIChange> IOpenBIAssetComparer.Compare(Asset from, Asset to)
        => Compare(from, to);

    // ─── Asset root ────────────────────────────────────────────────────────────

    private static void CompareAssetRoot(Asset from, Asset to, List<OpenBIChange> changes)
    {
        if (from.Info != null && to.Info != null)
            CompareAssetInfo(from.Info, to.Info, changes);

        CompareDataModel(from.DataModel, to.DataModel, changes);
        CompareLayout(from.Layout, to.Layout, changes);

        CompareCollection(
            from.RefreshTasks, to.RefreshTasks,
            x => x.Id, OpenBIEntity.RefreshTask, parentId: null, changes,
            CompareRefreshTask);

        CompareCollection(
            from.DataSourceConnections, to.DataSourceConnections,
            x => x.Name, OpenBIEntity.DataSourceConnection, parentId: null, changes,
            CompareDataSourceConnection);
    }

    // ─── AssetInfo ─────────────────────────────────────────────────────────────

    private static void CompareAssetInfo(AssetInfo from, AssetInfo to, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "name",              from.Name,          to.Name);
        AddPart(parts, "description",       from.Description,   to.Description);
        AddPart(parts, "externalType",      from.ExternalType,  to.ExternalType);
        AddPart(parts, "idFolder",          from.IdFolder,      to.IdFolder);
        AddPart(parts, "folderName",        from.FolderName,    to.FolderName);
        AddPart(parts, "type",              from.Type,          to.Type);
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.AssetInfo, from.Id, parts));
    }

    // ─── DataModel ─────────────────────────────────────────────────────────────

    private static void CompareDataModel(DataModel? from, DataModel? to, List<OpenBIChange> changes)
    {
        // Whole DataModel added or removed: out of scope for patch (would require full rebuild).
        if (from == null || to == null) return;

        CompareCollection(
            from.Tables, to.Tables,
            x => x.Id, OpenBIEntity.Table, parentId: null, changes,
            CompareTable);

        CompareCollection(
            from.Relationships, to.Relationships,
            x => x.Name, OpenBIEntity.Relationship, parentId: null, changes,
            CompareRelationship);
    }

    // ─── Table ─────────────────────────────────────────────────────────────────

    private static void CompareTable(Table from, Table to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "name", from.Name, to.Name);
        AddPart(parts, "type", from.Type, to.Type);
        if (!ExpressionEqual(from.Expression, to.Expression))
            parts.Add(OpenBIChangePart.For("expression", to.Expression));
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.Table, id, parts));

        CompareCollection(
            from.Columns, to.Columns,
            x => x.Id, OpenBIEntity.Column, parentId: id, changes,
            CompareColumn);
    }

    // ─── Column ────────────────────────────────────────────────────────────────

    private static void CompareColumn(Column from, Column to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "name",          from.Name,          to.Name);
        AddPart(parts, "type",          from.Type,          to.Type);
        AddPart(parts, "description",   from.Description,   to.Description);
        AddPart(parts, "dataType",      from.DataType,      to.DataType);
        AddPart(parts, "summarizeBy",   from.SummarizeBy,   to.SummarizeBy);
        AddPart(parts, "formatString",  from.FormatString,  to.FormatString);
        AddPart(parts, "dataCategory",  from.DataCategory,  to.DataCategory);
        AddPart(parts, "isKey",         from.IsKey,         to.IsKey);
        AddPart(parts, "isUnique",      from.IsUnique,      to.IsUnique);
        AddPart(parts, "isNullable",    from.IsNullable,    to.IsNullable);
        AddPart(parts, "isDimension",   from.IsDimension,   to.IsDimension);
        AddPart(parts, "isMeasure",     from.IsMeasure,     to.IsMeasure);
        if (!ExpressionEqual(from.Expression, to.Expression))
            parts.Add(OpenBIChangePart.For("expression", to.Expression));
        if (!ColumnsReferencesEqual(from.ColumnsReferences, to.ColumnsReferences))
            parts.Add(OpenBIChangePart.For("columnsReferences", to.ColumnsReferences));
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.Column, id, parts));
    }

    // ─── Relationship ──────────────────────────────────────────────────────────

    private static void CompareRelationship(Relationship from, Relationship to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "name",         from.Name,         to.Name);
        AddPart(parts, "idColumnFrom", from.IdColumnFrom, to.IdColumnFrom);
        AddPart(parts, "idColumnTo",   from.IdColumnTo,   to.IdColumnTo);
        AddPart(parts, "type",         from.Type,         to.Type);
        if (!ExpressionEqual(from.Expression, to.Expression))
            parts.Add(OpenBIChangePart.For("expression", to.Expression));
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.Relationship, id, parts));
    }

    // ─── Layout ────────────────────────────────────────────────────────────────

    private static void CompareLayout(Layout? from, Layout? to, List<OpenBIChange> changes)
    {
        // Whole Layout added or removed: out of scope for patch (would require full rebuild).
        if (from == null || to == null) return;

        CompareCollection(
            from.Pages, to.Pages,
            x => x.Id, OpenBIEntity.Page, parentId: null, changes,
            ComparePage);
    }

    // ─── Page ──────────────────────────────────────────────────────────────────

    private static void ComparePage(Page from, Page to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "name",                  from.Name,                  to.Name);
        AddPart(parts, "order",                 from.Order,                 to.Order);
        AddPart(parts, "width",                 from.Width,                 to.Width);
        AddPart(parts, "height",                from.Height,                to.Height);
        AddPart(parts, "isEnabled",             from.IsEnabled,             to.IsEnabled);
        AddPart(parts, "description",           from.Description,           to.Description);
        AddPart(parts, "embedPageUrlParameter", from.EmbedPageUrlParameter, to.EmbedPageUrlParameter);
        AddPart(parts, "embedPageUrl",          from.EmbedPageUrl,          to.EmbedPageUrl);
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.Page, id, parts));

        CompareCollection(
            from.Visuals, to.Visuals,
            x => x.Id, OpenBIEntity.Visual, parentId: id, changes,
            CompareVisual);

        CompareCollection(
            from.PageLevelFilters, to.PageLevelFilters,
            x => x.Id, OpenBIEntity.Filter, parentId: id, changes,
            CompareFilter);
    }

    // ─── Visual ────────────────────────────────────────────────────────────────

    private static void CompareVisual(Visual from, Visual to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "name",             from.Name,             to.Name);
        AddPart(parts, "category",         from.Category,         to.Category);
        AddPart(parts, "type",             from.Type,             to.Type);
        AddPart(parts, "openBIVisualType", from.OpenBIVisualType, to.OpenBIVisualType);
        AddPart(parts, "x",               from.X,                to.X);
        AddPart(parts, "y",               from.Y,                to.Y);
        AddPart(parts, "z",               from.Z,                to.Z);
        AddPart(parts, "width",           from.Width,            to.Width);
        AddPart(parts, "height",          from.Height,           to.Height);
        AddPart(parts, "description",     from.Description,      to.Description);
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.Visual, id, parts));

        // VisualProjection: composite key = {visualId}::{projectionName}::{order}
        // Converters decode this via VisualProjectionKey.TryDecode — must use Encode here.
        CompareCollection(
            from.VisualProjections, to.VisualProjections,
            x => x.ProjectionName is null
                ? null
                : VisualProjectionKey.Encode(id, x.ProjectionName, x.Order),
            OpenBIEntity.VisualProjection, parentId: id, changes,
            CompareVisualProjection);

        CompareCollection(
            from.VisualLevelFilters, to.VisualLevelFilters,
            x => x.Id, OpenBIEntity.Filter, parentId: id, changes,
            CompareFilter);

        // Child visuals (container visuals). ParentId is the parent Visual's Id.
        CompareCollection(
            from.Children, to.Children,
            x => x.Id, OpenBIEntity.Visual, parentId: id, changes,
            CompareVisual);
    }

    // ─── VisualProjection ──────────────────────────────────────────────────────

    private static void CompareVisualProjection(VisualProjection from, VisualProjection to, string id, List<OpenBIChange> changes)
    {
        // NOTE: `id` is the composite VisualProjectionKey ({visualId}::{projName}::{order}).
        // Projections are matched by projName+order, so Order can never differ within the
        // intersect — omit it from Replace parts.
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "openBIProjectionName", from.OpenBIProjectionName, to.OpenBIProjectionName);
        AddPart(parts, "isActive",             from.IsActive,             to.IsActive);
        AddPart(parts, "type",                 from.Type,                 to.Type);
        AddPart(parts, "isDimension",          from.IsDimension,          to.IsDimension);
        AddPart(parts, "isMeasure",            from.IsMeasure,            to.IsMeasure);
        AddPart(parts, "idColumnReference",    from.IdColumnReference,    to.IdColumnReference);
        if (!ExpressionEqual(from.Expression, to.Expression))
            parts.Add(OpenBIChangePart.For("expression", to.Expression));
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.VisualProjection, id, parts));
    }

    // ─── Filter ────────────────────────────────────────────────────────────────

    private static void CompareFilter(Filter from, Filter to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "idColumn",        from.IdColumn,        to.IdColumn);
        AddPart(parts, "function",        from.Function,        to.Function);
        AddPart(parts, "functionName",    from.FunctionName,    to.FunctionName);
        AddPart(parts, "logicalOperator", from.LogicalOperator, to.LogicalOperator);
        AddPart(parts, "isGroup",         from.IsGroup,         to.IsGroup);
        if (!ExpressionEqual(from.Expression, to.Expression))
            parts.Add(OpenBIChangePart.For("expression", to.Expression));
        if (!StringListEqual(from.Values, to.Values))
            parts.Add(OpenBIChangePart.For("values", to.Values));
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.Filter, id, parts));

        // Recursive child filters
        CompareCollection(
            from.Children, to.Children,
            x => x.Id, OpenBIEntity.Filter, parentId: id, changes,
            CompareFilter);
    }

    // ─── RefreshTask ───────────────────────────────────────────────────────────

    private static void CompareRefreshTask(RefreshTask from, RefreshTask to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        if (!AdditionalMetadataEqual(from.AdditionalMetadata, to.AdditionalMetadata))
            parts.Add(OpenBIChangePart.For("additionalMetadata", to.AdditionalMetadata));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.RefreshTask, id, parts));

        CompareCollection(
            from.Triggers, to.Triggers,
            x => x.Id, OpenBIEntity.RefreshTrigger, parentId: id, changes,
            CompareRefreshTrigger);
    }

    // ─── RefreshTrigger ────────────────────────────────────────────────────────

    private static void CompareRefreshTrigger(RefreshTrigger from, RefreshTrigger to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "type", from.Type, to.Type);
        if (!ScheduleParametersEqual(from.ScheduleParameters, to.ScheduleParameters))
            parts.Add(OpenBIChangePart.For("scheduleParameters", to.ScheduleParameters));
        if (!CompositeParametersEqual(from.CompositeParameters, to.CompositeParameters))
            parts.Add(OpenBIChangePart.For("compositeParameters", to.CompositeParameters));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.RefreshTrigger, id, parts));
    }

    // ─── DataSourceConnection ──────────────────────────────────────────────────

    private static void CompareDataSourceConnection(DataSourceConnection from, DataSourceConnection to, string id, List<OpenBIChange> changes)
    {
        var parts = new List<OpenBIChangePart>();
        AddPart(parts, "externalId", from.ExternalId, to.ExternalId);
        AddPart(parts, "type",       from.Type,       to.Type);
        if (!DictionaryEqual(from.Parameters, to.Parameters))
            parts.Add(OpenBIChangePart.For("parameters", to.Parameters));

        if (parts.Count > 0)
            changes.Add(OpenBIChange.Replace(OpenBIEntity.DataSourceConnection, id, parts));
    }

    // ─── Collection helper ─────────────────────────────────────────────────────

    private static void CompareCollection<T>(
        IEnumerable<T>? from,
        IEnumerable<T>? to,
        Func<T, string?> keySelector,
        OpenBIEntity entity,
        string? parentId,
        List<OpenBIChange> changes,
        Action<T, T, string, List<OpenBIChange>>? compareProperties = null)
    {
        var fromDict = (from ?? Enumerable.Empty<T>())
            .Where(x => keySelector(x) is not null)
            .ToDictionary(x => keySelector(x)!);

        var toDict = (to ?? Enumerable.Empty<T>())
            .Where(x => keySelector(x) is not null)
            .ToDictionary(x => keySelector(x)!);

        foreach (var key in fromDict.Keys.Except(toDict.Keys))
            changes.Add(OpenBIChange.Remove(entity, key));

        foreach (var key in toDict.Keys.Except(fromDict.Keys))
            changes.Add(OpenBIChange.Add(entity, parentId!, toDict[key]));

        if (compareProperties is not null)
            foreach (var key in fromDict.Keys.Intersect(toDict.Keys))
                compareProperties(fromDict[key], toDict[key], key, changes);
    }

    // ─── Scalar property helper ────────────────────────────────────────────────

    private static void AddPart<T>(List<OpenBIChangePart> parts, string property, T from, T to)
    {
        if (!EqualityComparer<T>.Default.Equals(from, to))
            parts.Add(OpenBIChangePart.For(property, to));
    }

    // ─── Structural equality helpers ───────────────────────────────────────────

    private static bool ExpressionEqual(Expression? a, Expression? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Id       == b.Id
            && a.Code     == b.Code
            && a.Language == b.Language
            && a.Type     == b.Type
            && AdditionalMetadataEqual(a.AdditionalMetadata, b.AdditionalMetadata);
    }

    /// <summary>
    /// Order-independent comparison. Null and empty are treated as equal.
    /// Duplicate Names: first occurrence wins (consistent with dictionary semantics).
    /// </summary>
    private static bool AdditionalMetadataEqual(ICollection<AdditionalMetadata>? a, ICollection<AdditionalMetadata>? b)
    {
        var aEmpty = a is null || a.Count == 0;
        var bEmpty = b is null || b.Count == 0;
        if (aEmpty && bEmpty) return true;
        if (aEmpty || bEmpty) return false;

        if (a!.Count != b!.Count) return false;

        var aDict = a.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        var bDict = b.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        if (aDict.Count != bDict.Count) return false;
        return aDict.All(kv => bDict.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    /// <summary>Order-sensitive. Null and empty are treated as equal.</summary>
    private static bool ColumnsReferencesEqual(ICollection<ColumnsReference>? a, ICollection<ColumnsReference>? b)
    {
        var aList = a?.ToList() ?? new List<ColumnsReference>();
        var bList = b?.ToList() ?? new List<ColumnsReference>();
        if (aList.Count != bList.Count) return false;
        return aList.Zip(bList).All(p => p.First.IdColumn == p.Second.IdColumn);
    }

    /// <summary>Order-sensitive. Null and empty are treated as equal.</summary>
    private static bool StringListEqual(List<string>? a, List<string>? b)
    {
        var aList = a ?? new List<string>();
        var bList = b ?? new List<string>();
        if (aList.Count != bList.Count) return false;
        return aList.Zip(bList).All(p => p.First == p.Second);
    }

    private static bool ScheduleParametersEqual(ScheduleParameters? a, ScheduleParameters? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.CronExpression == b.CronExpression;
    }

    private static bool CompositeParametersEqual(CompositeParameters? a, CompositeParameters? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return StringListEqual(a.DependentRefreshTaskIds, b.DependentRefreshTaskIds);
    }

    /// <summary>Key-value equal. Null and empty are treated as equal.</summary>
    private static bool DictionaryEqual(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        var aMap = a ?? new Dictionary<string, string>();
        var bMap = b ?? new Dictionary<string, string>();
        if (aMap.Count != bMap.Count) return false;
        return aMap.All(kv => bMap.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }
}
