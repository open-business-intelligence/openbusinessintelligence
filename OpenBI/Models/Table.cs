using System.Collections.Generic;

namespace OpenBI
{
    public partial class Table
    {
        public const string TableTypeTable = "Table";
        public const string TableTypeObject = "Object";

        public string Id { get; set; }
        public string? Type { get; set; }
        public string Name { get; set; } = null!;
        public Expression? Expression { get; set; }
        public ICollection<Column> Columns { get; set; } = new List<Column>();
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }
}
