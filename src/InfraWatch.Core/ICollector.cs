namespace InfraWatch.Core;

/// <summary>
/// A source of health + inventory for one pillar (or external integration). The engine
/// schedules each registered collector on its own <see cref="Interval"/> and persists what
/// it returns. Implementations should degrade gracefully (return Unknown, not throw) when
/// access is missing.
/// </summary>
public interface ICollector
{
    /// <summary>Pillar name, used as <see cref="HealthRecord.Pillar"/>, e.g. "HostNet".</summary>
    string Name { get; }

    /// <summary>How often the engine should run this collector.</summary>
    TimeSpan Interval { get; }

    Task<CollectionResult> CollectAsync(CancellationToken cancellationToken);
}
