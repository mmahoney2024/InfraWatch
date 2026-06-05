namespace InfraWatch.Collectors.Veeam;

public sealed class VeeamOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Backup &amp; Replication REST base URL, e.g. https://veeam:9419</summary>
    public string BaseUrl { get; set; } = "";

    public string Username { get; set; } = "";

    /// <summary>Password (a secret — inject via user-secrets/env, not appsettings in source).</summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// REST API version header (x-api-version). Depends on the B&amp;R build:
    /// v12.0 = 1.1-rev0, v12.1 = 1.1-rev2, v12.2 = 1.2-rev0, v12.3 = 1.2-rev1.
    /// </summary>
    public string ApiVersion { get; set; } = "1.1-rev2";

    /// <summary>Alert if a job has had no successful run within this many hours.</summary>
    public int RpoHours { get; set; } = 24;

    /// <summary>Repository free space below this percent is Warning.</summary>
    public double RepoFreeWarnPct { get; set; } = 10;

    /// <summary>B&amp;R uses a self-signed cert by default; accept it unless told otherwise.</summary>
    public bool IgnoreCertErrors { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);
}
