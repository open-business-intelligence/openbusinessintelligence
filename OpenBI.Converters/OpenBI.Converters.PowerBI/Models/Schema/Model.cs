using System.Collections.Generic; 
namespace OpenBI.Converters.PowerBI.Models.Schema{ 

    public class Model
    {
        public string culture { get; set; }
        public DataAccessOptions dataAccessOptions { get; set; }
        public string defaultPowerBIDataSourceVersion { get; set; }
        public string sourceQueryCulture { get; set; }
        public List<Table> tables { get; set; }
        public List<Relationship> relationships { get; set; }
        public List<Culture> cultures { get; set; }
        public List<Annotation> annotations { get; set; }
    }

}