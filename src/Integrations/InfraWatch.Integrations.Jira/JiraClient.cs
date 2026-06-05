using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace InfraWatch.Integrations.Jira;

/// <summary>
/// Thin Jira Cloud REST v3 client (enhanced JQL search + approximate count). Authentication
/// is configured on the typed <see cref="HttpClient"/> in the DI extension.
/// </summary>
public sealed class JiraClient
{
    private const int PageSize = 100;
    private const int HardCap = 2000;

    private static readonly string[] IssueFields =
        ["summary", "status", "priority", "assignee", "created", "project", "resolutiondate"];

    // Unanswered detection also needs the comments (to inspect author account types).
    private static readonly string[] UnansweredFields =
        ["summary", "status", "priority", "assignee", "created", "project", "comment"];

    private readonly HttpClient _http;
    private readonly JiraOptions _options;

    public JiraClient(HttpClient http, IOptions<JiraOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string BaseUrl => _options.BaseUrl;

    /// <summary>Approximate count for a JQL query.</summary>
    public async Task<int> CountAsync(string jql, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(
            "/rest/api/3/search/approximate-count", new { jql }, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
    }

    /// <summary>Search returning flattened issues, paged up to a cap.</summary>
    public async Task<List<JiraIssue>> SearchIssuesAsync(string jql, int cap, CancellationToken ct)
    {
        var issues = new List<JiraIssue>();
        await foreach (var el in EnumerateAsync(jql, cap, IssueFields, ct))
            issues.Add(MapIssue(el));
        return issues;
    }

    /// <summary>Like <see cref="SearchIssuesAsync"/> but pulls comments too, so each issue's
    /// <see cref="JiraIssue.LastSupportReply"/> (last agent comment) is populated.</summary>
    public async Task<List<JiraIssue>> SearchIssuesWithRepliesAsync(string jql, int cap, CancellationToken ct)
    {
        var issues = new List<JiraIssue>();
        await foreach (var el in EnumerateAsync(jql, cap, UnansweredFields, ct))
            issues.Add(MapIssue(el));
        return issues;
    }

    /// <summary>
    /// Search for unanswered issues: candidates from the JQL, then keep only those with no
    /// public reply from anyone other than the reporter (a proxy for "first response not
    /// sent"). Over-fetches candidates because some are filtered out as answered.
    /// </summary>
    public async Task<List<JiraIssue>> SearchUnansweredAsync(string jql, int cap, CancellationToken ct)
    {
        var buffer = Math.Min(HardCap, Math.Max(cap * 5, 50));
        var issues = new List<JiraIssue>();
        await foreach (var el in EnumerateAsync(jql, buffer, UnansweredFields, ct))
        {
            if (IsUnanswered(el))
                issues.Add(MapIssue(el));
            if (issues.Count >= cap)
                break;
        }
        return issues;
    }

    /// <summary>Pull a single date field from every issue matching the JQL (for trends).</summary>
    public async Task<List<DateTimeOffset>> SearchDatesAsync(string jql, string field, int cap, CancellationToken ct)
    {
        var dates = new List<DateTimeOffset>();
        await foreach (var el in EnumerateAsync(jql, cap, [field], ct))
        {
            if (el.TryGetProperty("fields", out var f)
                && f.TryGetProperty(field, out var d)
                && d.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(d.GetString(), out var dto))
            {
                dates.Add(dto);
            }
        }
        return dates;
    }

    private async IAsyncEnumerable<JsonElement> EnumerateAsync(
        string jql, int cap, string[] fields,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        cap = Math.Min(cap, HardCap);
        string? pageToken = null;
        var fetched = 0;

        while (fetched < cap)
        {
            var body = new
            {
                jql,
                maxResults = Math.Min(PageSize, cap - fetched),
                fields,
                nextPageToken = pageToken,
            };

            using var resp = await _http.PostAsJsonAsync("/rest/api/3/search/jql", body, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            if (root.TryGetProperty("issues", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in arr.EnumerateArray())
                {
                    // Clone so the element survives disposal of the JsonDocument.
                    yield return issue.Clone();
                    fetched++;
                }
            }

            pageToken = root.TryGetProperty("nextPageToken", out var tok) && tok.ValueKind == JsonValueKind.String
                ? tok.GetString()
                : null;

            if (string.IsNullOrEmpty(pageToken))
                break;
        }
    }

    private JiraIssue MapIssue(JsonElement issue)
    {
        var key = issue.GetProperty("key").GetString() ?? "";
        var f = issue.GetProperty("fields");

        var summary = Str(f, "summary") ?? "(no summary)";
        var created = f.TryGetProperty("created", out var cr) && DateTimeOffset.TryParse(cr.GetString(), out var c)
            ? c
            : DateTimeOffset.UtcNow;

        var status = "Unknown";
        var statusCategory = "undefined";
        if (f.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            status = Str(st, "name") ?? status;
            if (st.TryGetProperty("statusCategory", out var sc) && sc.ValueKind == JsonValueKind.Object)
                statusCategory = Str(sc, "key") ?? statusCategory;
        }

        var priority = f.TryGetProperty("priority", out var pr) && pr.ValueKind == JsonValueKind.Object
            ? Str(pr, "name") ?? "None"
            : "None";

        string? assignee = f.TryGetProperty("assignee", out var asg) && asg.ValueKind == JsonValueKind.Object
            ? Str(asg, "displayName")
            : null;

        var project = f.TryGetProperty("project", out var pj) && pj.ValueKind == JsonValueKind.Object
            ? Str(pj, "key") ?? ""
            : "";

        var ageHours = Math.Max(0, (DateTimeOffset.UtcNow - created).TotalHours);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/browse/{key}";

        return new JiraIssue(key, summary, project, status, statusCategory, priority, assignee, created, ageHours, url, LastAgentComment(issue));
    }

    /// <summary>Timestamp of the most recent comment from a real agent (accountType
    /// "atlassian"), or null if support hasn't replied. Requires the "comment" field.</summary>
    private static DateTimeOffset? LastAgentComment(JsonElement issue)
    {
        if (!issue.TryGetProperty("fields", out var f) || !f.TryGetProperty("comment", out var c)
            || c.ValueKind != JsonValueKind.Object
            || !c.TryGetProperty("comments", out var comments) || comments.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        DateTimeOffset? latest = null;
        foreach (var comment in comments.EnumerateArray())
        {
            if (comment.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object
                && Str(a, "accountType") == "atlassian"
                && comment.TryGetProperty("created", out var cr) && cr.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(cr.GetString(), out var when)
                && (latest is null || when > latest))
            {
                latest = when;
            }
        }
        return latest;
    }

    /// <summary>
    /// True when no real <em>agent</em> has commented yet — our proxy for "first response not
    /// sent". A reply counts only if its author is an agent (Jira <c>accountType</c> ==
    /// "atlassian"); automation ("app") and customer ("customer") comments are ignored, since
    /// JSM desks auto-reply on creation. Note: the search API may return only a subset of
    /// comments; for full fidelity the JSM first-response SLA would be the production signal.
    /// </summary>
    private static bool IsUnanswered(JsonElement issue)
    {
        if (!issue.TryGetProperty("fields", out var f)
            || !f.TryGetProperty("comment", out var c) || c.ValueKind != JsonValueKind.Object
            || !c.TryGetProperty("comments", out var comments) || comments.ValueKind != JsonValueKind.Array)
        {
            return true; // no comments at all -> awaiting first response
        }

        foreach (var comment in comments.EnumerateArray())
        {
            if (comment.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object
                && Str(a, "accountType") == "atlassian")
            {
                return false; // a real agent has replied -> answered
            }
        }
        return true; // only automation / customer comments -> still awaiting an agent
    }

    private static string? Str(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
