using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenBI.Connectors.Interfaces;
using OpenBI.MCP.Server.Application.Plugins;

namespace OpenBI.MCP.Server.Application.Services;

/// <summary>
/// Returns a singleton <see cref="ISiteConnectionFactory"/> per distinct assembly-qualified factory type name
/// (same value as site JSON <c>siteConnectionFactoryName</c>). First use instantiates via <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type)"/>; subsequent calls reuse the same instance.
/// </summary>
public sealed class SiteConnectionFactoryActivator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PluginTypeRegistry _pluginRegistry;
    private readonly ILogger<SiteConnectionFactoryActivator> _logger;
    private readonly ConcurrentDictionary<string, Lazy<ISiteConnectionFactory>> _factories = new(StringComparer.Ordinal);

    public SiteConnectionFactoryActivator(
        IServiceProvider serviceProvider,
        PluginTypeRegistry pluginRegistry,
        ILogger<SiteConnectionFactoryActivator> logger)
    {
        _serviceProvider = serviceProvider;
        _pluginRegistry = pluginRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Gets the cached factory for the given assembly-qualified type name, creating it on first use.
    /// </summary>
    public ISiteConnectionFactory GetFactory(string assemblyQualifiedTypeName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
            throw new ArgumentException("Factory type name is required.", nameof(assemblyQualifiedTypeName));

        var key = NormalizeKey(assemblyQualifiedTypeName);
        var lazy = _factories.GetOrAdd(
            key,
            k => new Lazy<ISiteConnectionFactory>(
                () => CreateFactoryInstanceOnce(k),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    private static string NormalizeKey(string assemblyQualifiedTypeName) =>
        assemblyQualifiedTypeName.Trim();

    private ISiteConnectionFactory CreateFactoryInstanceOnce(string assemblyQualifiedTypeName)
    {
        var type = _pluginRegistry.Resolve(assemblyQualifiedTypeName);
        if (type == null)
            throw new InvalidOperationException(
                $"Cannot resolve type '{assemblyQualifiedTypeName}'. Ensure the plugin DLL is placed in the plugins/ directory and the name matches an ISiteConnectionFactory implementation.");

        if (!typeof(ISiteConnectionFactory).IsAssignableFrom(type))
            throw new InvalidOperationException($"Type '{type.FullName}' does not implement ISiteConnectionFactory.");

        try
        {
            var instance = ActivatorUtilities.CreateInstance(_serviceProvider, type);
            if (instance is not ISiteConnectionFactory factory)
                throw new InvalidOperationException($"ActivatorUtilities returned unexpected type for '{type.FullName}'.");

            _logger.LogInformation("Registered singleton ISiteConnectionFactory: {Type}", type.FullName);
            return factory;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to create factory {Type}", type.FullName);
            throw new InvalidOperationException($"Failed to create factory '{assemblyQualifiedTypeName}'. See inner exception.", ex);
        }
    }
}
