using OpenBI.Models;
using System.Collections.Generic;

namespace OpenBI
{
    public partial class Visual
    {
        public string Id { get; set; }
        public string Name { get; set; } = null!;
        public VisualCategories Category { get; set; }
        public string Type { get; set; } = null!;
        public string? OpenBIVisualType { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public decimal Z { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public string? Description { get; set; }
        public ICollection<VisualProjection> VisualProjections { get; set; } = new List<VisualProjection>();
        public ICollection<Filter> VisualLevelFilters { get; set; } = new List<Filter>();
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
        public ICollection<Visual>? Children { get; set; }
    }
}
