namespace OpenBI.Connectors.Interfaces.Models;

/// <summary>
/// Request to create a folder on the BI site (e.g. new workspace name).
/// </summary>
public class CreateSiteFolderRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}
