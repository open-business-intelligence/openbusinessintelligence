using System;
using System.Collections.Generic;
using System.Text;

namespace OpenBI
{
    public class SourcesExplorationResult
    {
        public List<SourcesExplorationWorkspace> Workspaces { get; set; }
    }

    public class SourcesExplorationWorkspace
    {
        public string Name { get; set; }
        public string ExternalID { get; set; }
        public List<SourcesExplorationArtifact> Artifacts { get; set; }
    }

    public class SourcesExplorationArtifact
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        //public ICollection<SourcesExplorationAsset> Assets { get; set; }
       
    }

    public class SourcesExplorationAsset
    {
        public AssetType? Type { get; set; }
        public string ExternalType { get; set; }
        public string ExternalID { get; set; }
    }
}
