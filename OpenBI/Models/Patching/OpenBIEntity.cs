namespace OpenBI.Patching;

/// <summary>
/// Identifies the type of OpenBI entity targeted by a change.
/// For entities without an Id field, the Id in <see cref="OpenBIChange"/> represents
/// the natural key: Relationship → Name, DataSourceConnection → Name,
/// VisualProjection → ProjectionName.
/// </summary>
public enum OpenBIEntity
{
    AssetInfo,
    Table,
    Column,
    Relationship,
    Page,
    Visual,
    VisualProjection,
    Filter,
    RefreshTask,
    RefreshTrigger,
    DataSourceConnection
}
