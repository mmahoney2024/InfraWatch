using System.ServiceProcess;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Imaging;

/// <summary>
/// Imaging-server health (SmartDeploy + legacy WDS/MDT). All read-only:
///  • Services — the imaging services are running (SmartDeploy SDApiService/SDClientService, WDSServer)
///  • Image stores — reachable, free disk space, and the OS images they hold (count / size / age)
///  • Shares / files — boot media and key files exist and are readable
///  • TFTP (optional) — download a PXE boot file and confirm bytes/latency
/// Imaging is back-office, so issues are Warning (not a production outage).
/// </summary>
public sealed class ImagingCollector : ICollector
{
    public const string Pillar = "Imaging";

    private readonly ImagingOptions _options;
    private readonly ILogger<ImagingCollector> _logger;

    public ImagingCollector(IOptions<ImagingOptions> options, ILogger<ImagingCollector> logger)
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
        var server = string.IsNullOrWhiteSpace(_options.Server) ? "imaging" : _options.Server;
        var haveServer = !string.IsNullOrWhiteSpace(_options.Server);

        if (haveServer)
            foreach (var svc in _options.Services)
                if (!string.IsNullOrWhiteSpace(svc))
                    health.Add(ServiceCheck(server, svc));

        foreach (var store in _options.ImageShares)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(store))
                ImageStoreCheck(server, store, health, inventory, ct);
        }

        foreach (var share in _options.Shares)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(share)) continue;
            try
            {
                _ = Directory.EnumerateFileSystemEntries(share).Take(1).ToList();
                health.Add(H(server, $"{Label(share)}: share", HealthStatus.Healthy, null, null, $"{share} accessible"));
            }
            catch (Exception ex)
            {
                health.Add(H(server, $"{Label(share)}: share", HealthStatus.Warning, null, null, $"inaccessible: {ex.Message}"));
            }
        }

        foreach (var file in _options.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(file))
                health.Add(FileCheck(server, file, inventory));
        }

        foreach (var file in _options.TftpFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(file) || !haveServer) continue;

            var r = TftpClient.Read(_options.Server, file, _options.TftpMaxBytes, _options.TftpTimeoutMs);
            health.Add(H(server, $"tftp {file}", r.Ok ? HealthStatus.Healthy : HealthStatus.Warning,
                r.Bytes, "bytes", r.Ok ? $"{r.Message} ({r.LatencyMs} ms)" : r.Message));
            if (r.Ok)
                inventory.Add(new InventoryRecord
                {
                    Pillar = Pillar, Kind = "boot-file", Key = file, Name = file,
                    Attributes = new Dictionary<string, string>
                    {
                        ["bytes"] = r.Bytes.ToString(), ["latencyMs"] = r.LatencyMs.ToString(),
                    },
                });
        }

        return new CollectionResult(health, inventory);
    }

    private HealthRecord ServiceCheck(string server, string svc)
    {
        try
        {
            using var sc = new ServiceController(svc, _options.Server);
            var status = sc.Status;
            return H(server, $"service {svc}",
                status == ServiceControllerStatus.Running ? HealthStatus.Healthy : HealthStatus.Warning,
                null, null, $"{svc}: {status}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not query service {Service} on {Server}", svc, _options.Server);
            return H(server, $"service {svc}", HealthStatus.Unknown, null, null, $"could not query service: {ex.Message}");
        }
    }

    private void ImageStoreCheck(string server, string store, List<HealthRecord> health, List<InventoryRecord> inventory, CancellationToken ct)
    {
        var label = Label(store);

        try
        {
            _ = Directory.EnumerateFileSystemEntries(store).Take(1).ToList();
            health.Add(H(server, $"{label}: share", HealthStatus.Healthy, null, null, $"{store} accessible"));
        }
        catch (Exception ex)
        {
            health.Add(H(server, $"{label}: share", HealthStatus.Warning, null, null, $"inaccessible: {ex.Message}"));
            return; // can't inventory what we can't reach
        }

        if (DiskFree.TryGet(store, out var freeGb, out var totalGb, out var freePct))
        {
            var low = _options.DiskWarnPct > 0 && freePct < _options.DiskWarnPct;
            health.Add(H(server, $"{label}: free space", low ? HealthStatus.Warning : HealthStatus.Healthy,
                Math.Round(freePct, 1), "%free",
                $"{Math.Round(freeGb)} GB free of {Math.Round(totalGb)} GB ({Math.Round(freePct, 1)}%)"));
        }

        var patterns = _options.ImagePatterns.Count > 0 ? _options.ImagePatterns : ["*.wim"];
        var files = new List<FileInfo>();
        foreach (var pat in patterns)
        {
            try { files.AddRange(new DirectoryInfo(store).EnumerateFiles(pat, SearchOption.AllDirectories)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Image enumeration failed in {Store} for {Pattern}", store, pat); }
        }
        files = files.GroupBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        var newest = DateTime.MinValue;
        foreach (var fi in files)
        {
            ct.ThrowIfCancellationRequested();
            var sizeGb = Math.Round(fi.Length / 1024.0 / 1024.0 / 1024.0, 2);
            var modified = fi.LastWriteTime;
            if (modified > newest) newest = modified;
            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar, Kind = "image", Key = fi.FullName, Name = fi.Name,
                Attributes = new Dictionary<string, string>
                {
                    ["sizeGB"] = sizeGb.ToString(),
                    ["modified"] = modified.ToString("yyyy-MM-dd"),
                    ["ageDays"] = ((int)(DateTime.Now - modified).TotalDays).ToString(),
                    ["store"] = label,
                },
            });
        }

        if (files.Count == 0)
        {
            health.Add(H(server, $"{label}: images", HealthStatus.Warning, 0, "count", "no images found"));
        }
        else
        {
            var newestAge = (int)(DateTime.Now - newest).TotalDays;
            var stale = _options.StaleWarnDays > 0 && newestAge > _options.StaleWarnDays;
            health.Add(H(server, $"{label}: images", stale ? HealthStatus.Warning : HealthStatus.Healthy, files.Count, "count",
                $"{files.Count} image(s), newest {newestAge}d old" + (stale ? $" (stale > {_options.StaleWarnDays}d)" : "")));
        }
    }

    private HealthRecord FileCheck(string server, string unc, List<InventoryRecord> inventory)
    {
        var name = Path.GetFileName(unc.TrimEnd('\\'));
        try
        {
            var fi = new FileInfo(unc);
            if (!fi.Exists)
                return H(server, $"file {name}", HealthStatus.Warning, null, null, $"missing: {unc}");

            using (var fs = fi.OpenRead())
            {
                Span<byte> one = stackalloc byte[1];
                _ = fs.Read(one); // confirm readable
            }

            var sizeMb = Math.Round(fi.Length / 1024.0 / 1024.0, 1);
            var modified = fi.LastWriteTime;
            var ageDays = (int)(DateTime.Now - modified).TotalDays;
            var stale = _options.StaleWarnDays > 0 && ageDays > _options.StaleWarnDays;

            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar, Kind = "boot-file", Key = unc, Name = name,
                Attributes = new Dictionary<string, string>
                {
                    ["sizeMB"] = sizeMb.ToString(),
                    ["modified"] = modified.ToString("yyyy-MM-dd"),
                    ["ageDays"] = ageDays.ToString(),
                },
            });

            return H(server, $"file {name}", stale ? HealthStatus.Warning : HealthStatus.Healthy, sizeMb, "MB",
                $"{sizeMb} MB, modified {modified:yyyy-MM-dd}" + (stale ? $" (stale > {_options.StaleWarnDays}d)" : ""));
        }
        catch (UnauthorizedAccessException)
        {
            return H(server, $"file {name}", HealthStatus.Warning, null, null, "access denied");
        }
        catch (Exception ex)
        {
            return H(server, $"file {name}", HealthStatus.Warning, null, null, ex.Message);
        }
    }

    /// <summary>Short label for a UNC path — its last segment (the share/folder name).</summary>
    private static string Label(string unc)
    {
        var trimmed = unc.TrimEnd('\\');
        var idx = trimmed.LastIndexOf('\\');
        return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
    }

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string? unit, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = unit, Summary = summary,
    };
}
