using System.Globalization;
using System.Net;
using System.Text;
using InfraWatch.Core;
using InfraWatch.Integrations.Jira;

namespace InfraWatch.Web;

/// <summary>
/// Renders the dashboard as a single self-contained HTML page — no CDN/JS dependencies, so
/// it works on an isolated server. Auto-refreshes every 30s.
/// </summary>
public static class DashboardRenderer
{
    public static string Render(IReadOnlyList<HealthRecord> health, JiraDashboard jira)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="refresh" content="30">
            <title>InfraWatch</title>
            <style>
            :root{--ok:#1f9d55;--warn:#d9a200;--crit:#d23b3b;--unk:#8a909a;--ink:#1b2430;--bg:#f4f6f9;--card:#fff;--line:#e3e8ee}
            *{box-sizing:border-box}body{margin:0;font:14px/1.45 -apple-system,Segoe UI,Roboto,Arial,sans-serif;background:var(--bg);color:var(--ink)}
            header{background:var(--ink);color:#fff;padding:14px 22px;display:flex;align-items:baseline;gap:14px}
            header h1{font-size:18px;margin:0;letter-spacing:.3px}header .sub{color:#9fb0c3;font-size:12px}
            main{max-width:1100px;margin:0 auto;padding:20px}
            h2{font-size:13px;text-transform:uppercase;letter-spacing:.6px;color:#5a6573;margin:26px 0 10px}
            .tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px}
            .tile{background:var(--card);border:1px solid var(--line);border-left-width:5px;border-radius:8px;padding:14px}
            .tile .name{font-weight:600;font-size:13px;color:#5a6573;text-transform:uppercase;letter-spacing:.4px}
            .tile .big{font-size:22px;font-weight:700;margin:6px 0 2px}.tile .det{font-size:12px;color:#6b7480}
            .ok{border-left-color:var(--ok)}.warn{border-left-color:var(--warn)}.crit{border-left-color:var(--crit)}.unknown{border-left-color:var(--unk)}
            .dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:6px;vertical-align:middle}
            .d-ok{background:var(--ok)}.d-warn{background:var(--warn)}.d-crit{background:var(--crit)}.d-unknown{background:var(--unk)}
            .stats{display:flex;gap:12px;flex-wrap:wrap}.stat{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px 16px;min-width:120px}
            .stat .n{font-size:24px;font-weight:700}.stat .l{font-size:12px;color:#6b7480}
            .card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:14px 16px;margin-top:12px}
            table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;padding:7px 8px;border-bottom:1px solid var(--line)}
            th{color:#6b7480;font-weight:600;font-size:11px;text-transform:uppercase;letter-spacing:.4px}
            td.k a{color:#2b6cb0;text-decoration:none;font-weight:600}td.k a:hover{text-decoration:underline}
            .pill{display:inline-block;padding:1px 8px;border-radius:10px;font-size:11px;font-weight:600}
            .pill.crit{background:#fbe4e4;color:#a12020}.pill.warn{background:#fbf1d6;color:#8a6a00}.pill.ok{background:#e0f1e6;color:#1c6b3c}.pill.unknown{background:#eceef1;color:#5a6573}
            .alert{background:#fbe4e4;border:1px solid #f0bcbc;border-left:5px solid var(--crit);border-radius:8px;padding:12px 16px;margin-top:12px;color:#7a1d1d;font-weight:600}
            .notice{background:#fff8e6;border:1px solid #efdca0;border-radius:8px;padding:12px 16px;color:#6a5500}
            .muted{color:#8a909a}footer{max-width:1100px;margin:18px auto 40px;padding:0 20px;color:#9098a3;font-size:12px}
            svg{max-width:100%}.legend{font-size:12px;color:#6b7480;margin-top:6px}.legend b{font-weight:600}
            .sw{display:inline-block;width:11px;height:3px;vertical-align:middle;margin:0 4px 0 12px}
            </style></head><body>
            """);

        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        sb.Append($"<header><h1>InfraWatch</h1><span class=\"sub\">one pane of glass · generated {now} · auto-refresh 30s</span></header><main>");

        RenderTiles(sb, health, jira);
        RenderJira(sb, jira);
        RenderHostNet(sb, health);

        sb.Append("</main><footer>InfraWatch · Phase 0 walking skeleton · HostNet + Jira slice</footer></body></html>");
        return sb.ToString();
    }

    private static void RenderTiles(StringBuilder sb, IReadOnlyList<HealthRecord> health, JiraDashboard jira)
    {
        sb.Append("<h2>Status</h2><section class=\"tiles\">");

        // Host/Net pillar tile
        var hostNet = health.Where(h => h.Pillar == "HostNet").ToList();
        var hnStatus = Worst(hostNet);
        var hnWarn = hostNet.Count(h => h.Status == HealthStatus.Warning);
        var hnCrit = hostNet.Count(h => h.Status is HealthStatus.Critical or HealthStatus.Unknown);
        Tile(sb, "Host / Net", hnStatus,
            hostNet.Count == 0 ? "—" : $"{hostNet.Count} checks",
            hostNet.Count == 0 ? "no data yet" : $"{hnWarn} warning · {hnCrit} critical");

        // Jira pillar tile
        if (jira.Configured)
        {
            var jStatus = jira.Unanswered.Count > 0 ? HealthStatus.Warning : HealthStatus.Healthy;
            Tile(sb, "Jira Service Desk", jStatus, $"{jira.OpenCount} open",
                $"{jira.WaitingCount} waiting · {jira.Unanswered.Count} unanswered");
        }
        else
        {
            Tile(sb, "Jira Service Desk", HealthStatus.Unknown, "—", "not configured");
        }

        // Timeclock alert tile
        var tcStatus = jira.TimeclockAlert ? HealthStatus.Critical : HealthStatus.Healthy;
        Tile(sb, "⏰ Timeclock", tcStatus,
            jira.Configured ? jira.Timeclock.Count.ToString() : "—",
            jira.TimeclockAlert ? "UNADDRESSED TICKETS" : (jira.Configured ? "all clear" : "not configured"));

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

        // Stats
        sb.Append("<div class=\"stats\">");
        Stat(sb, jira.OpenCount, "Open");
        Stat(sb, jira.WaitingCount, "Waiting for support");
        Stat(sb, jira.CreatedThisMonth, "Created this month");
        Stat(sb, jira.ResolvedThisMonth, "Resolved this month");
        Stat(sb, jira.Unanswered.Count, "Unanswered > 1 day");
        sb.Append("</div>");

        if (jira.TimeclockAlert)
            sb.Append($"<div class=\"alert\">⏰ {jira.Timeclock.Count} open timeclock ticket(s) need attention.</div>");

        // Line chart
        sb.Append("<div class=\"card\"><b>Open vs. closed this month</b>")
          .Append(BuildChart(jira.Trend))
          .Append("<div class=\"legend\"><span class=\"sw\" style=\"background:#2b6cb0\"></span><b>Created</b>")
          .Append("<span class=\"sw\" style=\"background:#1f9d55\"></span><b>Resolved</b></div></div>");

        IssueTable(sb, "Most pressing tickets", jira.Pressing, showPriority: true);
        IssueTable(sb, "Unanswered > 1 day", jira.Unanswered, showPriority: false);
        if (jira.Timeclock.Count > 0)
            IssueTable(sb, "⏰ Open timeclock tickets", jira.Timeclock, showPriority: false);
    }

    private static void RenderHostNet(StringBuilder sb, IReadOnlyList<HealthRecord> health)
    {
        var hostNet = health.Where(h => h.Pillar == "HostNet")
            .OrderBy(h => h.Check).ThenBy(h => h.Target).ToList();
        sb.Append("<h2>Host / Net checks</h2>");
        if (hostNet.Count == 0)
        {
            sb.Append("<div class=\"card muted\">No checks recorded yet — the first collection runs at startup.</div>");
            return;
        }

        sb.Append("<div class=\"card\"><table><tr><th>Target</th><th>Check</th><th>Status</th><th>Result</th><th>When</th></tr>");
        foreach (var h in hostNet)
        {
            sb.Append("<tr>")
              .Append($"<td>{Enc(h.Target)}</td>")
              .Append($"<td>{Enc(h.Check)}</td>")
              .Append($"<td>{Pill(h.Status)}</td>")
              .Append($"<td>{Enc(h.Summary ?? "")}</td>")
              .Append($"<td class=\"muted\">{h.Timestamp.ToLocalTime():HH:mm:ss}</td>")
              .Append("</tr>");
        }
        sb.Append("</table></div>");
    }

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
        // baseline + max gridline
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

    private static void Tile(StringBuilder sb, string name, HealthStatus status, string big, string detail) =>
        sb.Append($"<div class=\"tile {Cls(status)}\"><div class=\"name\"><span class=\"dot d-{Cls(status)}\"></span>{Enc(name)}</div>")
          .Append($"<div class=\"big\">{Enc(big)}</div><div class=\"det\">{Enc(detail)}</div></div>");

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

    private static string FmtAge(double hours) =>
        hours >= 48 ? $"{hours / 24:0}d" : $"{hours:0}h";

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
