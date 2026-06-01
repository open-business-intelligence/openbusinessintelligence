using System.Collections.Generic;

namespace OpenBI
{
    public class Filter
    {
        public string? Id { get; set; }
        public string? IdColumn { get; set; }
        public FilterFunctionType Function { get; set; }
        public string? FunctionName { get; set; }
        public string? LogicalOperator { get; set; }
        public Expression? Expression { get; set; }
        public List<string> Values { get; set; }
        public bool IsGroup { get; set; }
        public List<Filter>? Children { get; set; }
        public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
    }

    public enum FilterFunctionType
    {
        OnlySelectedValues,
        ExceptSelectedValues,
        Expression
    }
}
