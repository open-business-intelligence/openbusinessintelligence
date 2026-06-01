namespace OpenBI.Converters.PowerBI.Models.Schema{ 

    public class Partition
    {
        public string name { get; set; }
        public string mode { get; set; }
        public string state { get; set; }
        public Source source { get; set; }
    }

}