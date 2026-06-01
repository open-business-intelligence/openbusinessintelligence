using System.Collections.Generic;

namespace OpenBI
{
    public partial class Relationship
    {
        public string? Name { get; set; }
        public string IdColumnFrom { get; set; }
        public string IdColumnTo { get; set; }
        public RelationshipDirection Type { get; set; }
        public Expression? Expression { get; set; }
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }

    public enum RelationshipDirection
    {
        OneToMany,
        ManyToOne,
        ManyToMany,
        OneToOne
    }
}
