using System.Collections.Concurrent;
using OpenBI.Converters.Interfaces;

namespace OpenBI.MCP.Server.Application.Services;

/// <summary>
/// Wraps a platform <see cref="IOpenBIConverterFactory"/> and caches one <see cref="IOpenBIConverter"/> per asset type string
/// Caches one instance per asset-type string so converter creation is O(1) after the first call.
/// </summary>
public sealed class CachingOpenBIConverterFactory : IOpenBIConverterFactory
{
    private readonly IOpenBIConverterFactory _inner;
    private readonly ConcurrentDictionary<string, IOpenBIConverter> _cache = new(StringComparer.Ordinal);

    public CachingOpenBIConverterFactory(IOpenBIConverterFactory inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public IOpenBIConverter CreateOpenBIConverter(string assetType)
    {
        var key = assetType ?? string.Empty;
        return _cache.GetOrAdd(key, k => _inner.CreateOpenBIConverter(k));
    }
}
