using System.Collections.Generic;

namespace OpenBI.Connectors.Interfaces.Models
{
    /// <summary>
    /// Generic, platform-agnostic parameters for querying assets.
    /// Platform-specific parameters can be added via AdditionalParameters dictionary.
    /// </summary>
    public class QueryAssetsParameters
    {
        /// <summary>
        /// Filter assets by name (contains or regex pattern)
        /// </summary>
        public string? NameFilter { get; set; }

        /// <summary>
        /// Filter by folder/workspace name (contains or regex pattern)
        /// </summary>
        public string? FolderNameFilter { get; set; }

        public string? AssetType { get; set; }

        /// <summary>
        /// Query specific asset by ID
        /// </summary>
        public string? AssetId { get; set; }

        /// <summary>
        /// Query assets in specific folder/workspace by ID
        /// </summary>
        public string? FolderId { get; set; }

        /// <summary>
        /// Platform-specific parameters (e.g., "AssetType" for PowerBI: "Report", "SemanticModel")
        /// </summary>
        public Dictionary<string, string>? AdditionalParameters { get; set; }
    }
}
