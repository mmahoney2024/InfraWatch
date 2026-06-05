using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Dhcp;

public sealed record DhcpTestOutcome(bool Success, string Message);

/// <summary>
/// Runs the active DHCP offer/lease probe on demand (e.g. from a dashboard button), against
/// a configured server. Separate from the scheduled collector so it can be triggered any time.
/// </summary>
public sealed class DhcpTester
{
    private readonly DhcpOptions _options;

    public DhcpTester(IOptions<DhcpOptions> options) => _options = options.Value;

    public DhcpTestOutcome Run(string server, bool fullLease)
    {
        // Only probe servers we're configured to monitor.
        if (!_options.Servers.Any(s => string.Equals(s, server, StringComparison.OrdinalIgnoreCase)))
            return new(false, $"'{server}' is not a configured DHCP server.");

        if (string.IsNullOrWhiteSpace(_options.RelayAddress) || !IPAddress.TryParse(_options.RelayAddress, out var relay))
            return new(false, "Set Dhcp:RelayAddress to a local IP on a subnet the server has a scope for, then retry.");

        IPAddress? serverIp;
        try
        {
            serverIp = Array.Find(Dns.GetHostAddresses(server), a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (Exception ex)
        {
            return new(false, $"Could not resolve {server}: {ex.Message}");
        }
        if (serverIp is null)
            return new(false, $"No IPv4 address for {server}.");

        try
        {
            var r = DhcpProbe.Run(serverIp, relay, _options.OfferTimeoutMs, fullLease);
            return r.OfferReceived
                ? new(true, $"OK — {server}: {r.Message} ({r.LatencyMs} ms)")
                : new(false, $"No response — {server}: {r.Message}. " +
                             "(For a failover pair this is normal on the partner that isn't responsible for the probe MAC.)");
        }
        catch (Exception ex)
        {
            return new(false, $"{server}: probe error — {ex.Message}");
        }
    }
}
