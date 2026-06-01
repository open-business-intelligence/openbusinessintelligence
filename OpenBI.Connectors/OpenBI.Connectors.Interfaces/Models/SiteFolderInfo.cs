namespace OpenBI.Connectors.Interfaces.Models;

/// <summary>
/// Generic folder entry (e.g. workspace, app folder) returned by <see cref="ISiteConnection.GetSiteFoldersAsync"/>.
/// </summary>
public class SiteFolderInfo
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Type { get; set; }
    public string? FullPath { get; set; }
}
