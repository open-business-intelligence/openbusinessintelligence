using OpenBI.Converters.Interfaces;
using OpenBI.Converters.PowerBI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace OpenBI.Converters.PowerBI;

/// <summary>
/// Extension methods for registering compression services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers PowerBI OpenBI Converters.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration instance</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPowerBIOpenBIConverters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        
        services.AddScoped<PowerBIOpenBIConverterFactory>();

        return services;
    }    
}
