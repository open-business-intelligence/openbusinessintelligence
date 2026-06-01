using System;

namespace OpenBI.Connectors.Interfaces.Models
{
    /// <summary>
    /// Represents an asset from a BI site that can be queried.
    /// </summary>
    public class SiteAsset
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Path { get; set; }
        public DateTime? CreationDate { get; set; }
        public DateTime? LastUpdate { get; set; }
        public string? Creator { get; set; }
        public string? LastUpdater { get; set; }
        public string? WebUrl { get; set; }
        public string? EmbedUrl { get; set; }
        public bool IsReadOnly { get; set; }
    }
}
