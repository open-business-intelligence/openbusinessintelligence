using System;
using System.Collections.Generic;

namespace OpenBI
{
    public class AssetInfo
    {
        public string Id { get; set; }
        public string ExternalType { get; set; } = null!;
        public string IdFolder { get; set; }
        public string FolderName { get; set; }
        public AssetType Type { get; set; }
        public string Name { get; set; } = null!;        
        public string Description { get; set; } = null!;
        public DateTime? LatestUpdate { get; set; }
        public string? LatestUpdater { get; set; }
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }

    public enum AssetType
    {
        Report,
        DataModel
    }
}