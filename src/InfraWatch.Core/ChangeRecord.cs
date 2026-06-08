namespace InfraWatch.Core;

/// <summary>
/// A drift event — an inventory item appeared or disappeared. "Half of 'why did it break?'
/// is 'what changed?'" — this is the change history, computed by diffing inventory over time.
/// </summary>
public sealed record ChangeRecord
{
    public required string Pillar { get; init; }
    public required string Kind { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }

    /// <summary>"added" or "removed".</summary>
    public required string ChangeType { get; init; }

    public string? Detail { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
