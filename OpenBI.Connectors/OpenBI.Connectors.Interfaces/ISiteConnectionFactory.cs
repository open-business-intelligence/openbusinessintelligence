using System;
using System.Collections.Generic;

namespace OpenBI.Connectors.Interfaces
{
    /// <summary>
    /// Factory interface for creating ISiteConnection instances.
    /// Each BI platform library (PowerBI, QlikSense, etc.) implements this interface.
    /// </summary>
    public interface ISiteConnectionFactory
    {
        /// <summary>
        /// Gets the type of ISiteConnection that this factory creates.
        /// Used by the host application to resolve the appropriate logger type.
        /// </summary>
        Type ConnectionType { get; }

        /// <summary>
        /// Creates an ISiteConnection instance with the provided connection parameters.
        /// </summary>
        /// <param name="parameters">Connection parameters dictionary (TenantId, ClientId, ClientSecret, etc.)</param>
        /// <returns>Configured ISiteConnection instance</returns>
        ISiteConnection CreateConnection(IDictionary<string, string> parameters);
    }
}
