using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenBI.Interfaces.Infrastructure;
using OpenBI.MCP.Server.Application.Plugins;

namespace OpenBI.MCP.Server.Infrastructure.Secrets;

public static class SecretsVaultServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISecretsVaultRepository"/> from <see cref="SecretsVaultOptions.ImplementationType"/> (assembly-qualified name).
    /// Uses <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type, object[])"/> so implementations can take DI services (e.g. <see cref="IOptions{SecretsVaultOptions}"/>, <see cref="ILogger{T}"/>, <see cref="IConfiguration"/>).
    /// </summary>
    public static IServiceCollection AddConfiguredSecretsVault(this IServiceCollection services)
    {
        services.AddSingleton<ISecretsVaultRepository>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SecretsVaultOptions>>().Value;
            var typeName = opts.ImplementationType;
            if (string.IsNullOrWhiteSpace(typeName))
                typeName = typeof(JsonFileSecretsVaultRepository).AssemblyQualifiedName!;

            var pluginRegistry = sp.GetRequiredService<PluginTypeRegistry>();
            var type = pluginRegistry.Resolve(typeName);
            if (type == null)
                throw new InvalidOperationException(
                    $"SecretsVault:ImplementationType '{typeName}' could not be loaded. Ensure the plugin DLL is placed in the plugins/ directory or use the built-in JsonFileSecretsVaultRepository.");

            if (!typeof(ISecretsVaultRepository).IsAssignableFrom(type))
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' does not implement ISecretsVaultRepository.");

            var instance = ActivatorUtilities.CreateInstance(sp, type);
            return (ISecretsVaultRepository)instance;
        });

        return services;
    }
}
