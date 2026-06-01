using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using OpenBI.Interfaces.Infrastructure;
using OpenBI.Interfaces.Sites;
using OpenBI.Connectors.Interfaces;
using OpenBI.Converters.Interfaces;

namespace OpenBI.MCP.Server.Application.Plugins;

/// <summary>
/// Scans every subfolder of <c>plugins/</c> under the host base directory and loads each
/// plugin into an isolated <see cref="AssemblyLoadContext"/>.
/// All public types that implement any of the four extensibility interfaces
/// (<see cref="ISiteConnectionFactory"/>, <see cref="IOpenBIConverterFactory"/>,
/// <see cref="ISiteRegistry"/>, <see cref="ISecretsVaultRepository"/>)
/// are registered in the shared <see cref="PluginTypeRegistry"/>.
/// </summary>
public sealed class PluginLoader
{
    /// <summary>
    /// Shared interface assemblies that must be resolved from the DEFAULT
    /// <see cref="AssemblyLoadContext"/> so that <c>IsAssignableFrom</c> checks
    /// across load-context boundaries succeed.
    /// </summary>
    private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "OpenBI",
        "OpenBI.Interfaces",
        "OpenBI.Connectors.Interfaces",
        "OpenBI.Converters.Interfaces",
    };

    private static readonly Type[] ExtensibilityInterfaces =
    [
        typeof(ISiteConnectionFactory),
        typeof(IOpenBIConverterFactory),
        typeof(ISiteRegistry),
        typeof(ISecretsVaultRepository),
    ];

    private readonly PluginTypeRegistry _registry;
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(PluginTypeRegistry registry, ILogger<PluginLoader> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Scans <paramref name="pluginsBaseDirectory"/> for plugin subfolders and loads them.
    /// Silently skips subfolders that do not contain a valid plugin.
    /// </summary>
    public void LoadAll(string pluginsBaseDirectory)
    {
        if (!Directory.Exists(pluginsBaseDirectory))
        {
            _logger.LogDebug("Plugin directory not found â€” no plugins loaded: {Path}", pluginsBaseDirectory);
            return;
        }

        foreach (var pluginDir in Directory.EnumerateDirectories(pluginsBaseDirectory))
        {
            var dirName = Path.GetFileName(pluginDir);
            try
            {
                LoadPlugin(pluginDir, dirName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin from '{Dir}' â€” skipped.", pluginDir);
            }
        }
    }

    private void LoadPlugin(string pluginDir, string dirName)
    {
        // Convention: the main DLL has the same name as the directory
        var depsFile = Directory.EnumerateFiles(pluginDir, "*.deps.json", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        var mainDll = Path.Combine(pluginDir, dirName + ".dll");

        if (!File.Exists(mainDll))
        {
            _logger.LogDebug("No entry DLL found in '{Dir}' â€” skipped.", pluginDir);
            return;
        }

        var ctx = new PluginLoadContext(mainDll, depsFile);
        var assembly = ctx.LoadFromAssemblyPath(mainDll);

        var registered = 0;
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!ImplementsAnyExtensibilityInterface(type)) continue;
            _registry.Register(type);
            registered++;
            _logger.LogDebug("Plugin type registered: {Type} from {Assembly}", type.FullName, assembly.GetName().Name);
        }

        _logger.LogInformation("Plugin loaded: {Dir} â€” {Count} type(s) registered.", dirName, registered);
    }

    private static bool ImplementsAnyExtensibilityInterface(Type type)
    {
        foreach (var iface in ExtensibilityInterfaces)
            if (iface.IsAssignableFrom(type))
                return true;
        return false;
    }

    // â”€â”€â”€ Isolated load context â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver? _resolver;

        public PluginLoadContext(string mainDllPath, string? depsJsonPath)
            : base(name: Path.GetFileNameWithoutExtension(mainDllPath), isCollectible: false)
        {
            // Use deps.json if available; fall back to directory-relative probing
            var resolverPath = depsJsonPath ?? mainDllPath;
            try
            {
                _resolver = new AssemblyDependencyResolver(resolverPath);
            }
            catch
            {
                _resolver = null;
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // 1. Explicit allowlist â€” always route our shared contract assemblies through
            //    the default ALC so IsAssignableFrom checks work across plugin boundaries.
            if (assemblyName.Name != null
                && SharedAssemblyNames.Contains(assemblyName.Name))
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }

            // 2. If the assembly is already loaded in the default ALC, reuse it.
            //    This prevents type-identity mismatches for ANY shared runtime dep
            //    (e.g. Microsoft.Extensions.Logging.Abstractions, System.Text.Jsonâ€¦)
            //    whose types appear in shared interface signatures.
            if (assemblyName.Name != null)
            {
                var already = Default.Assemblies.FirstOrDefault(
                    a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
                if (already != null)
                    return already;
            }

            // 3. Resolve plugin-private dependencies via deps.json.
            if (_resolver != null)
            {
                var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
                if (resolved != null)
                    return LoadFromAssemblyPath(resolved);
            }

            // 4. Let the runtime fall back (shouldn't normally reach here).
            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            if (_resolver != null)
            {
                var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                if (path != null)
                    return LoadUnmanagedDllFromPath(path);
            }

            return IntPtr.Zero;
        }
    }
}
