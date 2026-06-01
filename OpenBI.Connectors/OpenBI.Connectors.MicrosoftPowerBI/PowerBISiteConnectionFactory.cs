using OpenBI.Interfaces.Infrastructure;
using OpenBI.Connectors.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace OpenBI.Connectors.PowerBI
{
    /// <summary>
    /// Factory for creating PowerBISiteConnection instances.
    /// </summary>
    public class PowerBISiteConnectionFactory : ISiteConnectionFactory
    {
        private readonly ILogger<PowerBISiteConnection> _logger;
        private readonly IArtifactCompressionService? _compression;

        public PowerBISiteConnectionFactory(IServiceProvider provider)
        {
            _logger = provider.GetRequiredService<ILogger<PowerBISiteConnection>>();
            _compression = provider.GetService<IArtifactCompressionService>();
        }

        public Type ConnectionType => typeof(PowerBISiteConnection);

        public ISiteConnection CreateConnection(IDictionary<string, string> parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            var connection = new PowerBISiteConnection(_logger, _compression);
            connection.SetConnectionParameters(parameters);
            connection.SetCompressionService(_compression);
            
            return connection;
        }
    }
}
