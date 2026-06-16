using System.Management;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Print;

/// <summary>
/// Print-server health (WMI, integrated Windows auth): the Spooler service, and per published
/// printer its status, error state (jam / no paper / no toner / offline …), and queue depth
/// (a stuck/backed-up queue is the most common print problem). Inventories each printer
/// (share / port / driver) for documentation. Read-only.
/// </summary>
public sealed class PrintCollector : ICollector
{
    public const string Pillar = "Print";

    private readonly PrintOptions _options;
    private readonly ILogger<PrintCollector> _logger;

    public PrintCollector(IOptions<PrintOptions> options, ILogger<PrintCollector> logger)
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
        var host = _options.Server;
        if (string.IsNullOrWhiteSpace(host))
            return new CollectionResult(health, inventory);

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

            health.Add(SpoolerCheck(scope, host));

            // Count queued jobs per printer (Win32_PrintJob.Name is "PrinterName, JobId").
            var jobs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var jobSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name FROM Win32_PrintJob")))
            {
                foreach (ManagementBaseObject j in jobSearcher.Get())
                {
                    ct.ThrowIfCancellationRequested();
                    var n = j["Name"]?.ToString() ?? "";
                    var idx = n.IndexOf(',');
                    var printer = idx > 0 ? n[..idx].Trim() : n.Trim();
                    if (printer.Length > 0)
                        jobs[printer] = jobs.GetValueOrDefault(printer) + 1;
                }
            }

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(
                "SELECT Name, ShareName, Shared, PortName, DriverName, Default, WorkOffline, PrinterStatus, DetectedErrorState FROM Win32_Printer"));
            var total = 0;
            foreach (ManagementBaseObject p in searcher.Get())
            {
                ct.ThrowIfCancellationRequested();
                var name = p["Name"]?.ToString() ?? "";
                if (name.Length == 0) continue;

                var shared = ToBool(p["Shared"]);
                var share = p["ShareName"]?.ToString() ?? "";
                var port = p["PortName"]?.ToString() ?? "";
                var driver = p["DriverName"]?.ToString() ?? "";
                var offline = ToBool(p["WorkOffline"]);
                var pstatus = ToU32(p["PrinterStatus"]);
                var errState = ToU32(p["DetectedErrorState"]);
                var queued = jobs.GetValueOrDefault(name);
                total += queued;

                inventory.Add(new InventoryRecord
                {
                    Pillar = Pillar, Kind = "printer", Key = $@"{host}\{name}", Name = name,
                    Attributes = new Dictionary<string, string>
                    {
                        ["share"] = shared ? share : "(not shared)",
                        ["port"] = port,
                        ["driver"] = driver,
                    },
                });

                // A "hard" error: paper/toner/jam/door/offline/service/bin — not 0(Unknown)/1(Other)/2(No Error).
                var hardError = errState is 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11;
                HealthStatus status;
                string summary;
                if (offline || pstatus == 7)
                {
                    status = HealthStatus.Warning;
                    summary = $"offline · {queued} job(s) queued";
                }
                else if (hardError)
                {
                    status = HealthStatus.Warning;
                    summary = $"{ErrorText(errState)} · {queued} job(s) queued";
                }
                else if (_options.QueueWarnJobs > 0 && queued >= _options.QueueWarnJobs)
                {
                    status = HealthStatus.Warning;
                    summary = $"backlog: {queued} job(s) queued";
                }
                else
                {
                    status = HealthStatus.Healthy;
                    summary = $"{StatusText(pstatus)} · {queued} job(s) queued";
                }

                health.Add(H(name, "printer", status, queued, "jobs", summary));
            }

            health.Add(H(host, "queue total", HealthStatus.Healthy, total, "jobs", $"{total} job(s) queued across {jobs.Count} printer(s) with work"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Print collection failed on {Host}", host);
            health.Add(H(host, "spooler", HealthStatus.Critical, null, null, $"unreachable: {ex.Message}"));
        }

        return new CollectionResult(health, inventory);
    }

    private HealthRecord SpoolerCheck(ManagementScope scope, string host)
    {
        try
        {
            using var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT State FROM Win32_Service WHERE Name='Spooler'"));
            foreach (ManagementBaseObject svc in s.Get())
            {
                var state = svc["State"]?.ToString() ?? "Unknown";
                return H(host, "spooler", state == "Running" ? HealthStatus.Healthy : HealthStatus.Critical,
                    null, null, $"Spooler: {state}");
            }
            return H(host, "spooler", HealthStatus.Unknown, null, null, "Spooler service not found");
        }
        catch (Exception ex)
        {
            return H(host, "spooler", HealthStatus.Unknown, null, null, $"could not query spooler: {ex.Message}");
        }
    }

    private static bool ToBool(object? o)
    {
        try { return o is not null && Convert.ToBoolean(o); }
        catch { return false; }
    }

    private static uint ToU32(object? o)
    {
        try { return o is null ? 0 : Convert.ToUInt32(o); }
        catch { return 0; }
    }

    private static string StatusText(uint s) => s switch
    {
        1 => "other", 2 => "unknown", 3 => "idle", 4 => "printing",
        5 => "warming up", 6 => "stopped", 7 => "offline", _ => $"status {s}",
    };

    private static string ErrorText(uint e) => e switch
    {
        3 => "low paper", 4 => "no paper", 5 => "low toner", 6 => "no toner",
        7 => "door open", 8 => "jammed", 9 => "offline", 10 => "service requested",
        11 => "output bin full", _ => $"error {e}",
    };

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string? unit, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = unit, Summary = summary,
    };
}
