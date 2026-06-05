using System.ServiceProcess;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Imaging;

/// <summary>
/// WDS / MDT imaging health. Three layers, all read-only (the TFTP download is a passive
/// read like a PXE client):
///  • TFTP — download the PXE boot file(s) and confirm bytes/size/latency
///  • Deployment share — key files (boot WIM, Bootstrap.ini …) exist, are readable, fresh
///  • WDS service — the service is running
/// Uses the service account's own Windows credentials.
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

        if (_options.CheckService && haveServer)
            health.Add(ServiceCheck(server));

        foreach (var file in _options.TftpFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(file) || !haveServer)
                continue;

            var r = TftpClient.Read(_options.Server, file, _options.TftpMaxBytes, _options.TftpTimeoutMs);
            health.Add(H(server, $"tftp {file}", r.Ok ? HealthStatus.Healthy : HealthStatus.Critical,
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

        if (!string.IsNullOrWhiteSpace(_options.DeploymentShare))
        {
            try
            {
                _ = Directory.EnumerateFileSystemEntries(_options.DeploymentShare).Take(1).ToList();
                health.Add(H(server, "share", HealthStatus.Healthy, null, null, $"{_options.DeploymentShare} accessible"));
            }
            catch (Exception ex)
            {
                health.Add(H(server, "share", HealthStatus.Critical, null, null, $"share inaccessible: {ex.Message}"));
            }

            foreach (var rel in _options.ShareFiles)
            {
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(rel))
                    health.Add(FileCheck(server, rel, inventory));
            }
        }

        return new CollectionResult(health, inventory);
    }

    private HealthRecord ServiceCheck(string server)
    {
        try
        {
            using var sc = new ServiceController(_options.ServiceName, _options.Server);
            var status = sc.Status;
            return H(server, "wds-service",
                status == ServiceControllerStatus.Running ? HealthStatus.Healthy : HealthStatus.Critical,
                null, null, $"{_options.ServiceName}: {status}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not query service {Service} on {Server}", _options.ServiceName, _options.Server);
            return H(server, "wds-service", HealthStatus.Unknown, null, null, $"could not query service: {ex.Message}");
        }
    }

    private HealthRecord FileCheck(string server, string rel, List<InventoryRecord> inventory)
    {
        var full = Path.Combine(_options.DeploymentShare, rel);
        try
        {
            var fi = new FileInfo(full);
            if (!fi.Exists)
                return H(server, $"file {rel}", HealthStatus.Critical, null, null, "missing");

            using (var fs = fi.OpenRead())
            {
                Span<byte> one = stackalloc byte[1];
                _ = fs.Read(one); // confirm readable
            }

            var sizeMb = Math.Round(fi.Length / 1024.0 / 1024.0, 1);
            var modified = fi.LastWriteTime;
            var ageDays = (DateTime.Now - modified).TotalDays;
            var stale = _options.StaleDays > 0 && ageDays > _options.StaleDays;

            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar, Kind = "file", Key = rel, Name = rel,
                Attributes = new Dictionary<string, string>
                {
                    ["sizeMB"] = sizeMb.ToString(),
                    ["modified"] = modified.ToString("u"),
                    ["ageDays"] = ((int)ageDays).ToString(),
                },
            });

            return H(server, $"file {rel}", stale ? HealthStatus.Warning : HealthStatus.Healthy, sizeMb, "MB",
                $"{sizeMb} MB, modified {modified:yyyy-MM-dd}" + (stale ? $" (stale > {_options.StaleDays}d)" : ""));
        }
        catch (UnauthorizedAccessException)
        {
            return H(server, $"file {rel}", HealthStatus.Critical, null, null, "access denied");
        }
        catch (Exception ex)
        {
            return H(server, $"file {rel}", HealthStatus.Critical, null, null, ex.Message);
        }
    }

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string? unit, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = unit, Summary = summary,
    };
}
