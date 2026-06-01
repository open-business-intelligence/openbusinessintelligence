using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenBI.Interfaces.Infrastructure;

namespace OpenBI.MCP.Server.Infrastructure.Secrets;

/// <summary>
/// Reads secrets from a JSON file on disk (root object = key/value map). Implements <see cref="ISecretsVaultRepository"/> for parity with Gateway/OpenBao.
/// </summary>
public sealed class JsonFileSecretsVaultRepository : ISecretsVaultRepository
{
    private readonly string _baseDirectory;

    /// <summary>
    /// Uses <see cref="SecretsVaultOptions.BaseDirectory"/> or falls back to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public JsonFileSecretsVaultRepository(IOptions<SecretsVaultOptions> options)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.BaseDirectory))
            _baseDirectory = AppContext.BaseDirectory;
        else
            _baseDirectory = Path.IsPathRooted(o.BaseDirectory)
                ? o.BaseDirectory
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, o.BaseDirectory));
    }

    /// <summary>
    /// Explicit base directory (e.g. tests).
    /// </summary>
    public JsonFileSecretsVaultRepository(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public Task<Dictionary<string, object>?> GetSecretAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_baseDirectory, path);
        if (!File.Exists(fullPath))
            return Task.FromResult<Dictionary<string, object>?>(null);

        var json = File.ReadAllText(fullPath);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return Task.FromResult<Dictionary<string, object>?>(null);

        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = JsonValueToObject(prop.Value);

        return Task.FromResult<Dictionary<string, object>?>(dict);
    }

    private static object JsonValueToObject(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => "",
            JsonValueKind.Array => el.GetRawText(),
            JsonValueKind.Object => el.GetRawText(),
            _ => el.GetRawText()
        };

    public Task CreateSecretAsync(string path, Dictionary<string, object> data, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JsonFileSecretsVaultRepository is read-only in MCP.");

    public Task UpdateSecretAsync(string path, Dictionary<string, object> data, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JsonFileSecretsVaultRepository is read-only in MCP.");

    public Task<bool> SecretExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_baseDirectory, path);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task DeleteSecretAsync(string path, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("JsonFileSecretsVaultRepository is read-only in MCP.");
}
