using OpenBI.Interfaces.Infrastructure;
using OpenBI.Converters.Interfaces;
using OpenBI.Converters.PowerBI.Converters;
using Microsoft.Extensions.Logging;
using System;

namespace OpenBI.Converters.PowerBI
{
    public class PowerBIOpenBIConverterFactory : IOpenBIConverterFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IArtifactCompressionService _compressionService;

        public PowerBIOpenBIConverterFactory(ILoggerFactory loggerFactory, IArtifactCompressionService compressionService) 
        {
            _loggerFactory = loggerFactory;
            _compressionService = compressionService;
        }

        public IOpenBIConverter CreateOpenBIConverter(string assetType)
        {
            var parsed = TryParseAssetType(assetType);
            if (!parsed.HasValue)
                throw new ArgumentException($"Unknown PowerBI asset type: '{assetType}'.", nameof(assetType));

            return parsed.Value switch
            {
                PowerBIAssetType.Report => new PowerBIReportPbirOpenBIConverter(_loggerFactory.CreateLogger<PowerBIReportPbirOpenBIConverter>(), _compressionService),
                PowerBIAssetType.SemanticModel => new PowerBISemanticModelOpenBIConverter(_loggerFactory.CreateLogger<PowerBISemanticModelOpenBIConverter>(), _compressionService),
                _ => throw new NotSupportedException(
                    $"No PowerBI OpenBI converter registered for asset type '{assetType}'.")
            };
        }

        private static PowerBIAssetType? TryParseAssetType(string? assetType)
        {
            var value = assetType?.Trim();
            if (string.IsNullOrEmpty(value))
                return null;

            if (Enum.TryParse<PowerBIAssetType>(value, ignoreCase: true, out var result))
                return result;

            if (string.Equals(value, "DataModel", StringComparison.OrdinalIgnoreCase))
                return PowerBIAssetType.SemanticModel;

            return null;
        }
    }
}
