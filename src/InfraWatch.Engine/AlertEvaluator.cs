using InfraWatch.Core;
using Microsoft.Extensions.Logging;

namespace InfraWatch.Engine;

/// <summary>
/// Turns health observations into alerts on <em>state change</em>: fires when a check enters
/// Critical (and a recovery notice when it returns to Healthy), so a persistently-red check
/// doesn't alert every cycle. Seeded from the store at startup so a restart doesn't re-alert
/// pre-existing conditions.
/// </summary>
public sealed class AlertEvaluator
{
    private readonly IReadOnlyList<IAlertChannel> _channels;
    private readonly ILogger<AlertEvaluator> _logger;
    private readonly Dictionary<string, HealthStatus> _last = new();
    private readonly object _gate = new();

    public AlertEvaluator(IEnumerable<IAlertChannel> channels, ILogger<AlertEvaluator> logger)
    {
        _channels = channels.ToList();
        _logger = logger;
    }

    public bool HasChannels => _channels.Count > 0;

    /// <summary>Prime known state without alerting (used at startup).</summary>
    public void Seed(IEnumerable<HealthRecord> current)
    {
        lock (_gate)
        {
            foreach (var r in current)
                _last[Key(r)] = r.Status;
        }
    }

    public async Task EvaluateAsync(IEnumerable<HealthRecord> records, CancellationToken cancellationToken)
    {
        if (_channels.Count == 0)
            return;

        List<Alert> toSend = [];
        lock (_gate)
        {
            foreach (var r in records)
            {
                var key = Key(r);
                var prev = _last.GetValueOrDefault(key, HealthStatus.Unknown);
                _last[key] = r.Status;

                if (r.Status == HealthStatus.Critical && prev != HealthStatus.Critical)
                    toSend.Add(ToAlert(r, recovered: false));
                else if (prev == HealthStatus.Critical && r.Status == HealthStatus.Healthy)
                    toSend.Add(ToAlert(r, recovered: true));
            }
        }

        foreach (var alert in toSend)
        {
            _logger.LogInformation("Dispatching alert to {Channels} channel(s): {Title}",
                _channels.Count, alert.Title);
            foreach (var channel in _channels)
                await channel.SendAsync(alert, cancellationToken);
        }
    }

    private static string Key(HealthRecord r) => $"{r.Pillar}|{r.Target}|{r.Check}";

    private static Alert ToAlert(HealthRecord r, bool recovered)
    {
        var desc = r.Summary ?? $"{r.Pillar}/{r.Target} {r.Check}";
        return new Alert
        {
            Title = recovered ? $"Resolved – {r.Pillar}: {desc}" : $"{r.Pillar} alert: {desc}",
            Message = $"{r.Pillar} / {r.Target} / {r.Check}\nStatus: {r.Status}\n{r.Summary}".Trim(),
            Severity = recovered ? HealthStatus.Healthy : HealthStatus.Critical,
            Pillar = r.Pillar,
        };
    }
}
