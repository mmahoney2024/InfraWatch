using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using DnsClient;
using DnsClient.Protocol;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Dns;

/// <summary>
/// DNS health: resolves configured records, verifies expected answers, measures query
/// latency, and detects NXDOMAIN / SERVFAIL. Queries the system resolver by default, or a
/// specific server per check. Pure network — no credentials.
/// </summary>
public sealed class DnsCollector : ICollector
{
    public const string Pillar = "Dns";

    private readonly DnsOptions _options;
    private readonly ILogger<DnsCollector> _logger;
    private readonly ConcurrentDictionary<string, LookupClient> _clients = new();

    public DnsCollector(IOptions<DnsOptions> options, ILogger<DnsCollector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => Pillar;
    public TimeSpan Interval => _options.Interval;

    public async Task<CollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        var health = new List<HealthRecord>();
        var inventory = new List<InventoryRecord>();

        foreach (var check in _options.Checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(check.Name))
                continue;
            health.Add(await ResolveAsync(check, inventory, cancellationToken));
        }

        return new CollectionResult(health, inventory);
    }

    private async Task<HealthRecord> ResolveAsync(
        DnsCheck check, List<InventoryRecord> inventory, CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(check.Server) ? check.Name : $"{check.Name}@{check.Server}";
        var checkName = check.Type.ToLowerInvariant();

        if (!Enum.TryParse<QueryType>(check.Type, ignoreCase: true, out var queryType))
            return Fail(target, checkName, $"Unknown record type '{check.Type}'");

        try
        {
            var client = ClientFor(check.Server);
            var sw = Stopwatch.StartNew();
            var result = await client.QueryAsync(check.Name, queryType, cancellationToken: ct);
            sw.Stop();
            var ms = Math.Round(sw.Elapsed.TotalMilliseconds, 1);

            if (result.HasError)
            {
                var why = result.Header.ResponseCode switch
                {
                    DnsHeaderResponseCode.NotExistentDomain => "NXDOMAIN (no such name)",
                    DnsHeaderResponseCode.ServerFailure => "SERVFAIL",
                    _ => $"{result.Header.ResponseCode}: {result.ErrorMessage}",
                };
                return Fail(target, checkName, why);
            }

            var answers = result.Answers.Select(Format).Where(s => !string.IsNullOrEmpty(s)).ToList();

            if (answers.Count == 0)
                return new HealthRecord
                {
                    Pillar = Pillar, Target = target, Check = checkName,
                    Status = HealthStatus.Warning, Value = ms, Unit = "ms",
                    Summary = $"No {check.Type} records ({ms} ms)",
                };

            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar, Kind = "dns-record", Key = $"{target}/{checkName}", Name = check.Name,
                Attributes = new Dictionary<string, string>
                {
                    ["type"] = check.Type.ToUpperInvariant(),
                    ["server"] = string.IsNullOrWhiteSpace(check.Server) ? "system" : check.Server,
                    ["answers"] = string.Join(", ", answers),
                    ["latencyMs"] = ms.ToString(),
                },
            });

            if (!string.IsNullOrWhiteSpace(check.Expect)
                && !answers.Any(a => a.Contains(check.Expect, StringComparison.OrdinalIgnoreCase)))
            {
                return new HealthRecord
                {
                    Pillar = Pillar, Target = target, Check = checkName,
                    Status = HealthStatus.Critical, Value = ms, Unit = "ms",
                    Summary = $"Unexpected answer: got {string.Join(", ", answers)} (wanted '{check.Expect}')",
                };
            }

            var status = ms >= _options.WarnMs ? HealthStatus.Warning : HealthStatus.Healthy;
            return new HealthRecord
            {
                Pillar = Pillar, Target = target, Check = checkName, Status = status,
                Value = ms, Unit = "ms",
                Summary = $"{Truncate(string.Join(", ", answers), 60)} ({ms} ms)",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS query {Name}/{Type} failed", check.Name, check.Type);
            return Fail(target, checkName, ex.Message);
        }
    }

    private LookupClient ClientFor(string? server) =>
        _clients.GetOrAdd(string.IsNullOrWhiteSpace(server) ? "system" : server, key =>
        {
            var options = key == "system"
                ? new LookupClientOptions()
                : new LookupClientOptions(IPAddress.Parse(key));
            options.Timeout = TimeSpan.FromMilliseconds(_options.TimeoutMs);
            options.UseCache = false; // measure real query latency each time
            return new LookupClient(options);
        });

    private static HealthRecord Fail(string target, string check, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check,
        Status = HealthStatus.Critical, Summary = summary,
    };

    private static string Format(DnsResourceRecord r) => r switch
    {
        ARecord a => a.Address.ToString(),
        AaaaRecord aaaa => aaaa.Address.ToString(),
        CNameRecord c => c.CanonicalName.Value,
        MxRecord mx => $"{mx.Preference} {mx.Exchange.Value}",
        TxtRecord txt => string.Join(" ", txt.Text),
        NsRecord ns => ns.NSDName.Value,
        PtrRecord ptr => ptr.PtrDomainName.Value,
        _ => r.ToString() ?? "",
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
