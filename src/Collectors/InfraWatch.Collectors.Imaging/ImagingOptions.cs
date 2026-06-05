namespace InfraWatch.Collectors.Imaging;

public sealed class ImagingOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>WDS / MDT server host name. Used for TFTP and the service check.</summary>
    public string Server { get; set; } = "";

    // --- TFTP boot-file download test ("what file it downloads") ---

    /// <summary>Boot files to download over TFTP, e.g. "boot\\x64\\wdsnbp.com".</summary>
    public List<string> TftpFiles { get; set; } = [];

    public int TftpTimeoutMs { get; set; } = 5000;

    /// <summary>Cap how much to download per file (a full boot.wim is huge).</summary>
    public int TftpMaxBytes { get; set; } = 1_048_576;

    // --- Deployment-share file checks (read-only) ---

    /// <summary>UNC path to the MDT deployment share, e.g. \\img\DeploymentShare$.</summary>
    public string DeploymentShare { get; set; } = "";

    /// <summary>Relative paths under the share that must exist + be readable,
    /// e.g. "Boot\\LiteTouchPE_x64.wim", "Control\\Bootstrap.ini".</summary>
    public List<string> ShareFiles { get; set; } = [];

    /// <summary>Warn if a checked file hasn't changed in this many days (0 = don't check).</summary>
    public int StaleDays { get; set; }

    // --- WDS service ---

    public bool CheckService { get; set; } = true;
    public string ServiceName { get; set; } = "WDSServer";
}
