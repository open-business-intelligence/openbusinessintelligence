namespace OpenBI.MCP.Server.Infrastructure.Secrets;

/// <summary>
/// Configuration for <see cref="OpenBI.Interfaces.Infrastructure.ISecretsVaultRepository"/> registration (see <c>SecretsVault</c> section in appsettings.json).
/// </summary>
public sealed class SecretsVaultOptions
{
    public const string SectionName = "SecretsVault";

    /// <summary>
    /// Assembly-qualified type name implementing <see cref="OpenBI.Interfaces.Infrastructure.ISecretsVaultRepository"/>.
    /// If empty, <see cref="JsonFileSecretsVaultRepository"/> is used.
    /// </summary>
    public string? ImplementationType { get; set; }

    /// <summary>
    /// Base directory for file-based vault implementations (relative paths are resolved from <see cref="AppContext.BaseDirectory"/>).
    /// </summary>
    public string? BaseDirectory { get; set; }
}
