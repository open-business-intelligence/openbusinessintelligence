using System.Collections.Generic;

namespace OpenBI;

/// <summary>
/// Tipo di trigger per l'esecuzione di una <see cref="RefreshTask"/>.
/// </summary>
public enum RefreshTriggerType
{
    /// <summary>Esecuzione in base a una schedulazione (es. oraria).</summary>
    Scheduled,

    /// <summary>Esecuzione a fronte di un evento (es. completamento di altre task di reload).</summary>
    Composite
}

/// <summary>
/// Parametri di schedulazione per un trigger di tipo <see cref="RefreshTriggerType.Scheduled"/>.
/// Estendibile con Recurrence, Time, TimeZoneId in base alle API delle piattaforme (Qlik, Power BI).
/// </summary>
public class ScheduleParameters
{
    /// <summary>Espressione cron per la schedulazione (opzionale).</summary>
    public string? CronExpression { get; set; }
}

/// <summary>
/// Parametri per un trigger di tipo <see cref="RefreshTriggerType.Composite"/> (esecuzione a fronte del completamento di altre task).
/// </summary>
public class CompositeParameters
{
    /// <summary>Id delle RefreshTask che al completamento lanciano questa task.</summary>
    public List<string>? DependentRefreshTaskIds { get; set; }
}

/// <summary>
/// Trigger che determina quando una <see cref="RefreshTask"/> viene eseguita (schedulato o a seguito di altre task).
/// </summary>
public class RefreshTrigger
{
    public string Id { get; set; } = null!;
    public RefreshTriggerType Type { get; set; }

    /// <summary>Valorizzato quando <see cref="Type"/> è <see cref="RefreshTriggerType.Scheduled"/>.</summary>
    public ScheduleParameters? ScheduleParameters { get; set; }

    /// <summary>Valorizzato quando <see cref="Type"/> è <see cref="RefreshTriggerType.Composite"/>.</summary>
    public CompositeParameters? CompositeParameters { get; set; }
}
