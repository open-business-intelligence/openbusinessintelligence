using System.Collections.Generic;
using System.Text.Json;

namespace OpenBI;

public class Asset
{
    public string? IdSite { get; set; }
    public AssetInfo? Info { get; set; }
    public Layout? Layout { get; set; }
    public DataModel? DataModel { get; set; }
    public List<RefreshTask>? RefreshTasks { get; set; }
    public List<DataSourceConnection>? DataSourceConnections { get; set; }
    public IEnumerable<Asset> Dependencies { get; set; }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
