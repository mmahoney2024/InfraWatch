namespace InfraWatch.Core;

/// <summary>
/// "Is it OK right now?" — one measured health observation for a single check on a single
/// target. Collectors emit these; the store keeps every one (append-only) so history and
/// drift fall out for free.
/// </summary>
public sealed record HealthRecord
{
    /// <summary>Pillar that produced this, e.g. "HostNet", "Jira".</summary>
    public required string Pillar { get; init; }

    /// <summary>What was checked, e.g. "dc01.sscserv.com", "IMS".</summary>
    public required string Target { get; init; }

    /// <summary>The specific check, e.g. "ping", "tls-expiry", "timeclock".</summary>
    public required string Check { get; init; }

    public required HealthStatus Status { get; init; }

    /// <summary>One-line human summary for the tile/list.</summary>
    public string? Summary { get; init; }

    /// <summary>Measured numeric value, if any (latency, days-to-expiry, ticket count).</summary>
    public double? Value { get; init; }

    /// <summary>Unit for <see cref="Value"/>, e.g. "ms", "days", "tickets".</summary>
    public string? Unit { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Optional extra measured detail for drill-down.</summary>
    public IReadOnlyDictionary<string, string>? Details { get; init; }
}
