namespace InfraWatch.Collectors.Dns;

public sealed class DnsOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Query at or above this many ms is Warning (yellow).</summary>
    public double WarnMs { get; set; } = 200;

    /// <summary>Per-query timeout in ms.</summary>
    public int TimeoutMs { get; set; } = 3000;

    /// <summary>Records to resolve and verify. Configured via the "Dns:Checks" section
    /// (empty by default — config binding appends to a non-empty list rather than
    /// replacing it, so the defaults live in appsettings.json instead).</summary>
    public List<DnsCheck> Checks { get; set; } = [];
}

public sealed class DnsCheck
{
    /// <summary>Name to resolve, e.g. "dc01.sscserv.com".</summary>
    public string Name { get; set; } = "";

    /// <summary>Record type: A, AAAA, CNAME, MX, TXT, NS, PTR, SOA.</summary>
    public string Type { get; set; } = "A";

    /// <summary>Resolver IP to query directly. Null/empty = the system resolver.</summary>
    public string? Server { get; set; }

    /// <summary>If set, at least one answer must contain this (verifies the expected answer).</summary>
    public string? Expect { get; set; }
}
