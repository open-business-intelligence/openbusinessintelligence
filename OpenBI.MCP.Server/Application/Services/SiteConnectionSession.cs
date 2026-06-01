using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenBI.Connectors.Interfaces;
using OpenBI.Interfaces.Infrastructure;
using OpenBI.Interfaces.Sites;
using OpenBI.MCP.Server.Application.Plugins;

namespace OpenBI.MCP.Server.Application.Services;

/// <summary>
/// Resolves <see cref="ISiteConnection"/> for an MCP session using cached site config and vault secrets.
/// The factory is resolved according to <see cref="RegisteredSite.SiteConnectionFactoryScope"/>:
/// for "Singleton" (default) the cached <see cref="SiteConnectionFactoryActivator"/> instance is used;
/// for "Scoped" the factory type is resolved via the request scope's <see cref="IServiceProvider"/>.
/// </summary>
public sealed class SiteConnectionSession
{
    private readonly ISecretsVaultRepository _secretsVault;
    private readonly SiteConnectionFactoryActivator _factoryActivator;
    private readonly PluginTypeRegistry _pluginRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SiteConnectionSession> _logger;

    public SiteConnectionSession(
        ISecretsVaultRepository secretsVault,
        SiteConnectionFactoryActivator factoryActivator,
        PluginTypeRegistry pluginRegistry,
        IServiceProvider serviceProvider,
        ILogger<SiteConnectionSession> logger)
    {
        _secretsVault = secretsVault;
        _factoryActivator = factoryActivator;
        _pluginRegistry = pluginRegistry;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ISiteConnection> OpenConnectionAsync(RegisteredSite site, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> connectionParams;
        if (string.IsNullOrWhiteSpace(site.PlatformSecretsPath))
        {
            connectionParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var secretData = await _secretsVault.GetSecretAsync(site.PlatformSecretsPath!, cancellationToken).ConfigureAwait(false);
            if (secretData == null || secretData.Count == 0)
                throw new InvalidOperationException(
                    $"No connection parameters found at secrets path '{site.PlatformSecretsPath}'.");

            connectionParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in secretData)
                connectionParams[kvp.Key] = kvp.Value?.ToString() ?? "";
        }

        var factory = ResolveFactory(site);
        var connection = factory.CreateConnection(connectionParams);
        _logger.LogDebug("Opened ISiteConnection for site {IdSite}", site.IdSite);
        return connection;
    }

    private ISiteConnectionFactory ResolveFactory(RegisteredSite site)
    {
        var scope = (site.SiteConnectionFactoryScope ?? "Singleton").Trim();

        if (string.Equals(scope, "Scoped", StringComparison.OrdinalIgnoreCase))
        {
            var factoryType = _pluginRegistry.Resolve(site.SiteConnectionFactoryName)
                ?? throw new InvalidOperationException(
                    $"Cannot resolve factory type '{site.SiteConnectionFactoryName}'. Ensure the plugin DLL is placed in the plugins/ directory and the name matches an ISiteConnectionFactory implementation.");

            if (!typeof(ISiteConnectionFactory).IsAssignableFrom(factoryType))
                throw new InvalidOperationException(
                    $"Type '{factoryType.FullName}' does not implement ISiteConnectionFactory.");

            return (ISiteConnectionFactory)_serviceProvider.GetRequiredService(factoryType);
        }

        return _factoryActivator.GetFactory(site.SiteConnectionFactoryName);
    }
}
