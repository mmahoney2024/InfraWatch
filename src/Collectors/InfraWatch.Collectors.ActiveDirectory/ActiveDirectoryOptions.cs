namespace InfraWatch.Collectors.ActiveDirectory;

public sealed class ActiveDirectoryOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Domain DNS name (e.g. compass-tamu.tamu.edu). Empty = the host's own domain.</summary>
    public string Domain { get; set; } = "";

    /// <summary>Explicit DCs to LDAP-bind. Empty = discover from the domain.</summary>
    public List<string> DomainControllers { get; set; } = [];

    public int LdapPort { get; set; } = 389;

    /// <summary>Use LDAPS (636 + TLS). Sets the port to 636 unless LdapPort was overridden.</summary>
    public bool UseLdaps { get; set; }

    public int BindTimeoutMs { get; set; } = 5000;

    /// <summary>LDAP bind at or above this many ms is Warning.</summary>
    public double LdapWarnMs { get; set; } = 500;

    /// <summary>Also check inbound replication neighbors per DC.</summary>
    public bool CheckReplication { get; set; } = true;
}
