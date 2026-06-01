using System.Collections.Generic;

namespace OpenBI
{
    public partial class Expression
    {
        public string Id { get; set; }
        public string? Type { get; set; }
        public string? Language { get; set; }
        public string Code { get; set; }
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }
}
