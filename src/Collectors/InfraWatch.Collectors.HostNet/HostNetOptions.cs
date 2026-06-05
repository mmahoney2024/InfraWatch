namespace InfraWatch.Collectors.HostNet;

public sealed class HostNetOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);

    public int PingTimeoutMs { get; set; } = 2000;

    /// <summary>Latency at or above this is Warning (yellow).</summary>
    public double PingWarnMs { get; set; } = 150;

    /// <summary>Hosts to ICMP-ping. Configured via "HostNet:PingTargets" (empty default —
    /// config binding appends to a non-empty list, so defaults live in appsettings.json).</summary>
    public List<string> PingTargets { get; set; } = [];

    /// <summary>"host:port" endpoints to read the TLS certificate from (via "HostNet:TlsTargets").</summary>
    public List<string> TlsTargets { get; set; } = [];

    /// <summary>Cert expiring within this many days is Warning (yellow).</summary>
    public int CertWarnDays { get; set; } = 30;

    /// <summary>Cert expiring within this many days (or expired) is Critical (red).</summary>
    public int CertCriticalDays { get; set; } = 7;
}
