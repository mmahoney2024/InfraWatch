namespace InfraWatch.Core;

/// <summary>
/// Roll-up health state for a check or tile. Maps to the dashboard's
/// grey / green / yellow / red.
/// </summary>
public enum HealthStatus
{
    /// <summary>No data yet, or the check could not run (e.g. access missing).</summary>
    Unknown = 0,

    /// <summary>Green — measured and within expectations.</summary>
    Healthy = 1,

    /// <summary>Yellow — degraded or approaching a threshold.</summary>
    Warning = 2,

    /// <summary>Red — failed or breached.</summary>
    Critical = 3,
}
