using System.Collections.Generic;

namespace OpenBI
{
    public partial class Layout
    {
        public ICollection<Page> Pages { get; set; } = new List<Page>();
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }
}
