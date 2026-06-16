namespace InfraWatch.Collectors.Imaging;

/// <summary>
/// Imaging-server health. Generic across SmartDeploy (services + image/boot shares) and
/// legacy WDS/MDT (TFTP boot-file download). All checks are read-only and use the service
/// account's own Windows credentials. Imaging is a back-office function, so problems are
/// surfaced as Warning rather than Critical (it isn't a production outage).
/// </summary>
public sealed class ImagingOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Imaging server host — the label, and the machine the Windows services are
    /// queried on (e.g. <c>fs-aio.compass-tamu.tamu.edu</c>).</summary>
    public string Server { get; set; } = "";

    /// <summary>Windows services that should be running on the server. SmartDeploy:
    /// <c>SDApiService</c>, <c>SDClientService</c>; network boot: <c>WDSServer</c>.</summary>
    public List<string> Services { get; set; } = [];

    /// <summary>UNC image stores: inventoried for OS images, checked for reachability, and
    /// reported on for free disk space (e.g. <c>\\fs-aio\Images</c>).</summary>
    public List<string> ImageShares { get; set; } = [];

    /// <summary>Glob patterns counted as OS images in the image stores (default: <c>*.wim</c>).</summary>
    public List<string> ImagePatterns { get; set; } = [];

    /// <summary>Additional UNC shares to verify are reachable (boot media, user-state share…).</summary>
    public List<string> Shares { get; set; } = [];

    /// <summary>Specific UNC files that must exist and be readable (e.g. the boot WIM, boot.sdi).</summary>
    public List<string> Files { get; set; } = [];

    /// <summary>Warn when free space on an image volume drops below this percent (0 = don't check).</summary>
    public int DiskWarnPct { get; set; } = 10;

    /// <summary>Warn if the newest image (or a checked file) is older than this many days (0 = off).</summary>
    public int StaleWarnDays { get; set; }

    // --- optional TFTP PXE boot-file test (legacy WDS support; off unless configured) ---

    /// <summary>Boot files to download over TFTP, e.g. "boot\\x64\\wdsnbp.com".</summary>
    public List<string> TftpFiles { get; set; } = [];

    public int TftpTimeoutMs { get; set; } = 5000;

    /// <summary>Cap how much to download per file (a full boot.wim is huge).</summary>
    public int TftpMaxBytes { get; set; } = 1_048_576;
}
