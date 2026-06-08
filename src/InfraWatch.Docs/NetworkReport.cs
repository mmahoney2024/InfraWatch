using System.Text;
using InfraWatch.Core;
using Markdig;

namespace InfraWatch.Docs;

/// <summary>
/// Generates the "State of the Network" document from the store — a rendering of measured
/// reality (inventory + current health), not a hand-maintained doc. Markdown is the source
/// of truth; HTML is produced from it for the dashboard view.
/// </summary>
public sealed class NetworkReport
{
    private static readonly string[] PillarOrder =
        ["HostNet", "Dns", "Dhcp", "Smb", "ActiveDirectory", "HyperV", "Veeam", "Imaging"];

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private readonly IStore _store;

    public NetworkReport(IStore store) => _store = store;

    public async Task<string> GenerateHtmlBodyAsync(CancellationToken ct = default) =>
        Markdown.ToHtml(await GenerateMarkdownAsync(ct), Pipeline);

    public async Task<string> GenerateMarkdownAsync(CancellationToken ct = default)
    {
        var allHealth = await _store.GetLatestHealthAsync(ct);
        var health = allHealth.Where(h => h.Pillar != "Jira").ToList();
        var pillars = OrderPillars(health.Select(h => h.Pillar)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# InfraWatch — State of the Network").AppendLine();
        sb.AppendLine($"*Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm} — a rendering of measured reality, not hand-maintained.*")
          .AppendLine();

        // Overall status banner — the headline color.
        var warnAll = health.Count(h => h.Status == HealthStatus.Warning);
        var critAll = health.Count(h => h.Status is HealthStatus.Critical or HealthStatus.Unknown);
        var banner = critAll > 0
            ? $"🔴 **{critAll} critical**" + (warnAll > 0 ? $" · 🟡 {warnAll} warning{(warnAll == 1 ? "" : "s")}" : "")
            : warnAll > 0
                ? $"🟡 **{warnAll} warning{(warnAll == 1 ? "" : "s")}** · no critical issues"
                : "🟢 **All systems healthy**";
        sb.AppendLine($"> **Overall:** {banner} — {health.Count} checks across {pillars.Count} pillars.").AppendLine();

        sb.AppendLine("## Health summary").AppendLine();
        sb.AppendLine("| | Pillar | Checks | 🟢 OK | 🟡 Warning | 🔴 Critical |");
        sb.AppendLine("|:--:|---|--:|--:|--:|--:|");
        foreach (var p in pillars)
        {
            var recs = health.Where(h => h.Pillar == p).ToList();
            var ok = recs.Count(h => h.Status == HealthStatus.Healthy);
            var wn = recs.Count(h => h.Status == HealthStatus.Warning);
            var cr = recs.Count(h => h.Status is HealthStatus.Critical or HealthStatus.Unknown);
            sb.AppendLine($"| {Dot(Worst(recs))} | {PillarName(p)} | {recs.Count} | {ok} | {(wn > 0 ? wn.ToString() : "—")} | {(cr > 0 ? $"**{cr}**" : "—")} |");
        }
        sb.AppendLine();

        var changes = await _store.GetRecentChangesAsync(40, ct);
        if (changes.Count > 0)
        {
            sb.AppendLine("## Recent changes").AppendLine();
            sb.AppendLine("| When | Change | Pillar | Item | Detail |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var c in changes)
                sb.AppendLine($"| {c.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm} | {ChangeLabel(c.ChangeType)} | {PillarName(c.Pillar)} | {Esc(c.Name)} ({Esc(c.Kind)}) | {Esc(c.Detail ?? "")} |");
            sb.AppendLine();
        }

        foreach (var p in pillars)
        {
            var precs = health.Where(h => h.Pillar == p).ToList();
            sb.AppendLine($"## {Dot(Worst(precs))} {PillarName(p)}").AppendLine();

            var problems = precs.Where(h => h.Status is HealthStatus.Warning or HealthStatus.Critical)
                .OrderByDescending(h => h.Status).ToList();
            if (problems.Count > 0)
            {
                sb.AppendLine("**Attention:**").AppendLine();
                foreach (var h in problems)
                    sb.AppendLine($"- {(h.Status == HealthStatus.Critical ? "🔴" : "🟡")} `{Esc(h.Target)}` — {Esc(h.Check)}: {Esc(h.Summary ?? "")}");
                sb.AppendLine();
            }

            var inventory = await _store.GetLatestInventoryAsync(p, ct);
            if (inventory.Count == 0)
            {
                sb.AppendLine("*No inventory recorded yet.*").AppendLine();
                continue;
            }

            foreach (var group in inventory.GroupBy(i => i.Kind).OrderBy(g => g.Key))
            {
                var rows = group.OrderBy(i => i.Name).ToList();
                var attrKeys = rows.SelectMany(r => r.Attributes?.Keys ?? []).Distinct().ToList();

                sb.AppendLine($"### {KindName(group.Key)} ({rows.Count})").AppendLine();
                sb.Append("| Name |");
                foreach (var k in attrKeys) sb.Append($" {Esc(k)} |");
                sb.AppendLine();
                sb.Append("|---|");
                foreach (var _ in attrKeys) sb.Append("---|");
                sb.AppendLine();
                foreach (var rec in rows)
                {
                    sb.Append($"| {Esc(rec.Name)} |");
                    foreach (var k in attrKeys)
                        sb.Append($" {Esc(rec.Attributes is not null && rec.Attributes.TryGetValue(k, out var v) ? v : "")} |");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<string> OrderPillars(IEnumerable<string> present)
    {
        var set = present.ToHashSet();
        foreach (var p in PillarOrder)
            if (set.Remove(p)) yield return p;
        foreach (var p in set.OrderBy(x => x)) yield return p;
    }

    private static string PillarName(string p) => p switch
    {
        "HostNet" => "Host / Net",
        "Dns" => "DNS",
        "Dhcp" => "DHCP",
        "Smb" => "SMB / File",
        "ActiveDirectory" => "Active Directory",
        "HyperV" => "Hyper-V",
        "Veeam" => "Veeam",
        "Imaging" => "Imaging (WDS/MDT)",
        _ => p,
    };

    private static string KindName(string kind) => kind switch
    {
        "dc" => "Domain Controllers",
        "fsmo" => "FSMO Roles",
        "site" => "AD Sites",
        "vm" => "Virtual Machines",
        "host" => "Hosts",
        "share" => "Shares",
        "dns-record" => "DNS Records",
        "scope" => "DHCP Scopes",
        "job" => "Backup Jobs",
        "repository" => "Backup Repositories",
        "backup" => "Backups (per machine)",
        "cert" => "TLS Certificates",
        "boot-file" => "Boot Files",
        "file" => "Files",
        _ => kind,
    };

    private static string Dot(HealthStatus s) => s switch
    {
        HealthStatus.Healthy => "🟢",
        HealthStatus.Warning => "🟡",
        _ => "🔴",
    };

    private static HealthStatus Worst(IEnumerable<HealthRecord> recs)
    {
        var worst = HealthStatus.Healthy;
        foreach (var h in recs)
        {
            if (h.Status is HealthStatus.Critical or HealthStatus.Unknown) return HealthStatus.Critical;
            if (h.Status == HealthStatus.Warning) worst = HealthStatus.Warning;
        }
        return worst;
    }

    private static string ChangeLabel(string type) => type switch
    {
        "added" => "🟢 added",
        "removed" => "🔴 removed",
        "changed" => "🟡 changed",
        _ => type,
    };

    private static string Esc(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
