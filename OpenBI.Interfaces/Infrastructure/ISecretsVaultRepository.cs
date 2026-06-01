namespace OpenBI.Interfaces.Infrastructure;

/// <summary>
/// Repository for storing and retrieving secrets (credentials, connection parameters, API keys).
/// </summary>
public interface ISecretsVaultRepository
{
    Task CreateSecretAsync(string path, Dictionary<string, object> data, CancellationToken cancellationToken = default);
    Task UpdateSecretAsync(string path, Dictionary<string, object> data, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object>?> GetSecretAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> SecretExistsAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteSecretAsync(string path, CancellationToken cancellationToken = default);
}
