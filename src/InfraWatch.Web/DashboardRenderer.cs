using System.Globalization;
using System.Net;
using System.Text;
using InfraWatch.Core;
using InfraWatch.Integrations.Jira;

namespace InfraWatch.Web;

/// <summary>
/// Renders the dashboard as self-contained HTML — no CDN/JS deps, works on an isolated
/// server, auto-refreshes every 30s. Three drill-down views: overview (tiles) → pillar
/// (its checks + inventory) → check (latest + history).
/// </summary>
public static class DashboardRenderer
{
    // ---- Views -------------------------------------------------------------

    /// <summary>Top level: the wall of status tiles + the Jira widgets.</summary>
    public static string RenderOverview(IReadOnlyList<HealthRecord> health, JiraDashboard jira, bool dark = false)
    {
        var sb = new StringBuilder();
        OpenPage(sb, dark, breadcrumb: null);
        RenderTiles(sb, health, jira);
        sb.Append("<div id=\"jira\"></div>");
        RenderJira(sb, jira);
        ClosePage(sb);
        return sb.ToString();
    }

    /// <summary>Drill 1: one pillar's targets (servers/endpoints), each rolled up and
    /// linking to its own detail page. Inventory (documentation) follows.</summary>
    public static string RenderPillar(
        string pillar, IReadOnlyList<HealthRecord> health,
        IReadOnlyList<InventoryRecord> inventory, bool dark = false)
    {
        var recs = health.Where(h => h.Pillar == pillar).ToList();
        var targets = recs.GroupBy(h => h.Target).OrderBy(g => g.Key).ToList();

        var sb = new StringBuilder();
        OpenPage(sb, dark, breadcrumb: $"<a href=\"/\">Overview</a> › {Enc(PillarName(pillar))}");
        sb.Append($"<h2>{Enc(PillarName(pillar))} — {targets.Count} {(targets.Count == 1 ? "target" : "targets")}</h2>");

        if (targets.Count == 0)
        {
            sb.Append("<div class=\"card muted\">No checks for this pillar yet.</div>");
        }
        else
        {
            sb.Append("<div class=\"card\"><table><tr><th>Target</th><th>Status</th><th>Checks</th><th>Detail</th></tr>");
            foreach (var g in targets)
            {
                var list = g.ToList();
                var ok = list.Count(h => h.Status == HealthStatus.Healthy);
                var warn = list.Count(h => h.Status == HealthStatus.Warning);
                var crit = list.Count(h => h.Status is HealthStatus.Critical or HealthStatus.Unknown);
                var url = $"/target?pillar={Esc(pillar)}&target={Esc(g.Key)}";
                sb.Append("<tr>")
                  .Append($"<td class=\"k\"><a href=\"{Enc(url)}\">{Enc(g.Key)}</a></td>")
                  .Append($"<td>{Pill(Worst(list))}</td>")
                  .Append($"<td>{list.Count}</td>")
                  .Append($"<td class=\"muted\">{ok} OK · {warn} warn · {crit} crit</td>")
                  .Append("</tr>");
            }
            sb.Append("</table></div>");
        }

        RenderInventory(sb, inventory);
        ClosePage(sb);
        return sb.ToString();
    }

    /// <summary>Drill 2: one target (server/endpoint) — its individual checks (each links to
    /// history) plus the inventory that belongs to it (e.g. a host's VMs).</summary>
    public static string RenderTarget(
        string pillar, string target, IReadOnlyList<HealthRecord> health,
        IReadOnlyList<InventoryRecord> inventory, bool dark = false)
    {
        var recs = health.Where(h => h.Pillar == pillar && h.Target == target)
            .OrderBy(h => h.Check).ToList();

        var sb = new StringBuilder();
        var crumb = $"<a href=\"/\">Overview</a> › <a href=\"/pillar?name={Esc(pillar)}\">{Enc(PillarName(pillar))}</a> › {Enc(target)}";
        OpenPage(sb, dark, crumb);
        sb.Append($"<h2>{Enc(target)} — checks</h2>");

        if (recs.Count == 0)
        {
            sb.Append("<div class=\"card muted\">No checks for this target.</div>");
        }
        else
        {
            sb.Append("<div class=\"card\"><table><tr><th>Check</th><th>Status</th><th>Result</th><th>When</th></tr>");
            foreach (var h in recs)
            {
                var url = $"/check?pillar={Esc(h.Pillar)}&target={Esc(h.Target)}&check={Esc(h.Check)}";
                sb.Append("<tr>")
                  .Append($"<td class=\"k\"><a href=\"{Enc(url)}\">{Enc(h.Check)}</a></td>")
                  .Append($"<td>{Pill(h.Status)}</td>")
                  .Append($"<td>{Enc(h.Summary ?? "")}</td>")
                  .Append($"<td class=\"muted\">{h.Timestamp.ToLocalTime():HH:mm:ss}</td>")
                  .Append("</tr>");
            }
            sb.Append("</table></div>");
        }

        if (pillar == "Dhcp")
            RenderDhcpTest(sb, target);

        var related = inventory.Where(i => BelongsToTarget(i, target)).ToList();
        RenderInventory(sb, related);
        ClosePage(sb);
        return sb.ToString();
    }

    private static void RenderDhcpTest(StringBuilder sb, string server)
    {
        sb.Append("<h2>Active test</h2><div class=\"card\">")
          .Append("<div>Send a real DHCP <b>DISCOVER</b> to this server and show whether it OFFERs an address. ")
          .Append("<span class=\"muted\">Intrusive probe (uses a dedicated probe MAC); needs <code>Dhcp:RelayAddress</code> set.</span></div>")
          .Append("<div style=\"margin-top:10px\">")
          .Append($"<button class=\"btn\" onclick=\"iwDhcp('{Enc(server)}',false)\">Run DISCOVER → OFFER</button>")
          .Append($"<button class=\"btn intrusive\" onclick=\"iwDhcp('{Enc(server)}',true)\">Full lease (acquire + release)</button>")
          .Append("</div><div id=\"dhcpResult\" class=\"muted\" style=\"margin-top:10px\">—</div></div>")
          .Append("<script>function iwDhcp(s,l){var e=document.getElementById('dhcpResult');")
          .Append("e.textContent='Testing '+s+'… (up to ~5s)';")
          .Append("fetch('/dhcp-test?server='+encodeURIComponent(s)+'&lease='+l)")
          .Append(".then(r=>r.text()).then(t=>{e.textContent=t;}).catch(x=>{e.textContent='Error: '+x;});}</script>");
    }

    private static bool BelongsToTarget(InventoryRecord i, string target) =>
        string.Equals(i.Key, target, StringComparison.OrdinalIgnoreCase)
        || i.Key.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase)
        || string.Equals(i.Name, target, StringComparison.OrdinalIgnoreCase)
        || (i.Attributes?.Values.Any(v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase)) ?? false);

    /// <summary>Drill 2: a single check — latest state, value sparkline, and history.</summary>
    public static string RenderCheck(
        string pillar, string target, string check,
        IReadOnlyList<HealthRecord> history, bool dark = false)
    {
        var sb = new StringBuilder();
        var crumb = $"<a href=\"/\">Overview</a> › <a href=\"/pillar?name={Esc(pillar)}\">{Enc(PillarName(pillar))}</a> › <a href=\"/target?pillar={Esc(pillar)}&target={Esc(target)}\">{Enc(target)}</a> › {Enc(check)}";
        OpenPage(sb, dark, crumb);

        if (history.Count == 0)
        {
            sb.Append("<div class=\"card muted\">No history recorded for this check.</div>");
            ClosePage(sb);
            return sb.ToString();
        }

        var latest = history[0]; // newest first
        sb.Append($"<h2>{Enc(target)} · {Enc(check)}</h2>");
        sb.Append("<div class=\"card\">")
          .Append($"<div style=\"font-size:20px;font-weight:700\">{Pill(latest.Status)} ")
          .Append($"{(latest.Value is { } v ? Enc($"{v} {latest.Unit}") : "")}</div>")
          .Append($"<div style=\"margin-top:4px\">{Enc(latest.Summary ?? "")}</div>")
          .Append($"<div class=\"muted\" style=\"margin-top:4px\">as of {latest.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {history.Count} samples</div>")
          .Append(BuildSparkline(history))
          .Append("</div>");

        sb.Append("<h2>History</h2><div class=\"card\"><table><tr><th>When</th><th>Status</th><th>Value</th><th>Result</th></tr>");
        foreach (var h in history.Take(150))
        {
            sb.Append("<tr>")
              .Append($"<td class=\"muted\">{h.Timestamp.ToLocalTime():MM-dd HH:mm:ss}</td>")
              .Append($"<td>{Pill(h.Status)}</td>")
              .Append($"<td>{(h.Value is { } hv ? Enc($"{hv} {h.Unit}") : "")}</td>")
              .Append($"<td>{Enc(h.Summary ?? "")}</td>")
              .Append("</tr>");
        }
        sb.Append("</table></div>");

        ClosePage(sb);
        return sb.ToString();
    }

    // ---- Page chrome -------------------------------------------------------

    private static void OpenPage(StringBuilder sb, bool dark, string? breadcrumb)
    {
        var theme = dark ? "dark" : "light";
        var themeIcon = dark ? "☀" : "🌙";

        sb.Append($"<!doctype html><html lang=\"en\" data-theme=\"{theme}\">");
        sb.Append("""
            <head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="refresh" content="30">
            <title>InfraWatch</title>
            <style>
            :root{--ok:#1f9d55;--warn:#d9a200;--crit:#d23b3b;--unk:#8a909a;--ink:#1b2430;--bg:#f4f6f9;--card:#fff;--line:#e3e8ee;--header:#1b2430;--muted:#6b7480;--muted2:#5a6573}
            html[data-theme="dark"]{--ink:#e6e9ee;--bg:#0f141a;--card:#1a212b;--line:#2a323d;--muted:#9aa4b1;--muted2:#aab3c0}
            *{box-sizing:border-box}body{margin:0;font:14px/1.45 -apple-system,Segoe UI,Roboto,Arial,sans-serif;background:var(--bg);color:var(--ink)}
            header{background:var(--header);color:#fff;padding:14px 22px;display:flex;align-items:center;gap:14px}
            header h1{font-size:18px;margin:0;letter-spacing:.3px}header h1 a{color:#fff;text-decoration:none}header .sub{color:#9fb0c3;font-size:12px}
            .themeBtn{margin-left:auto;background:transparent;border:1px solid #ffffff55;color:#fff;border-radius:6px;padding:4px 10px;font-size:15px;line-height:1;cursor:pointer}
            .themeBtn:hover{background:#ffffff22}
            .btn{background:#2b6cb0;color:#fff;border:none;border-radius:6px;padding:7px 13px;font-size:13px;cursor:pointer;margin:0 8px 0 0}
            .btn:hover{background:#255a96}.btn.intrusive{background:#b9770a}.btn.intrusive:hover{background:#9c6309}
            main{max-width:1100px;margin:0 auto;padding:20px}
            .crumb{font-size:13px;color:var(--muted);margin-bottom:6px}.crumb a{color:#4a90d9;text-decoration:none}.crumb a:hover{text-decoration:underline}
            h2{font-size:13px;text-transform:uppercase;letter-spacing:.6px;color:var(--muted2);margin:26px 0 10px}
            .tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px}
            a.tile{text-decoration:none;color:inherit;display:block}
            .tile{background:var(--card);border:1px solid var(--line);border-left-width:5px;border-radius:8px;padding:14px;transition:box-shadow .1s,transform .1s}
            a.tile:hover{box-shadow:0 2px 10px #0003;transform:translateY(-1px)}
            .tile .name{font-weight:600;font-size:13px;color:var(--muted2);text-transform:uppercase;letter-spacing:.4px}
            .tile .big{font-size:22px;font-weight:700;margin:6px 0 2px}.tile .det{font-size:12px;color:var(--muted)}
            .tile .more{font-size:11px;color:#4a90d9;margin-top:6px}
            .ok{border-left-color:var(--ok)}.warn{border-left-color:var(--warn)}.crit{border-left-color:var(--crit)}.unknown{border-left-color:var(--unk)}
            .dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:6px;vertical-align:middle}
            .d-ok{background:var(--ok)}.d-warn{background:var(--warn)}.d-crit{background:var(--crit)}.d-unknown{background:var(--unk)}
            .stats{display:flex;gap:12px;flex-wrap:wrap}.stat{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px 16px;min-width:120px}
            .stat .n{font-size:24px;font-weight:700}.stat .l{font-size:12px;color:var(--muted)}
            .card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:14px 16px;margin-top:12px}
            table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;padding:7px 8px;border-bottom:1px solid var(--line);vertical-align:top}
            th{color:var(--muted);font-weight:600;font-size:11px;text-transform:uppercase;letter-spacing:.4px}
            td.k a{color:#4a90d9;text-decoration:none;font-weight:600}td.k a:hover{text-decoration:underline}
            .pill{display:inline-block;padding:1px 8px;border-radius:10px;font-size:11px;font-weight:600}
            .pill.crit{background:#fbe4e4;color:#a12020}.pill.warn{background:#fbf1d6;color:#8a6a00}.pill.ok{background:#e0f1e6;color:#1c6b3c}.pill.unknown{background:#eceef1;color:#5a6573}
            .alert{background:#fbe4e4;border:1px solid #f0bcbc;border-left:5px solid var(--crit);border-radius:8px;padding:12px 16px;margin-top:12px;color:#7a1d1d;font-weight:600}
            .notice{background:#fff8e6;border:1px solid #efdca0;border-radius:8px;padding:12px 16px;color:#6a5500}
            .muted{color:var(--muted)}footer{max-width:1100px;margin:18px auto 40px;padding:0 20px;color:var(--muted);font-size:12px}
            svg{max-width:100%}.legend{font-size:12px;color:var(--muted);margin-top:6px}.legend b{font-weight:600}
            .sw{display:inline-block;width:11px;height:3px;vertical-align:middle;margin:0 4px 0 12px}
            </style></head><body>
            """);

        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        sb.Append($"<header><h1><a href=\"/\">InfraWatch</a></h1><span class=\"sub\">one pane of glass · generated {now} · auto-refresh 30s</span>");
        sb.Append($"<button id=\"themeBtn\" class=\"themeBtn\" onclick=\"iwToggleTheme()\" title=\"Toggle dark mode\">{themeIcon}</button></header>");
        sb.Append("<script>function iwToggleTheme(){var d=document.documentElement.getAttribute('data-theme')!=='dark';document.documentElement.setAttribute('data-theme',d?'dark':'light');document.cookie='iw-theme='+(d?'dark':'light')+';path=/;max-age=31536000;samesite=lax';document.getElementById('themeBtn').textContent=d?'☀':'🌙';}</script>");
        sb.Append("<main>");
        if (!string.IsNullOrEmpty(breadcrumb))
            sb.Append($"<div class=\"crumb\">{breadcrumb}</div>");
    }

    private static void ClosePage(StringBuilder sb) =>
        sb.Append("</main><footer>InfraWatch · roll up, then drill down</footer></body></html>");

    // ---- Overview sections -------------------------------------------------

    private static void RenderTiles(StringBuilder sb, IReadOnlyList<HealthRecord> health, JiraDashboard jira)
    {
        sb.Append("<h2>Status</h2><section class=\"tiles\">");

        foreach (var pillar in InfraPillars(health))
        {
            var recs = health.Where(h => h.Pillar == pillar).ToList();
            var warn = recs.Count(h => h.Status == HealthStatus.Warning);
            var crit = recs.Count(h => h.Status is HealthStatus.Critical or HealthStatus.Unknown);
            Tile(sb, PillarName(pillar), Worst(recs), $"{recs.Count} checks",
                $"{warn} warning · {crit} critical", href: $"/pillar?name={Esc(pillar)}");
        }

        if (jira.Configured)
        {
            var jStatus = jira.Unanswered.Count > 0 ? HealthStatus.Warning : HealthStatus.Healthy;
            Tile(sb, "Jira Service Desk", jStatus, $"{jira.OpenCount} open",
                $"{jira.WaitingCount} waiting · {jira.Unanswered.Count} unanswered", href: "#jira");
        }
        else
        {
            Tile(sb, "Jira Service Desk", HealthStatus.Unknown, "—", "not configured", href: "#jira");
        }

        var tcStatus = jira.TimeclockAlert ? HealthStatus.Critical : HealthStatus.Healthy;
        Tile(sb, "⏰ Timeclock", tcStatus,
            jira.Configured ? jira.Timeclock.Count.ToString() : "—",
            jira.TimeclockAlert ? "UNADDRESSED TICKETS" : (jira.Configured ? "all clear" : "not configured"),
            href: "#jira");

        sb.Append("</section>");
    }

    private static void RenderJira(StringBuilder sb, JiraDashboard jira)
    {
        sb.Append("<h2>Jira</h2>");

        if (!jira.Configured)
        {
            sb.Append($"<div class=\"notice\">{Enc(jira.Message ?? "Jira not configured.")} ")
              .Append("See <code>docs/JIRA.md</code> and set <code>Jira:Email</code> + <code>Jira:ApiToken</code>.</div>");
            return;
        }

        sb.Append("<div class=\"stats\">");
        Stat(sb, jira.OpenCount, "Open");
        Stat(sb, jira.WaitingCount, "Waiting for support");
        Stat(sb, jira.CreatedThisMonth, "Created this month");
        Stat(sb, jira.ResolvedThisMonth, "Resolved this month");
        Stat(sb, jira.Unanswered.Count, "Unanswered > 1 day");
        sb.Append("</div>");

        if (jira.TimeclockAlert)
            sb.Append($"<div class=\"alert\">⏰ {jira.Timeclock.Count} open timeclock ticket(s) need attention.</div>");

        sb.Append("<div class=\"card\"><b>Open vs. closed this month</b>")
          .Append(BuildChart(jira.Trend))
          .Append("<div class=\"legend\"><span class=\"sw\" style=\"background:#2b6cb0\"></span><b>Created</b>")
          .Append("<span class=\"sw\" style=\"background:#1f9d55\"></span><b>Resolved</b></div></div>");

        IssueTable(sb, "Most pressing tickets", jira.Pressing, showPriority: true);
        IssueTable(sb, "Unanswered > 1 day", jira.Unanswered, showPriority: false);
        if (jira.Timeclock.Count > 0)
            IssueTable(sb, "⏰ Open timeclock tickets", jira.Timeclock, showPriority: false);
    }

    // ---- Pillar inventory --------------------------------------------------

    private static void RenderInventory(StringBuilder sb, IReadOnlyList<InventoryRecord> inventory)
    {
        if (inventory.Count == 0)
            return;

        foreach (var group in inventory.GroupBy(i => i.Kind).OrderBy(g => g.Key))
        {
            var rows = group.OrderBy(i => i.Name).ToList();
            var attrKeys = rows.SelectMany(r => r.Attributes?.Keys ?? [])
                .Distinct().ToList();

            sb.Append($"<h2>Inventory — {Enc(group.Key)} ({rows.Count})</h2><div class=\"card\"><table><tr><th>Name</th>");
            foreach (var k in attrKeys)
                sb.Append($"<th>{Enc(k)}</th>");
            sb.Append("</tr>");

            foreach (var r in rows)
            {
                sb.Append($"<tr><td>{Enc(r.Name)}</td>");
                foreach (var k in attrKeys)
                {
                    var val = r.Attributes is not null && r.Attributes.TryGetValue(k, out var v) ? v : "";
                    sb.Append($"<td>{Enc(val)}</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table></div>");
        }
    }

    // ---- Shared bits -------------------------------------------------------

    private static readonly string[] PillarOrder =
        ["HostNet", "Dns", "Dhcp", "Smb", "ActiveDirectory", "HyperV", "Veeam"];

    private static IEnumerable<string> InfraPillars(IReadOnlyList<HealthRecord> health)
    {
        var present = health.Select(h => h.Pillar).Where(p => p != "Jira").ToHashSet();
        foreach (var p in PillarOrder)
            if (present.Remove(p)) yield return p;
        foreach (var p in present.OrderBy(x => x))
            yield return p;
    }

    private static string PillarName(string pillar) => pillar switch
    {
        "HostNet" => "Host / Net",
        "Dns" => "DNS",
        "Dhcp" => "DHCP",
        "Smb" => "SMB / File",
        "ActiveDirectory" => "Active Directory",
        "HyperV" => "Hyper-V",
        "Veeam" => "Veeam",
        _ => pillar,
    };

    private static void IssueTable(StringBuilder sb, string title, IReadOnlyList<JiraIssue> issues, bool showPriority)
    {
        sb.Append($"<div class=\"card\"><b>{Enc(title)}</b> <span class=\"muted\">({issues.Count})</span>");
        if (issues.Count == 0)
        {
            sb.Append("<div class=\"muted\" style=\"margin-top:6px\">None 🎉</div></div>");
            return;
        }
        sb.Append("<table><tr><th>Key</th><th>Summary</th>");
        if (showPriority) sb.Append("<th>Priority</th>");
        sb.Append("<th>Status</th><th>Age</th><th>Project</th></tr>");
        foreach (var i in issues)
        {
            sb.Append("<tr>")
              .Append($"<td class=\"k\"><a href=\"{Enc(i.Url)}\" target=\"_blank\" rel=\"noopener\">{Enc(i.Key)}</a></td>")
              .Append($"<td>{Enc(Trunc(i.Summary, 80))}</td>");
            if (showPriority) sb.Append($"<td>{Enc(i.Priority)}</td>");
            sb.Append($"<td>{Enc(i.Status)}</td>")
              .Append($"<td>{FmtAge(i.AgeHours)}</td>")
              .Append($"<td>{Enc(i.Project)}</td>")
              .Append("</tr>");
        }
        sb.Append("</table></div>");
    }

    private static string BuildChart(IReadOnlyList<DayCount> trend)
    {
        if (trend.Count == 0)
            return "<div class=\"muted\" style=\"margin-top:6px\">No data this month yet.</div>";

        const int w = 680, h = 200, padL = 30, padB = 22, padT = 10, padR = 10;
        var plotW = w - padL - padR;
        var plotH = h - padB - padT;
        var max = Math.Max(1, trend.Max(t => Math.Max(t.Created, t.Resolved)));
        var n = trend.Count;

        double X(int idx) => padL + (n == 1 ? plotW / 2.0 : plotW * idx / (double)(n - 1));
        double Y(int val) => padT + plotH - plotH * val / (double)max;

        string Line(Func<DayCount, int> sel, string color)
        {
            var pts = string.Join(" ", trend.Select((t, idx) =>
                string.Create(CultureInfo.InvariantCulture, $"{X(idx):0.#},{Y(sel(t)):0.#}")));
            return $"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" points=\"{pts}\"/>";
        }

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {w} {h}\" role=\"img\" style=\"margin-top:8px\">");
        sb.Append($"<line x1=\"{padL}\" y1=\"{padT + plotH}\" x2=\"{w - padR}\" y2=\"{padT + plotH}\" stroke=\"#e3e8ee\"/>");
        sb.Append($"<line x1=\"{padL}\" y1=\"{padT}\" x2=\"{w - padR}\" y2=\"{padT}\" stroke=\"#f0f3f6\"/>");
        sb.Append($"<text x=\"2\" y=\"{padT + 4}\" font-size=\"10\" fill=\"#8a909a\">{max}</text>");
        sb.Append($"<text x=\"2\" y=\"{padT + plotH}\" font-size=\"10\" fill=\"#8a909a\">0</text>");
        sb.Append($"<text x=\"{padL}\" y=\"{h - 6}\" font-size=\"10\" fill=\"#8a909a\">{Enc(trend[0].Date)}</text>");
        sb.Append($"<text x=\"{w - padR - 28}\" y=\"{h - 6}\" font-size=\"10\" fill=\"#8a909a\">{Enc(trend[^1].Date)}</text>");
        sb.Append(Line(t => t.Created, "#2b6cb0"));
        sb.Append(Line(t => t.Resolved, "#1f9d55"));
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string BuildSparkline(IReadOnlyList<HealthRecord> history)
    {
        // history is newest-first; chart oldest→newest left→right.
        var pts = history.Where(h => h.Value is not null).Reverse().Select(h => h.Value!.Value).ToList();
        if (pts.Count < 2)
            return "";

        const int w = 680, h = 90, padL = 30, padT = 8, padB = 8, padR = 10;
        var plotW = w - padL - padR;
        var plotH = h - padT - padB;
        var min = pts.Min();
        var max = pts.Max();
        var range = Math.Max(1e-9, max - min);
        var n = pts.Count;

        double X(int idx) => padL + plotW * idx / (double)(n - 1);
        double Y(double val) => padT + plotH - plotH * (val - min) / range;

        var poly = string.Join(" ", pts.Select((v, idx) =>
            string.Create(CultureInfo.InvariantCulture, $"{X(idx):0.#},{Y(v):0.#}")));

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {w} {h}\" role=\"img\" style=\"margin-top:10px\">");
        sb.Append($"<text x=\"2\" y=\"{padT + 6}\" font-size=\"10\" fill=\"#8a909a\">{Enc(Num(max))}</text>");
        sb.Append($"<text x=\"2\" y=\"{padT + plotH}\" font-size=\"10\" fill=\"#8a909a\">{Enc(Num(min))}</text>");
        sb.Append($"<polyline fill=\"none\" stroke=\"#2b6cb0\" stroke-width=\"2\" points=\"{poly}\"/>");
        sb.Append("</svg>");
        var unit = history.FirstOrDefault(h => h.Unit is not null)?.Unit;
        return sb.Append($"<div class=\"muted\" style=\"font-size:11px\">value over time{(unit is null ? "" : $" ({Enc(unit)})")}</div>").ToString();
    }

    private static string Num(double v) => v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.#", CultureInfo.InvariantCulture);

    private static void Tile(StringBuilder sb, string name, HealthStatus status, string big, string detail, string href) =>
        sb.Append($"<a class=\"tile {Cls(status)}\" href=\"{Enc(href)}\"><div class=\"name\"><span class=\"dot d-{Cls(status)}\"></span>{Enc(name)}</div>")
          .Append($"<div class=\"big\">{Enc(big)}</div><div class=\"det\">{Enc(detail)}</div><div class=\"more\">drill in ›</div></a>");

    private static void Stat(StringBuilder sb, int n, string label) =>
        sb.Append($"<div class=\"stat\"><div class=\"n\">{n}</div><div class=\"l\">{Enc(label)}</div></div>");

    private static string Pill(HealthStatus s) => $"<span class=\"pill {Cls(s)}\">{s}</span>";

    private static HealthStatus Worst(IEnumerable<HealthRecord> records)
    {
        var worst = HealthStatus.Healthy;
        var any = false;
        foreach (var r in records) { any = true; if (r.Status > worst) worst = r.Status; }
        return any ? worst : HealthStatus.Unknown;
    }

    private static string Cls(HealthStatus s) => s switch
    {
        HealthStatus.Healthy => "ok",
        HealthStatus.Warning => "warn",
        HealthStatus.Critical => "crit",
        _ => "unknown",
    };

    private static string FmtAge(double hours) => hours >= 48 ? $"{hours / 24:0}d" : $"{hours:0}h";

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string Esc(string s) => Uri.EscapeDataString(s);
}
