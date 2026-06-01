using OpenBI.Interfaces.Infrastructure;
using Microsoft.Extensions.Logging;
using OpenBI;
using OpenBI.Patching;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenBI.Converters.Interfaces
{
    public interface IOpenBIConverter
    {
        void SetLogger(ILogger logger);
        void SetCompressionService(IArtifactCompressionService compressionService);

        Task<Asset> FromArtifactToOpenBIAsync(byte[] artifact);
        Task<byte[]> FromOpenBIToArtifactAsync(Asset asset);

        /// <summary>
        /// Applies a list of changes to an existing artifact and returns the patched result.
        /// Changes are produced by <see cref="IOpenBIAssetComparer.Compare"/> (default implementation:
        /// <see cref="OpenBIAssetComparer"/>) and describe the delta between the original (downloaded)
        /// asset and the desired (session) asset.
        /// Errors are collected per-change; the returned artifact reflects all successfully
        /// applied changes even when some fail.
        /// </summary>
        Task<OpenBIPatchResult> FromOpenBIPatchArtifactAsync(IEnumerable<OpenBIChange> changes, byte[] artifact);
    }
}
