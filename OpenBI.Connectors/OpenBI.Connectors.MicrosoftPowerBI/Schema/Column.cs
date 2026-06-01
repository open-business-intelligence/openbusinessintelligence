using System.Collections.Generic;
namespace OpenBI.Connectors.PowerBI.Schema
{

    public class Column
    {
        public int Id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string dataType { get; set; }
        public bool? isHidden { get; set; }
        public bool? isUnique { get; set; }
        public bool? isKey { get; set; }
        public bool? isNullable { get; set; }
        public AttributeHierarchy attributeHierarchy { get; set; }
        public string sourceColumn { get; set; }
        public string lineageTag { get; set; }
        public string summarizeBy { get; set; }
        public List<Annotation> annotations { get; set; }
        public bool? isNameInferred { get; set; }
        public bool? isDataTypeInferred { get; set; }
        public string formatString { get; set; }
        public string dataCategory { get; set; }
        public object expression { get; set; }
        public string sortByColumn { get; set; }
    }

}