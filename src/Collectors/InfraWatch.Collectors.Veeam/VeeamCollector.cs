using System.Text.Json;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Veeam;

/// <summary>
/// Veeam Backup &amp; Replication posture via the B&amp;R REST API: per-job last result + RPO
/// (no successful run within N hours), repository free space, and job/repo inventory.
/// Read-only. Degrades to "not configured" without credentials.
/// </summary>
public sealed class VeeamCollector : ICollector
{
    public const string Pillar = "Veeam";

    private readonly VeeamClient _client;
    private readonly VeeamOptions _options;
    private readonly ILogger<VeeamCollector> _logger;

    public VeeamCollector(VeeamClient client, IOptions<VeeamOptions> options, ILogger<VeeamCollector> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => Pillar;
    public TimeSpan Interval => _options.Interval;

    public async Task<CollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
            return CollectionResult.Empty; // dormant until BaseUrl + credentials are set

        var server = ServerLabel();
        var health = new List<HealthRecord>();
        var inventory = new List<InventoryRecord>();

        try
        {
            using var jobs = await _client.GetAsync("/api/v1/jobs/states", cancellationToken);
            var (jobOk, jobWarn, jobFail) = (0, 0, 0);
            foreach (var job in Data(jobs))
            {
                var rec = MapJob(server, job, inventory);
                health.Add(rec);
                if (rec.Status == HealthStatus.Healthy) jobOk++;
                else if (rec.Status == HealthStatus.Warning) jobWarn++;
                else if (rec.Status == HealthStatus.Critical) jobFail++;
            }
            health.Add(H(server, "jobs", jobFail > 0 ? HealthStatus.Critical : jobWarn > 0 ? HealthStatus.Warning : HealthStatus.Healthy,
                jobOk + jobWarn + jobFail, "jobs", $"{jobOk} ok · {jobWarn} warning · {jobFail} failed/RPO"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Veeam job query failed");
            health.Add(H(server, "connection", HealthStatus.Critical, null, null, $"connect/auth failed: {ex.Message}"));
            return new CollectionResult(health, inventory);
        }

        try
        {
            using var repos = await _client.GetAsync("/api/v1/backupInfrastructure/repositories/states", cancellationToken);
            foreach (var repo in Data(repos))
                health.Add(MapRepo(server, repo, inventory));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Veeam repository query failed");
            health.Add(H(server, "repositories", HealthStatus.Unknown, null, null, $"could not query: {ex.Message}"));
        }

        return new CollectionResult(health, inventory);
    }

    private HealthRecord MapJob(string server, JsonElement job, List<InventoryRecord> inventory)
    {
        var name = Str(job, "name");
        var type = Str(job, "type");
        var result = Str(job, "lastResult");   // Success | Warning | Failed | None
        var status = Str(job, "status");       // running / inactive / ...
        DateTimeOffset? lastRun = job.TryGetProperty("lastRun", out var lr) && lr.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(lr.GetString(), out var d) ? d : null;

        // A disabled job is intentionally off — don't alert on its old result.
        if (string.Equals(status, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            inventory.Add(JobInventory(name, type, result, status, lastRun));
            return H(server, $"job {name}", HealthStatus.Healthy, null, null, $"disabled (last result: {result})");
        }

        var baseStatus = result switch
        {
            "Success" => HealthStatus.Healthy,
            "Warning" => HealthStatus.Warning,
            "Failed" => HealthStatus.Critical,
            _ => HealthStatus.Unknown,
        };

        double? ageH = lastRun is { } when ? Math.Round((DateTimeOffset.UtcNow - when).TotalHours, 1) : null;
        var hs = baseStatus;
        var summary = lastRun is { } w
            ? $"{result}, last run {w.ToLocalTime():yyyy-MM-dd HH:mm}"
            : $"{result}, never run";

        if (ageH is { } age && age > _options.RpoHours)
        {
            hs = HealthStatus.Critical;
            summary = $"RPO breach: no run in {age:0}h (last result {result})";
        }

        inventory.Add(JobInventory(name, type, result, status, lastRun));
        return H(server, $"job {name}", hs, ageH, ageH is null ? null : "h", summary);
    }

    private static InventoryRecord JobInventory(string name, string type, string result, string status, DateTimeOffset? lastRun) => new()
    {
        Pillar = Pillar, Kind = "job", Key = name, Name = name,
        Attributes = new Dictionary<string, string>
        {
            ["type"] = type, ["lastResult"] = result, ["status"] = status,
            ["lastRun"] = lastRun?.ToLocalTime().ToString("u") ?? "",
        },
    };

    private HealthRecord MapRepo(string server, JsonElement repo, List<InventoryRecord> inventory)
    {
        var name = Str(repo, "name");
        var capacity = Dbl(repo, "capacityGB");
        var free = Dbl(repo, "freeGB");
        var used = Dbl(repo, "usedSpaceGB");
        var freePct = capacity > 0 ? Math.Round(free / capacity * 100, 1) : 0;

        var hs = capacity <= 0
            ? HealthStatus.Unknown
            : freePct < _options.RepoFreeWarnPct ? HealthStatus.Warning : HealthStatus.Healthy;

        inventory.Add(new InventoryRecord
        {
            Pillar = Pillar, Kind = "repository", Key = name, Name = name,
            Attributes = new Dictionary<string, string>
            {
                ["capacityGB"] = capacity.ToString("0.#"),
                ["freeGB"] = free.ToString("0.#"),
                ["usedGB"] = used.ToString("0.#"),
                ["freePct"] = freePct.ToString(),
            },
        });

        return H(server, $"repo {name}", hs, freePct, "%",
            $"{free:0} GB free of {capacity:0} GB ({freePct}%)");
    }

    private static IEnumerable<JsonElement> Data(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray()
            : [];

    private string ServerLabel()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl)) return "veeam";
        return Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var u) ? u.Host : _options.BaseUrl;
    }

    private static string Str(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static double Dbl(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.TryGetDouble(out var d) ? d : 0;

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string? unit, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = unit, Summary = summary,
    };
}
