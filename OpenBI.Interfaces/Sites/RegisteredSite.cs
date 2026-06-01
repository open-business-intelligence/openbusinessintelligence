namespace OpenBI.Interfaces.Sites;

/// <summary>
/// Site definition for MCP (from file registry or an external registry implementation).
/// </summary>
public sealed class RegisteredSite
{
    public string IdSite { get; init; } = "";
    public string SiteName { get; init; } = "";
    public string IdPlatform { get; init; } = "";
    public string PlatformName { get; init; } = "";
    public string? PlatformSecretsPath { get; init; }
    public string SiteConnectionFactoryName { get; init; } = "";

    /// <summary>
    /// Lifetime for <see cref="SiteConnectionFactoryName"/>: <c>Singleton</c> (default) or <c>Scoped</c> (case-insensitive).
    /// </summary>
    public string? SiteConnectionFactoryScope { get; init; }

    public string? SiteConverterFactoryName { get; init; }
}
