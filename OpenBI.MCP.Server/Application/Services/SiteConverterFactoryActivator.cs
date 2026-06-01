using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenBI.Converters.Interfaces;
using OpenBI.MCP.Server.Application.Plugins;

namespace OpenBI.MCP.Server.Application.Services;

/// <summary>
/// Returns a singleton <see cref="IOpenBIConverterFactory"/> (with per-asset-type converter cache) per distinct assembly-qualified factory type name
/// (same value as site JSON <c>siteConverterFactoryName</c>). First use instantiates via <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type)"/> and wraps with <see cref="CachingOpenBIConverterFactory"/>.
/// </summary>
public sealed class SiteConverterFactoryActivator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PluginTypeRegistry _pluginRegistry;
    private readonly ILogger<SiteConverterFactoryActivator> _logger;
    private readonly ConcurrentDictionary<string, Lazy<IOpenBIConverterFactory>> _factories = new(StringComparer.Ordinal);

    public SiteConverterFactoryActivator(
        IServiceProvider serviceProvider,
        PluginTypeRegistry pluginRegistry,
        ILogger<SiteConverterFactoryActivator> logger)
    {
        _serviceProvider = serviceProvider;
        _pluginRegistry = pluginRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Gets the cached factory for the given assembly-qualified type name, creating it on first use.
    /// </summary>
    public IOpenBIConverterFactory GetCachedFactory(string assemblyQualifiedTypeName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
            throw new ArgumentException("Factory type name is required.", nameof(assemblyQualifiedTypeName));

        var key = assemblyQualifiedTypeName.Trim();
        var lazy = _factories.GetOrAdd(
            key,
            k => new Lazy<IOpenBIConverterFactory>(
                () => CreateFactoryInstanceOnce(k),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    private IOpenBIConverterFactory CreateFactoryInstanceOnce(string assemblyQualifiedTypeName)
    {
        var type = _pluginRegistry.Resolve(assemblyQualifiedTypeName);
        if (type == null)
            throw new InvalidOperationException(
                $"Cannot resolve type '{assemblyQualifiedTypeName}'. Ensure the plugin DLL is placed in the plugins/ directory and the name matches an IOpenBIConverterFactory implementation.");

        if (!typeof(IOpenBIConverterFactory).IsAssignableFrom(type))
            throw new InvalidOperationException($"Type '{type.FullName}' does not implement IOpenBIConverterFactory.");

        try
        {
            var instance = ActivatorUtilities.CreateInstance(_serviceProvider, type);
            if (instance is not IOpenBIConverterFactory inner)
                throw new InvalidOperationException($"ActivatorUtilities returned unexpected type for '{type.FullName}'.");

            _logger.LogInformation("Registered singleton IOpenBIConverterFactory (cached): {Type}", type.FullName);
            return new CachingOpenBIConverterFactory(inner);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to create converter factory {Type}", type.FullName);
            throw new InvalidOperationException($"Failed to create converter factory '{assemblyQualifiedTypeName}'. See inner exception.", ex);
        }
    }
}
