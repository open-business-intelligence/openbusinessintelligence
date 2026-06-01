using System.Collections.Generic;
namespace OpenBI.Connectors.PowerBI.Schema
{

    public class Measure
    {
        public string name { get; set; }
        public object expression { get; set; }
        public string lineageTag { get; set; }
        public string dataType { get; set; }
        public List<Annotation> annotations { get; set; }
    }

}