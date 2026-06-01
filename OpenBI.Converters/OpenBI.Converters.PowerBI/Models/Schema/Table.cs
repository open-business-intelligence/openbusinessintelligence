using System.Collections.Generic; 
namespace OpenBI.Converters.PowerBI.Models.Schema{ 

    public class Table
    {
        public string name { get; set; }
        public string lineageTag { get; set; }
        public List<Column> columns { get; set; }
        public List<Partition> partitions { get; set; }
        public List<Measure> measures { get; set; }
        public List<Annotation> annotations { get; set; }
        public bool? isHidden { get; set; }
        public bool? isPrivate { get; set; }        
        public List<Hierarchy> hierarchies { get; set; }
    }

}