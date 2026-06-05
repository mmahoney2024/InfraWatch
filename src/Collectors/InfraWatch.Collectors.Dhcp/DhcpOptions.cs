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
}
