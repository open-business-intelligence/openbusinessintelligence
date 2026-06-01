using System.Collections.Generic;

namespace OpenBI;

/// <summary>
/// Entità OpenBI che rappresenta un'attività di reload/refresh dell'Asset (Reload Task in QlikSense, Data refresh in Power BI).
/// </summary>
public class RefreshTask
{
    public string Id { get; set; } = null!;

    /// <summary>Trigger che determinano quando la task viene eseguita (schedulati o composite).</summary>
    public List<RefreshTrigger>? Triggers { get; set; }

    /// <summary>Metadati aggiuntivi specifici della piattaforma (es. Qlik Sense, Power BI).</summary>
    public ICollection<AdditionalMetadata>? AdditionalMetadata { get; set; }
}
