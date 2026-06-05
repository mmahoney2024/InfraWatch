using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.DirectoryServices.Protocols;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.ActiveDirectory;

/// <summary>
/// Active Directory health + inventory: discovers the domain/forest, inventories DCs, FSMO
/// role holders and sites, LDAP-binds each DC (latency), and checks replication neighbors.
/// Read-only; binds with the service account's own Windows credentials (Negotiate/Kerberos).
/// Must run on a domain-joined host.
/// </summary>
public sealed class ActiveDirectoryCollector : ICollector
{
    public const string Pillar = "ActiveDirectory";

    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<ActiveDirectoryCollector> _logger;

    public ActiveDirectoryCollector(IOptions<ActiveDirectoryOptions> options, ILogger<ActiveDirectoryCollector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => Pillar;
    public TimeSpan Interval => _options.Interval;

    public Task<CollectionResult> CollectAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Build(cancellationToken), cancellationToken);

    private CollectionResult Build(CancellationToken ct)
    {
        var health = new List<HealthRecord>();
        var inventory = new List<InventoryRecord>();

        Domain domain;
        Forest forest;
        try
        {
            domain = string.IsNullOrWhiteSpace(_options.Domain)
                ? Domain.GetCurrentDomain()
                : Domain.GetDomain(new DirectoryContext(DirectoryContextType.Domain, _options.Domain));
            forest = domain.Forest;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AD domain discovery failed");
            health.Add(H(string.IsNullOrWhiteSpace(_options.Domain) ? "domain" : _options.Domain,
                "discovery", HealthStatus.Unknown, null, $"AD not reachable: {ex.Message}"));
            return new CollectionResult(health, inventory);
        }

        var dcs = new List<DomainController>();
        try
        {
            foreach (DomainController dc in domain.DomainControllers)
                dcs.Add(dc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enumerating DCs failed");
        }

        health.Add(H(domain.Name, "discovery", HealthStatus.Healthy, dcs.Count, $"domain {domain.Name}, {dcs.Count} DC(s)"));

        foreach (var dc in dcs)
        {
            ct.ThrowIfCancellationRequested();
            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar, Kind = "dc", Key = dc.Name, Name = dc.Name,
                Attributes = new Dictionary<string, string>
                {
                    ["site"] = Safe(() => dc.SiteName),
                    ["ip"] = Safe(() => dc.IPAddress),
                    ["os"] = Safe(() => dc.OSVersion),
                    ["roles"] = Safe(() => string.Join(", ", dc.Roles.Cast<ActiveDirectoryRole>())),
                },
            });
        }

        // FSMO roles
        var fsmo = GetFsmo(domain, forest);
        foreach (var (role, holder) in fsmo)
        {
            inventory.Add(new InventoryRecord
            {
                Pillar = Pillar, Kind = "fsmo", Key = role, Name = holder,
                Attributes = new Dictionary<string, string> { ["role"] = role, ["holder"] = holder },
            });
        }
        health.Add(H("FSMO", "roles", fsmo.Count == 5 ? HealthStatus.Healthy : HealthStatus.Warning,
            fsmo.Count, $"{fsmo.Count}/5 FSMO role holders identified"));

        // Sites
        try
        {
            foreach (ActiveDirectorySite site in forest.Sites)
            {
                inventory.Add(new InventoryRecord
                {
                    Pillar = Pillar, Kind = "site", Key = site.Name, Name = site.Name,
                    Attributes = new Dictionary<string, string>
                    {
                        ["servers"] = Safe(() => site.Servers.Count.ToString()),
                        ["subnets"] = Safe(() => string.Join(", ", site.Subnets.Cast<ActiveDirectorySubnet>().Select(s => s.Name))),
                    },
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enumerating sites failed");
        }

        // LDAP bind per DC (configured hosts override discovery)
        var bindHosts = _options.DomainControllers.Count > 0
            ? _options.DomainControllers
            : dcs.Select(d => d.Name).ToList();
        foreach (var host in bindHosts)
        {
            ct.ThrowIfCancellationRequested();
            health.Add(LdapBind(host));
        }

        // Replication neighbors per DC
        if (_options.CheckReplication)
        {
            foreach (var dc in dcs)
            {
                ct.ThrowIfCancellationRequested();
                health.Add(Replication(dc));
            }
        }

        return new CollectionResult(health, inventory);
    }

    private HealthRecord LdapBind(string dcHost)
    {
        var port = _options.UseLdaps && _options.LdapPort == 389 ? 636 : _options.LdapPort;
        try
        {
            using var conn = new LdapConnection(new LdapDirectoryIdentifier(dcHost, port))
            {
                AuthType = AuthType.Negotiate,
                Timeout = TimeSpan.FromMilliseconds(_options.BindTimeoutMs),
            };
            conn.SessionOptions.ProtocolVersion = 3;
            if (_options.UseLdaps)
                conn.SessionOptions.SecureSocketLayer = true;

            var sw = Stopwatch.StartNew();
            conn.Bind(); // binds as the current (service) account
            sw.Stop();

            var ms = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            var status = ms >= _options.LdapWarnMs ? HealthStatus.Warning : HealthStatus.Healthy;
            return H(dcHost, _options.UseLdaps ? "ldaps-bind" : "ldap-bind", status, ms, $"bind OK ({ms} ms)");
        }
        catch (Exception ex)
        {
            return H(dcHost, _options.UseLdaps ? "ldaps-bind" : "ldap-bind", HealthStatus.Critical, null, $"bind failed: {ex.Message}");
        }
    }

    private HealthRecord Replication(DomainController dc)
    {
        try
        {
            var neighbors = dc.GetAllReplicationNeighbors();
            var total = neighbors.Count;
            var failed = neighbors.Cast<ReplicationNeighbor>().Count(n => n.LastSyncResult != 0);
            return H(dc.Name, "replication",
                failed == 0 ? HealthStatus.Healthy : HealthStatus.Critical, failed,
                failed == 0 ? $"{total} neighbor(s), all in sync" : $"{failed}/{total} replication failure(s)");
        }
        catch (Exception ex)
        {
            return H(dc.Name, "replication", HealthStatus.Unknown, null, $"could not query: {ex.Message}");
        }
    }

    private static Dictionary<string, string> GetFsmo(Domain domain, Forest forest)
    {
        var roles = new Dictionary<string, string>();
        void Try(string role, Func<string> get) { try { roles[role] = get(); } catch { /* unreachable role owner */ } }
        Try("PDC Emulator", () => domain.PdcRoleOwner.Name);
        Try("RID Master", () => domain.RidRoleOwner.Name);
        Try("Infrastructure Master", () => domain.InfrastructureRoleOwner.Name);
        Try("Schema Master", () => forest.SchemaRoleOwner.Name);
        Try("Domain Naming Master", () => forest.NamingRoleOwner.Name);
        return roles;
    }

    private static string Safe(Func<string?> get)
    {
        try { return get() ?? ""; } catch { return ""; }
    }

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = value is null ? null : (check.Contains("bind") ? "ms" : "count"),
        Summary = summary,
    };
}
