namespace InfraWatch.Collectors.Web;

/// <summary>Web-server / site monitoring: HTTP availability + TLS certificate expiry.</summary>
public sealed class WebOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Sites to monitor. Configured via "Web:Sites" (empty default — config binding
    /// appends to a non-empty list, so defaults live in appsettings.json).</summary>
    public List<WebSite> Sites { get; set; } = [];

    /// <summary>HTTP request timeout.</summary>
    public int TimeoutMs { get; set; } = 10_000;

    /// <summary>Response slower than this (ms) is Warning (yellow).</summary>
    public double SlowMs { get; set; } = 2_000;

    /// <summary>Cert expiring within this many days is Warning (yellow).</summary>
    public int CertWarnDays { get; set; } = 30;

    /// <summary>Cert expiring within this many days (or expired) is Critical (red).</summary>
    public int CertCriticalDays { get; set; } = 7;
}

public sealed class WebSite
{
    public string Url { get; set; } = "";

    /// <summary>Friendly label; defaults to the host if unset.</summary>
    public string? Name { get; set; }

    /// <summary>If &gt; 0, require exactly this HTTP status; otherwise any 2xx/3xx is healthy.</summary>
    public int ExpectStatus { get; set; }
}
