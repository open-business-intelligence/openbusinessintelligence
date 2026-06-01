using System.Collections.Generic;

namespace OpenBI
{
    public partial class Page
    {
        public string Id { get; set; }        
        public string Name { get; set; } = null!;
        public int Order { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public bool IsEnabled { get; set; }
        public string? PageImagePath { get; set; }
        public string? Description { get; set; }
        public string? EmbedPageUrlParameter { get; set; }
        public string? EmbedPageUrl { get; set; }

        public ICollection<Visual> Visuals { get; set; } = new List<Visual>();
        public ICollection<Filter> PageLevelFilters { get; set; } = new List<Filter>();
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }
}