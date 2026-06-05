using System.Management;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.HyperV;

/// <summary>
/// Hyper-V health + inventory over WMI/CIM: per host — reachability, CPU %, free RAM %, VM
/// states, and checkpoint sprawl; inventories the host and its VMs. Uses the service
/// account's own Windows credentials (no creds in config). Read-only.
/// </summary>
public sealed class HyperVCollector : ICollector
{
    public const string Pillar = "HyperV";

    private readonly HyperVOptions _options;
    private readonly ILogger<HyperVCollector> _logger;

    public HyperVCollector(IOptions<HyperVOptions> options, ILogger<HyperVCollector> logger)
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
        var hosts = _options.Hosts.Count > 0 ? _options.Hosts : ["."];

        foreach (var host in hosts)
        {
            ct.ThrowIfCancellationRequested();
            CollectHost(host, health, inventory);
        }

        return new CollectionResult(health, inventory);
    }

    private void CollectHost(string host, List<HealthRecord> health, List<InventoryRecord> inventory)
    {
        var label = IsLocal(host) ? Environment.MachineName : host;

        ManagementScope virt;
        try
        {
            virt = ScopeFor(host, @"root\virtualization\v2");
            virt.Connect();
        }
        catch (Exception ex)
        {
            health.Add(H(label, "host", HealthStatus.Critical, null, null, $"unreachable: {ex.Message}"));
            return;
        }
        health.Add(H(label, "host", HealthStatus.Healthy, null, null, "reachable"));

        int running = 0, off = 0, other = 0;
        try
        {
            using var vms = new ManagementObjectSearcher(virt, new ObjectQuery(
                "SELECT ElementName, EnabledState, HealthState FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'"));
            foreach (ManagementBaseObject vm in vms.Get())
            {
                var name = vm["ElementName"]?.ToString() ?? "(unknown)";
                var state = ToInt(vm["EnabledState"]);
                var hs = ToInt(vm["HealthState"]);
                if (state == 2) running++;
                else if (state == 3) off++;
                else other++;

                inventory.Add(new InventoryRecord
                {
                    Pillar = Pillar, Kind = "vm", Key = $"{label}/{name}", Name = name,
                    Attributes = new Dictionary<string, string>
                    {
                        ["host"] = label,
                        ["state"] = VmState(state),
                        ["health"] = HealthText(hs),
                    },
                });
            }
            health.Add(H(label, "vm-states", HealthStatus.Healthy, running + off + other, "count",
                $"{running} running, {off} off, {other} other"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VM query failed on {Host}", label);
        }

        try
        {
            using var snaps = new ManagementObjectSearcher(virt, new ObjectQuery(
                "SELECT InstanceID FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemType = 'Microsoft:Hyper-V:Snapshot:Realized'"));
            var checkpoints = snaps.Get().Count;
            var st = checkpoints > _options.CheckpointWarn ? HealthStatus.Warning : HealthStatus.Healthy;
            health.Add(H(label, "checkpoints", st, checkpoints, "count", $"{checkpoints} checkpoint(s)"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Checkpoint query failed on {Host}", label);
        }

        CollectHostMetrics(host, label, running + off + other, running, inventory, health);
    }

    private void CollectHostMetrics(
        string host, string label, int vmCount, int running,
        List<InventoryRecord> inventory, List<HealthRecord> health)
    {
        try
        {
            var cim = ScopeFor(host, @"root\cimv2");
            cim.Connect();

            using var cpu = new ManagementObjectSearcher(cim, new ObjectQuery("SELECT LoadPercentage FROM Win32_Processor"));
            var loads = cpu.Get().Cast<ManagementBaseObject>()
                .Select(p => p["LoadPercentage"]).Where(v => v is not null).Select(ToDouble).ToList();
            if (loads.Count > 0)
            {
                var avg = Math.Round(loads.Average(), 1);
                health.Add(H(label, "cpu", avg >= _options.CpuWarnPct ? HealthStatus.Warning : HealthStatus.Healthy,
                    avg, "%", $"{avg}% CPU"));
            }

            using var os = new ManagementObjectSearcher(cim, new ObjectQuery(
                "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem"));
            foreach (ManagementBaseObject o in os.Get())
            {
                var freeKb = ToDouble(o["FreePhysicalMemory"]);
                var totalKb = ToDouble(o["TotalVisibleMemorySize"]);
                if (totalKb > 0)
                {
                    var freePct = Math.Round(freeKb / totalKb * 100, 1);
                    var st = freePct < _options.MemFreeWarnPct ? HealthStatus.Warning : HealthStatus.Healthy;
                    health.Add(H(label, "memory", st, freePct, "%",
                        $"{freePct}% free ({Gb(freeKb)} of {Gb(totalKb)} GB)"));
                }
                break;
            }

            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar, Kind = "host", Key = label, Name = label,
                Attributes = new Dictionary<string, string>
                {
                    ["vmCount"] = vmCount.ToString(),
                    ["running"] = running.ToString(),
                },
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Host metrics failed on {Host}", label);
        }
    }

    private static bool IsLocal(string host) =>
        host is "." or "localhost"
        || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private static ManagementScope ScopeFor(string host, string ns)
    {
        if (IsLocal(host))
            return new ManagementScope(ns);

        var options = new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            EnablePrivileges = true,
        };
        return new ManagementScope($@"\\{host}\{ns}", options);
    }

    private static int ToInt(object? v) => v is null ? 0 : Convert.ToInt32(v);
    private static double ToDouble(object? v) => v is null ? 0 : Convert.ToDouble(v);
    private static double Gb(double kb) => Math.Round(kb / 1024 / 1024, 1);

    private static string VmState(int s) => s switch
    {
        2 => "Running",
        3 => "Off",
        32768 => "Paused",
        32769 => "Saved",
        32770 => "Starting",
        32773 => "Saving",
        32774 => "Stopping",
        32776 => "Pausing",
        32777 => "Resuming",
        _ => $"State {s}",
    };

    private static string HealthText(int h) => h switch
    {
        0 => "",
        5 => "OK",
        10 => "Degraded",
        15 => "Minor failure",
        20 => "Major failure",
        25 => "Critical failure",
        30 => "Non-recoverable",
        _ => $"Health {h}",
    };

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string? unit, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = unit, Summary = summary,
    };
}
