using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Web;

/// <summary>
/// Web-server health: for each site, an HTTP availability check (status code + response time)
/// and a TLS certificate-expiry check (days remaining, with warn/critical thresholds). Pure
/// .NET, no special access. Inventories each site (status, server, cert subject/issuer/expiry).
/// </summary>
public sealed class WebCollector : ICollector
{
    public const string Pillar = "Web";

    private readonly WebOptions _options;
    private readonly ILogger<WebCollector> _logger;
    private readonly HttpClient _http;

    public WebCollector(IOptions<WebOptions> options, ILogger<WebCollector> logger)
    {
        _options = options.Value;
        _logger = logger;

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            // Cert expiry is validated separately; don't let an invalid/expiring cert block
            // the availability check.
            ServerCertificateCustomValidationCallback = static (_, _, _, _) => true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(_options.TimeoutMs) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("InfraWatch/1.0 (+infrastructure monitoring)");
    }

    public string Name => Pillar;
    public TimeSpan Interval => _options.Interval;

    public async Task<CollectionResult> CollectAsync(CancellationToken ct)
    {
        var health = new List<HealthRecord>();
        var inventory = new List<InventoryRecord>();

        foreach (var site in _options.Sites)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(site.Url))
                await CheckSiteAsync(site, health, inventory, ct);
        }

        return new CollectionResult(health, inventory);
    }

    private async Task CheckSiteAsync(WebSite site, List<HealthRecord> health, List<InventoryRecord> inventory, CancellationToken ct)
    {
        if (!Uri.TryCreate(site.Url, UriKind.Absolute, out var uri))
        {
            health.Add(H(site.Url, "http", HealthStatus.Unknown, null, null, "invalid URL"));
            return;
        }

        var target = site.Url;
        var label = string.IsNullOrWhiteSpace(site.Name) ? uri.Host : site.Name!;
        var attrs = new Dictionary<string, string> { ["url"] = site.Url };

        // --- HTTP availability ---
        try
        {
            var sw = Stopwatch.StartNew();
            using var resp = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            var ms = Math.Round(sw.Elapsed.TotalMilliseconds, 0);
            var code = (int)resp.StatusCode;
            var server = resp.Headers.Server?.ToString();

            var ok = site.ExpectStatus > 0 ? code == site.ExpectStatus : code is >= 200 and < 400;
            var status = !ok
                ? (code >= 500 ? HealthStatus.Critical : HealthStatus.Warning)
                : ms >= _options.SlowMs ? HealthStatus.Warning : HealthStatus.Healthy;

            health.Add(H(target, "http", status, ms, "ms", $"HTTP {code} in {ms} ms"));

            attrs["httpStatus"] = code.ToString();
            attrs["latencyMs"] = ms.ToString();
            if (!string.IsNullOrWhiteSpace(server)) attrs["server"] = server!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP check failed for {Url}", site.Url);
            health.Add(H(target, "http", HealthStatus.Critical, null, null, $"unreachable: {Brief(ex)}"));
        }

        // --- TLS certificate expiry (https only) ---
        if (uri.Scheme == Uri.UriSchemeHttps)
            await CheckTlsAsync(uri, target, attrs, health, ct);

        inventory.Add(new InventoryRecord
        {
            Pillar = Pillar, Kind = "website", Key = site.Url, Name = label, Attributes = attrs,
        });
    }

    private async Task CheckTlsAsync(Uri uri, string target, Dictionary<string, string> attrs, List<HealthRecord> health, CancellationToken ct)
    {
        var port = uri.Port > 0 ? uri.Port : 443;
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(uri.Host, port, ct);
            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: static (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(uri.Host);

            var remote = ssl.RemoteCertificate;
            if (remote is null)
            {
                health.Add(H(target, "tls-expiry", HealthStatus.Unknown, null, null, "no certificate presented"));
                return;
            }

            using var cert = remote as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(remote.Export(X509ContentType.Cert));
            var notAfter = cert.NotAfter.ToUniversalTime();
            var days = (int)Math.Floor((notAfter - DateTime.UtcNow).TotalDays);

            var status = days <= _options.CertCriticalDays
                ? HealthStatus.Critical
                : days <= _options.CertWarnDays
                    ? HealthStatus.Warning
                    : HealthStatus.Healthy;

            health.Add(H(target, "tls-expiry", status, days, "days",
                days < 0 ? $"expired {-days} day(s) ago" : $"expires in {days} day(s) ({notAfter:yyyy-MM-dd})"));

            attrs["certSubject"] = cert.Subject;
            attrs["certIssuer"] = cert.Issuer;
            attrs["certExpires"] = notAfter.ToString("yyyy-MM-dd");
            attrs["daysToExpiry"] = days.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TLS check failed for {Host}", uri.Host);
            health.Add(H(target, "tls-expiry", HealthStatus.Critical, null, null, $"handshake failed: {Brief(ex)}"));
        }
    }

    private static string Brief(Exception ex) => (ex.InnerException ?? ex).Message;

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string? unit, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = unit, Summary = summary,
    };
}
