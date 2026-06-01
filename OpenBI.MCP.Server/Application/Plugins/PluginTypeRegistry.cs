using System.Collections.Concurrent;

namespace OpenBI.MCP.Server.Application.Plugins;

/// <summary>
/// Holds all <see cref="Type"/> objects loaded from plugin assemblies, keyed by their
/// simple assembly-qualified name (<c>Namespace.TypeName, AssemblyName</c>).
/// Populated by <see cref="PluginLoader"/> at startup — before any DI resolution.
/// </summary>
public sealed class PluginTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _types =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers <paramref name="type"/> under its simple assembly-qualified key
    /// (<c>FullName, AssemblyName</c> without version/culture/token).
    /// </summary>
    public void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var key = BuildKey(type);
        _types[key] = type;
    }

    /// <summary>
    /// Resolves a <see cref="Type"/> by name. Checks the plugin registry first (exact match on
    /// simple key), then falls back to <see cref="Type.GetType(string, bool)"/> for types that
    /// live in the default <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
    /// Returns <see langword="null"/> if the name is not found.
    /// </summary>
    public Type? Resolve(string assemblyQualifiedTypeName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
            return null;

        var key = assemblyQualifiedTypeName.Trim();
        if (_types.TryGetValue(key, out var type))
            return type;

        // Strip version/culture/publicKeyToken for a looser lookup
        var simplifiedKey = SimplifyKey(key);
        if (!string.Equals(simplifiedKey, key, StringComparison.OrdinalIgnoreCase)
            && _types.TryGetValue(simplifiedKey, out type))
            return type;

        // Fall back: type already in the default ALC (e.g. JsonFileSecretsVaultRepository itself)
        return Type.GetType(key, throwOnError: false);
    }

    /// <summary>
    /// Returns all registered types.
    /// </summary>
    public IEnumerable<Type> All() => _types.Values;

    private static string BuildKey(Type type)
    {
        // Produce "Namespace.TypeName, AssemblyName" (no version/culture/token)
        var assemblyName = type.Assembly.GetName().Name ?? type.Assembly.FullName ?? "";
        return $"{type.FullName}, {assemblyName}";
    }

    /// <summary>
    /// Strips version, culture, and publicKeyToken tokens so that
    /// <c>Foo.Bar, MyAssembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null</c>
    /// becomes <c>Foo.Bar, MyAssembly</c>.
    /// </summary>
    private static string SimplifyKey(string name)
    {
        var idx = name.IndexOf(',');
        if (idx < 0) return name;

        var typePart = name[..idx].Trim();
        var rest = name[(idx + 1)..];
        var asmIdx = rest.IndexOf(',');
        var asmName = (asmIdx < 0 ? rest : rest[..asmIdx]).Trim();

        return $"{typePart}, {asmName}";
    }
}
