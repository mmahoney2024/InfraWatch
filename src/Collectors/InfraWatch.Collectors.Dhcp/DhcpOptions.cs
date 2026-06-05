namespace InfraWatch.Collectors.Dhcp;

public sealed class DhcpOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>DHCP server hosts to query (via the DhcpServer PowerShell module).</summary>
    public List<string> Servers { get; set; } = [];

    /// <summary>An active scope at or above this percent in use is Warning.</summary>
    public double PercentInUseWarn { get; set; } = 90;

    /// <summary>An active scope at or above this percent in use is Critical.</summary>
    public double PercentInUseCrit { get; set; } = 98;

    public int TimeoutSeconds { get; set; } = 45;

    // --- Active offer test (opt-in; intrusive — sends a real DHCP DISCOVER) ---

    /// <summary>Send a DHCP DISCOVER to each server and expect an OFFER. Off by default.</summary>
    public bool OfferTest { get; set; }

    /// <summary>Also complete the lease (REQUEST → ACK) then RELEASE it. More intrusive.</summary>
    public bool LeaseTest { get; set; }

    /// <summary>
    /// Relay/giaddr address to put in the DISCOVER — must be a local IP on a subnet the
    /// server has a scope for (the server unicasts the OFFER back to it). Required for the
    /// offer test. Binding UDP/67 may require the service to run with sufficient privilege.
    /// </summary>
    public string? RelayAddress { get; set; }

    public int OfferTimeoutMs { get; set; } = 4000;
}
