using System.Collections.Generic;

namespace OpenBI
{
    public partial class VisualProjection
    {
        public string ProjectionName { get; set; } = null!;
        public string? OpenBIProjectionName { get; set; }
        public Expression? Expression { get; set; }
        public bool IsActive { get; set; }
        public string? Type { get; set; }
        public bool IsDimension { get; set; }
        public bool IsMeasure { get; set; }
        public string? IdColumnReference { get; set; }
        public int Order { get; set; }
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }
}
