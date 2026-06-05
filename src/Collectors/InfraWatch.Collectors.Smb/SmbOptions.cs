namespace InfraWatch.Collectors.Smb;

public sealed class SmbOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>UNC paths to access-check, e.g. \\fs-aio\Data. Connect + auth + list.</summary>
    public List<string> Shares { get; set; } = [];

    /// <summary>Hosts whose shares to enumerate for inventory (via WMI Win32_Share).</summary>
    public List<string> EnumerateHosts { get; set; } = [];

    /// <summary>Listing at or above this many ms is Warning.</summary>
    public double ListWarnMs { get; set; } = 2000;

    /// <summary>Opt-in: write+read+delete a small canary file to verify write access. Off by default.</summary>
    public bool CanaryWrite { get; set; }

    public string CanaryFileName { get; set; } = "_infrawatch_canary.tmp";
}
