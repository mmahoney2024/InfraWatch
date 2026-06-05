namespace InfraWatch.Core;

/// <summary>
/// A delivery channel for alerts (email / Teams / ntfy / Discord). The engine decides
/// <em>what</em> and <em>when</em> (dedup/flap handled there); a channel only delivers.
/// </summary>
public interface IAlertChannel
{
    string Name { get; }

    Task SendAsync(Alert alert, CancellationToken cancellationToken = default);
}

/// <summary>A single alert to deliver.</summary>
public sealed record Alert
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required HealthStatus Severity { get; init; }
    public string? Pillar { get; init; }
    public string? Url { get; init; }
    public DateTimeOffset Raised { get; init; } = DateTimeOffset.UtcNow;
}
