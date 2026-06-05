using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.HostNet;

/// <summary>
/// General host/network health: ICMP latency and TLS certificate expiry. Pure .NET, no
/// special access required — the recommended first collector.
/// </summary>
public sealed class HostNetCollector : ICollector
{
    public const string Pillar = "HostNet";

    private readonly HostNetOptions _options;
    private readonly ILogger<HostNetCollector> _logger;

    public HostNetCollector(IOptions<HostNetOptions> options, ILogger<HostNetCollector> logger)
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

        foreach (var hostName in _options.PingTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            health.Add(await PingAsync(hostName, inventory));
        }

        foreach (var endpoint in _options.TlsTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            health.Add(await CheckTlsAsync(endpoint, inventory, cancellationToken));
        }

        return new CollectionResult(health, inventory);
    }

    private async Task<HealthRecord> PingAsync(string hostName, List<InventoryRecord> inventory)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostName, _options.PingTimeoutMs);

            if (reply.Status != IPStatus.Success)
            {
                return new HealthRecord
                {
                    Pillar = Pillar,
                    Target = hostName,
                    Check = "ping",
                    Status = HealthStatus.Critical,
                    Summary = $"Unreachable ({reply.Status})",
                };
            }

            var ms = reply.RoundtripTime;
            var status = ms >= _options.PingWarnMs ? HealthStatus.Warning : HealthStatus.Healthy;

            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar,
                Kind = "host",
                Key = hostName,
                Name = hostName,
                Attributes = new Dictionary<string, string>
                {
                    ["address"] = reply.Address?.ToString() ?? "",
                    ["lastLatencyMs"] = ms.ToString(),
                },
            });

            return new HealthRecord
            {
                Pillar = Pillar,
                Target = hostName,
                Check = "ping",
                Status = status,
                Summary = $"{ms} ms",
                Value = ms,
                Unit = "ms",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ping to {Host} failed", hostName);
            return new HealthRecord
            {
                Pillar = Pillar,
                Target = hostName,
                Check = "ping",
                Status = HealthStatus.Unknown,
                Summary = ex.Message,
            };
        }
    }

    private async Task<HealthRecord> CheckTlsAsync(
        string endpoint, List<InventoryRecord> inventory, CancellationToken ct)
    {
        var (host, port) = ParseEndpoint(endpoint);
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, ct);
            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: static (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(host);

            var remote = ssl.RemoteCertificate;
            if (remote is null)
            {
                return Unknown(endpoint, "No certificate presented");
            }

            using var cert = remote as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(remote.Export(X509ContentType.Cert));
            var notAfter = cert.NotAfter.ToUniversalTime();
            var days = (int)Math.Floor((notAfter - DateTime.UtcNow).TotalDays);

            var status = days <= _options.CertCriticalDays
                ? HealthStatus.Critical
                : days <= _options.CertWarnDays
                    ? HealthStatus.Warning
                    : HealthStatus.Healthy;

            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar,
                Kind = "cert",
                Key = endpoint,
                Name = host,
                Attributes = new Dictionary<string, string>
                {
                    ["subject"] = cert.Subject,
                    ["issuer"] = cert.Issuer,
                    ["notAfter"] = notAfter.ToString("u"),
                    ["daysToExpiry"] = days.ToString(),
                    ["port"] = port.ToString(),
                },
            });

            return new HealthRecord
            {
                Pillar = Pillar,
                Target = endpoint,
                Check = "tls-expiry",
                Status = status,
                Summary = days < 0 ? $"Expired {-days} day(s) ago" : $"Expires in {days} day(s)",
                Value = days,
                Unit = "days",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TLS check to {Endpoint} failed", endpoint);
            return new HealthRecord
            {
                Pillar = Pillar,
                Target = endpoint,
                Check = "tls-expiry",
                Status = HealthStatus.Critical,
                Summary = $"Connect/handshake failed: {ex.Message}",
            };
        }
    }

    private HealthRecord Unknown(string target, string summary) => new()
    {
        Pillar = Pillar,
        Target = target,
        Check = "tls-expiry",
        Status = HealthStatus.Unknown,
        Summary = summary,
    };

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        var idx = endpoint.LastIndexOf(':');
        if (idx > 0 && int.TryParse(endpoint[(idx + 1)..], out var port))
            return (endpoint[..idx], port);
        return (endpoint, 443);
    }
}
