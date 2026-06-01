using System.Collections.Generic;

namespace OpenBI;

public class DataSourceConnection
{
    public string? ExternalId { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public Dictionary<string, string>? Parameters { get; set; }
}
