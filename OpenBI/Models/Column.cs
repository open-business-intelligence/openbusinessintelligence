using System.Collections.Generic;

namespace OpenBI
{
    public partial class Column
    {
        public string Id { get; set; }
        public string? Type { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public ColumnDataType DataType { get; set; }
        public string? SummarizeBy { get; set; }
        public Expression? Expression { get; set; }
        public string? FormatString { get; set; }
        public string? DataCategory { get; set; }
        public bool IsKey { get; set; }
        public bool IsUnique { get; set; }
        public bool IsNullable { get; set; }
        public bool IsDimension { get; set; }
        public bool IsMeasure { get; set; }
        public ICollection<ColumnsReference> ColumnsReferences { get; set; } = new List<ColumnsReference>();
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }

        public enum ColumnDataType
        {
            String,
            Integer,
            Decimal,
            Date,
            Timestamp,
            Time,
            Boolean,
            Unknown
        }
    }
}
