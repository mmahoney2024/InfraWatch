namespace InfraWatch.Core;

/// <summary>
/// A derived downtime window for one check: from the sample that entered Critical to the
/// sample that returned to Healthy. Never stored — always computed from the append-only
/// health history, so it can't drift out of sync with what collectors actually observed.
/// </summary>
public sealed record IncidentRecord
{
    public required string Pillar { get; init; }
    public required string Target { get; init; }
    public required string Check { get; init; }

    /// <summary>Summary of the first Critical sample — what went wrong.</summary>
    public string? Error { get; init; }

    public required DateTimeOffset Start { get; init; }

    /// <summary>Null while the check is still down (or never recovered in the window).</summary>
    public DateTimeOffset? End { get; init; }

    public TimeSpan? Duration => End - Start;

    public bool IsOpen => End is null;
}
