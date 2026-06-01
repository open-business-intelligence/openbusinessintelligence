using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenBI.Converters.PowerBI.Models.Schema
{
    public class Relationship
    {
        public string name { get; set; }
        public string fromTable { get; set; }
        public string fromColumn { get; set; }
        public string toTable { get; set; }
        public string toColumn { get; set; }
        public string? fromCardinality { get; set; }
        public string? toCardinality { get; set; }
    }
}
