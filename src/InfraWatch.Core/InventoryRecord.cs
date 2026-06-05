namespace InfraWatch.Core;

/// <summary>
/// "What exists?" — a measured fact about the environment that becomes living
/// documentation. Stored over time so the diff is the change/drift log.
/// </summary>
public sealed record InventoryRecord
{
    /// <summary>Pillar that produced this, e.g. "HostNet", "Jira".</summary>
    public required string Pillar { get; init; }

    /// <summary>Type of thing, e.g. "host", "cert", "jira-issue".</summary>
    public required string Kind { get; init; }

    /// <summary>Stable identity within (Pillar, Kind), e.g. "dc01" or "IMS-535".</summary>
    public required string Key { get; init; }

    /// <summary>Human display name.</summary>
    public required string Name { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The measured attributes that render as documentation / list rows.</summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}
