using System.Diagnostics;
using System.Management;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Smb;

/// <summary>
/// SMB / file health + inventory. Per configured share: connect + authenticate + list
/// (latency), with an opt-in canary read/write. Per configured host: enumerate its shares
/// (Win32_Share) for documentation. Uses the service account's own Windows credentials.
/// Read-only by default (canary write is opt-in and scoped).
/// </summary>
public sealed class SmbCollector : ICollector
{
    public const string Pillar = "Smb";

    private readonly SmbOptions _options;
    private readonly ILogger<SmbCollector> _logger;

    public SmbCollector(IOptions<SmbOptions> options, ILogger<SmbCollector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => Pillar;
    public TimeSpan Interval => _options.Interval;

    public Task<CollectionResult> CollectAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Build(cancellationToken), cancellationToken);

    private CollectionResult Build(CancellationToken ct)
    {
        var health = new List<HealthRecord>();
        var inventory = new List<InventoryRecord>();

        foreach (var share in _options.Shares)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(share))
                AccessCheck(share, health, inventory);
        }

        foreach (var host in _options.EnumerateHosts)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(host))
                EnumerateShares(host, health, inventory);
        }

        return new CollectionResult(health, inventory);
    }

    private void AccessCheck(string unc, List<HealthRecord> health, List<InventoryRecord> inventory)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Probe connect + auth + list (take 1 to avoid enumerating everything).
            _ = Directory.EnumerateFileSystemEntries(unc).Take(1).ToList();
            sw.Stop();
            var ms = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            var status = ms >= _options.ListWarnMs ? HealthStatus.Warning : HealthStatus.Healthy;

            health.Add(H(unc, "access", status, ms, "ms", $"accessible ({ms} ms)"));
            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar, Kind = "share", Key = unc, Name = unc,
                Attributes = new Dictionary<string, string>
                {
                    ["path"] = unc, ["accessible"] = "true", ["latencyMs"] = ms.ToString(),
                },
            });
        }
        catch (UnauthorizedAccessException)
        {
            health.Add(H(unc, "access", HealthStatus.Critical, null, null, "access denied"));
        }
        catch (DirectoryNotFoundException)
        {
            health.Add(H(unc, "access", HealthStatus.Critical, null, null, "share/path not found"));
        }
        catch (IOException ex)
        {
            health.Add(H(unc, "access", HealthStatus.Critical, null, null, $"unreachable: {ex.Message}"));
        }
        catch (Exception ex)
        {
            health.Add(H(unc, "access", HealthStatus.Critical, null, null, ex.Message));
        }

        if (_options.CanaryWrite)
            health.Add(Canary(unc));
    }

    private HealthRecord Canary(string unc)
    {
        var path = Path.Combine(unc, _options.CanaryFileName);
        try
        {
            var token = $"infrawatch canary {DateTimeOffset.UtcNow:O}";
            File.WriteAllText(path, token);
            var read = File.ReadAllText(path);
            File.Delete(path);
            return read == token
                ? H(unc, "canary-write", HealthStatus.Healthy, null, null, "read/write OK")
                : H(unc, "canary-write", HealthStatus.Critical, null, null, "readback mismatch");
        }
        catch (Exception ex)
        {
            return H(unc, "canary-write", HealthStatus.Critical, null, null, $"failed: {ex.Message}");
        }
    }

    private void EnumerateShares(string host, List<HealthRecord> health, List<InventoryRecord> inventory)
    {
        try
        {
            var options = new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy,
                EnablePrivileges = true,
            };
            var scope = new ManagementScope($@"\\{host}\root\cimv2", options);
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Name, Path, Description, Type FROM Win32_Share"));
            var count = 0;
            foreach (ManagementBaseObject share in searcher.Get())
            {
                count++;
                var name = share["Name"]?.ToString() ?? "";
                inventory.Add(new InventoryRecord
                {
                    Pillar = Pillar, Kind = "share", Key = $@"\\{host}\{name}", Name = $@"\\{host}\{name}",
                    Attributes = new Dictionary<string, string>
                    {
                        ["host"] = host,
                        ["share"] = name,
                        ["path"] = share["Path"]?.ToString() ?? "",
                        ["type"] = ShareType(share["Type"]),
                        ["description"] = share["Description"]?.ToString() ?? "",
                    },
                });
            }
            health.Add(H(host, "shares", HealthStatus.Healthy, count, "count", $"{count} share(s) published"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Share enumeration failed on {Host}", host);
            health.Add(H(host, "shares", HealthStatus.Critical, null, null, $"enumeration failed: {ex.Message}"));
        }
    }

    private static string ShareType(object? type)
    {
        if (type is null) return "";
        var t = Convert.ToUInt32(type);
        return (t & 0x7FFFFFFF) switch
        {
            0 => t > 0x7FFFFFFF ? "Disk (special/admin)" : "Disk",
            1 => "Print queue",
            2 => "Device",
            3 => "IPC",
            _ => $"Type {t}",
        };
    }

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string? unit, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = unit, Summary = summary,
    };
}
