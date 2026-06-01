using System.Collections.Generic; 
namespace OpenBI.Converters.PowerBI.Models.Schema{ 

    public class Hierarchy
    {
        public string name { get; set; }
        public string lineageTag { get; set; }
        public string state { get; set; }
        public List<Level> levels { get; set; }
        public List<Annotation> annotations { get; set; }
    }

}