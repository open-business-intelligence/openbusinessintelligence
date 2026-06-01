using System.Text.Json;
using OpenBI.Interfaces.Infrastructure;

namespace OpenBI.Common.SecretsVault;

/// <summary>
/// Stores secrets as JSON files on disk. Each secret path maps to a .json file containing a flat key/value object.
/// Suitable for local development and open-source deployments without an external secrets manager.
/// </summary>
public sealed class FileBasedSecretsVaultRepository : ISecretsVaultRepository
{
    private readonly string _baseDirectory;

    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    public FileBasedSecretsVaultRepository(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public FileBasedSecretsVaultRepository() : this(AppContext.BaseDirectory) { }

    public Task<Dictionary<string, object>?> GetSecretAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(path);
        if (!File.Exists(fullPath))
            return Task.FromResult<Dictionary<string, object>?>(null);

        var json = File.ReadAllText(fullPath);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return Task.FromResult<Dictionary<string, object>?>(null);

        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = ElementToObject(prop.Value);

        return Task.FromResult<Dictionary<string, object>?>(dict);
    }

    public Task<bool> SecretExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(ResolvePath(path)));
    }

    public async Task CreateSecretAsync(string path, Dictionary<string, object> data, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(data, _writeOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSecretAsync(string path, Dictionary<string, object> data, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        Dictionary<string, object> merged = new(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(fullPath))
        {
            var existing = await GetSecretAsync(path, cancellationToken).ConfigureAwait(false);
            if (existing != null)
                foreach (var kv in existing)
                    merged[kv.Key] = kv.Value;
        }

        foreach (var kv in data)
            merged[kv.Key] = kv.Value;

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(merged, _writeOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteSecretAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string ResolvePath(string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            normalized += ".json";
        var full = Path.GetFullPath(Path.Combine(_baseDirectory, normalized));
        if (!full.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Secret path escapes base directory: {path}");
        return full;
    }

    private static object ElementToObject(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String  => el.GetString() ?? "",
            JsonValueKind.Number  => el.GetRawText(),
            JsonValueKind.True    => true,
            JsonValueKind.False   => false,
            JsonValueKind.Null    => "",
            _                     => el.GetRawText()
        };
}
