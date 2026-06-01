using OpenBI.Interfaces.Infrastructure;
using OpenBI.Connectors.Interfaces.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenBI.Connectors.Interfaces
{
    /// <summary>
    /// Interface for connecting to BI platforms (PowerBI, QlikSense, etc.)
    /// and retrieving metadata and artifacts from those instances.
    /// </summary>
    public interface ISiteConnection
    {
        /// <summary>
        /// Sets the connection parameters required to connect to a BI site.
        /// </summary>
        /// <param name="parameters">Dynamic parameters dictionary containing connection details</param>
        void SetConnectionParameters(IDictionary<string, string> parameters);

        void SetCompressionService(IArtifactCompressionService compressionService);

        /// <summary>
        /// Queries assets from the site with optional filtering parameters.
        /// </summary>
        /// <param name="parameters">Optional query parameters for filtering assets (name, folder, asset type, etc.)</param>
        /// <returns>Collection of site assets matching the query parameters</returns>
        Task<ICollection<SiteAsset>> QuerySiteAssetsAsync(QueryAssetsParameters? parameters = null);

        /// <summary>
        /// Downloads an asset artifact by its ID and type.
        /// </summary>
        /// <param name="assetId">The ID of the asset to download</param>
        /// <param name="assetType">The type of the asset (e.g., "Report", "SemanticModel")</param>
        /// <returns>Stream containing the artifact data</returns>
        Task<byte[]> DownloadAssetArtifactAsync(string assetId, string assetType);

        /// <summary>
        /// Uploads an asset artifact to the site.
        /// </summary>
        /// <param name="idFolder">Workspace ID (used when creating; for update, workspace is resolved from idAsset).</param>
        /// <param name="idAsset">Asset ID; null or empty means create in the given workspace; otherwise update existing asset.</param>
        /// <param name="artifact">GZip-compressed zip archive containing the asset definition files.</param>
        Task<string> UploadAssetArtifactAsync(string idFolder, string? idAsset, byte[] artifact);

        /// <summary>
        /// Lists top-level folders in the site (e.g. workspaces, streams).
        /// </summary>
        Task<IReadOnlyList<SiteFolderInfo>> GetSiteFoldersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a folder in the site (e.g. workspace).
        /// </summary>
        Task<SiteFolderInfo> CreateSiteFolderAsync(CreateSiteFolderRequest request, CancellationToken cancellationToken = default);
    }
}
