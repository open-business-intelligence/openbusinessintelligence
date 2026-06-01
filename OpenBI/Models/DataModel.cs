using System.Collections.Generic;

namespace OpenBI
{
    public partial class DataModel
    {
        public ICollection<Table> Tables { get; set; } = new List<Table>();
        public ICollection<Relationship> Relationships { get; set; } = new List<Relationship>();
        public Expression? Expression { get; set; }
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }
}
