using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenBI.Interfaces.Sites;
using OpenBI.MCP.Server.Application.Plugins;
using OpenBI.MCP.Server.Application.Sites;

namespace OpenBI.MCP.Server.Extensions;

/// <summary>
/// Options for <see cref="ISiteRegistry"/> registration (see <c>SiteRegistry</c> section in appsettings.json).
/// </summary>
public sealed class SiteRegistryOptions
{
    public const string SectionName = "SiteRegistry";

    /// <summary>
    /// Assembly-qualified type name implementing <see cref="ISiteRegistry"/>.
    /// If empty (default), <see cref="FileSiteRegistry"/> is used.
    /// </summary>
    public string? ImplementationType { get; set; }
}

public static class SiteRegistryServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISiteRegistry"/> driven by <c>SiteRegistry:ImplementationType</c> in appsettings.json.
    /// When the value is empty or absent, the built-in <see cref="FileSiteRegistry"/> is used.
    /// When a non-empty type name is provided, it is resolved from the <see cref="PluginTypeRegistry"/>
    /// (populated by <see cref="PluginLoader"/> at startup) and instantiated via
    /// <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type, object[])"/>.
    /// </summary>
    public static IServiceCollection AddConfiguredSiteRegistry(this IServiceCollection services)
    {
        services.AddSingleton<ISiteRegistry>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SiteRegistryOptions>>().Value;
            var typeName = opts.ImplementationType;

            if (string.IsNullOrWhiteSpace(typeName))
                return FileSiteRegistry.Load(
                    AppContext.BaseDirectory,
                    sp.GetRequiredService<ILogger<FileSiteRegistry>>());

            var pluginRegistry = sp.GetRequiredService<PluginTypeRegistry>();
            var type = pluginRegistry.Resolve(typeName);
            if (type == null)
                throw new InvalidOperationException(
                    $"SiteRegistry:ImplementationType '{typeName}' could not be loaded. Ensure the plugin DLL is placed in the plugins/ directory.");

            if (!typeof(ISiteRegistry).IsAssignableFrom(type))
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' does not implement ISiteRegistry.");

            return (ISiteRegistry)ActivatorUtilities.CreateInstance(sp, type);
        });

        return services;
    }
}
