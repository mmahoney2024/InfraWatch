using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Integrations.Jira;

/// <summary>
/// Polls the Jira service desks (IMS/CHG/CSI by default), computes the dashboard view, and
/// emits health + inventory records. Degrades to "not configured" / Unknown when no API
/// token is set, so the app still runs without secrets.
/// </summary>
public sealed class JiraCollector : ICollector
{
    public const string Pillar = "Jira";

    private readonly JiraClient _client;
    private readonly JiraSnapshotCache _cache;
    private readonly JiraOptions _options;
    private readonly ILogger<JiraCollector> _logger;

    public JiraCollector(
        JiraClient client, JiraSnapshotCache cache,
        IOptions<JiraOptions> options, ILogger<JiraCollector> logger)
    {
        _client = client;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => Pillar;
    public TimeSpan Interval => _options.Interval;

    public async Task<CollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            _cache.Update(JiraDashboard.NotConfigured(_options.BaseUrl, _options.Projects));
            return new CollectionResult([
                new HealthRecord
                {
                    Pillar = Pillar, Target = "summary", Check = "configured",
                    Status = HealthStatus.Unknown, Summary = "Jira not configured (no API token).",
                },
            ]);
        }

        var projects = string.Join(", ", _options.Projects);
        var scope = $"project in ({projects})";
        var timeclock = TimeclockClause();

        var openJql = $"{scope} AND statusCategory != Done";
        var waitingJql = $"{scope} AND status = \"Waiting for support\"";
        var pressingJql = $"{scope} AND statusCategory != Done ORDER BY priority DESC, created ASC";
        var unansweredJql = $"{scope} AND statusCategory != Done AND status = \"Waiting for support\" " +
                            $"AND created <= \"-{_options.UnansweredAgeHours}h\" ORDER BY created ASC";
        var timeclockJql = $"{scope} AND statusCategory != Done AND ({timeclock}) ORDER BY created ASC";
        var createdMtdJql = $"{scope} AND created >= startOfMonth()";
        var resolvedMtdJql = $"{scope} AND resolved >= startOfMonth()";
        // "This point last month": same month-to-date window in the previous month.
        var now = DateTimeOffset.Now;
        var lmStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(-1);
        var lmCutoff = now.AddMonths(-1);
        var createdLastJql = $"{scope} AND created >= \"{lmStart:yyyy/MM/dd HH:mm}\" AND created < \"{lmCutoff:yyyy/MM/dd HH:mm}\"";
        var resolvedLastJql = $"{scope} AND resolved >= \"{lmStart:yyyy/MM/dd HH:mm}\" AND resolved < \"{lmCutoff:yyyy/MM/dd HH:mm}\"";

        var openCount = await _client.CountAsync(openJql, cancellationToken);
        var waitingCount = await _client.CountAsync(waitingJql, cancellationToken);
        var createdMtd = await _client.CountAsync(createdMtdJql, cancellationToken);
        var resolvedMtd = await _client.CountAsync(resolvedMtdJql, cancellationToken);
        var createdLast = await _client.CountAsync(createdLastJql, cancellationToken);
        var resolvedLast = await _client.CountAsync(resolvedLastJql, cancellationToken);

        var pressing = await _client.SearchIssuesAsync(pressingJql, _options.MaxPressing, cancellationToken);
        var unanswered = await _client.SearchUnansweredAsync(unansweredJql, _options.MaxUnanswered, cancellationToken);
        var timeclockIssues = await _client.SearchIssuesAsync(timeclockJql, _options.MaxTimeclock, cancellationToken);

        var createdDates = await _client.SearchDatesAsync(createdMtdJql, "created", 2000, cancellationToken);
        var resolvedDates = await _client.SearchDatesAsync(resolvedMtdJql, "resolutiondate", 2000, cancellationToken);
        var trend = BuildTrend(createdDates, resolvedDates);

        var timeclockAlert = _options.Timeclock.AlertWhenOpen && timeclockIssues.Count > 0;

        var dashboard = new JiraDashboard
        {
            Configured = true,
            BaseUrl = _options.BaseUrl,
            Projects = _options.Projects,
            OpenCount = openCount,
            WaitingCount = waitingCount,
            CreatedThisMonth = createdMtd,
            ResolvedThisMonth = resolvedMtd,
            CreatedLastMonthToDate = createdLast,
            ResolvedLastMonthToDate = resolvedLast,
            Pressing = pressing,
            Unanswered = unanswered,
            Timeclock = timeclockIssues,
            TimeclockAlert = timeclockAlert,
            Trend = trend,
        };
        _cache.Update(dashboard);

        _logger.LogInformation(
            "Jira: {Open} open, {Unanswered} unanswered>{Age}h, {Timeclock} timeclock (alert={Alert})",
            openCount, unanswered.Count, _options.UnansweredAgeHours, timeclockIssues.Count, timeclockAlert);

        return new CollectionResult(BuildHealth(dashboard), BuildInventory(dashboard));
    }

    private string TimeclockClause()
    {
        var parts = new List<string>();
        foreach (var kw in _options.Timeclock.Keywords)
        {
            var safe = kw.Replace("\"", "");
            parts.Add($"summary ~ \"{safe}\"");
            parts.Add($"description ~ \"{safe}\"");
        }
        return parts.Count > 0 ? string.Join(" OR ", parts) : "summary ~ \"timeclock\"";
    }

    private static IReadOnlyList<DayCount> BuildTrend(
        List<DateTimeOffset> created, List<DateTimeOffset> resolved)
    {
        var now = DateTimeOffset.Now;
        var createdByDay = created.GroupBy(d => d.Day).ToDictionary(g => g.Key, g => g.Count());
        var resolvedByDay = resolved.GroupBy(d => d.Day).ToDictionary(g => g.Key, g => g.Count());

        var trend = new List<DayCount>();
        for (var day = 1; day <= now.Day; day++)
        {
            trend.Add(new DayCount(
                $"{now.Month:00}-{day:00}",
                createdByDay.GetValueOrDefault(day),
                resolvedByDay.GetValueOrDefault(day)));
        }
        return trend;
    }

    private List<HealthRecord> BuildHealth(JiraDashboard d) =>
    [
        Summary("open", d.OpenCount, HealthStatus.Healthy, $"{d.OpenCount} open"),
        Summary("waiting", d.WaitingCount, HealthStatus.Healthy, $"{d.WaitingCount} waiting for support"),
        Summary("created-mtd", d.CreatedThisMonth, HealthStatus.Healthy, $"{d.CreatedThisMonth} created this month"),
        Summary("resolved-mtd", d.ResolvedThisMonth, HealthStatus.Healthy, $"{d.ResolvedThisMonth} resolved this month"),
        Summary("unanswered", d.Unanswered.Count,
            d.Unanswered.Count > 0 ? HealthStatus.Warning : HealthStatus.Healthy,
            $"{d.Unanswered.Count} unanswered > {_options.UnansweredAgeHours}h"),
        new HealthRecord
        {
            Pillar = Pillar, Target = "timeclock", Check = "open-unaddressed",
            Status = d.TimeclockAlert ? HealthStatus.Critical : HealthStatus.Healthy,
            Value = d.Timeclock.Count, Unit = "tickets",
            Summary = d.Timeclock.Count == 0
                ? "No open timeclock tickets"
                : $"{d.Timeclock.Count} open timeclock ticket(s)",
        },
    ];

    private static HealthRecord Summary(string check, int value, HealthStatus status, string summary) => new()
    {
        Pillar = Pillar, Target = "summary", Check = check,
        Status = status, Value = value, Unit = "tickets", Summary = summary,
    };

    private static List<InventoryRecord> BuildInventory(JiraDashboard d)
    {
        var byKey = new Dictionary<string, InventoryRecord>();
        void Add(JiraIssue i, string role)
        {
            var existingRoles = byKey.TryGetValue(i.Key, out var ex) && ex.Attributes is { } a
                && a.TryGetValue("roles", out var r) ? r + "," + role : role;
            byKey[i.Key] = new InventoryRecord
            {
                Pillar = Pillar, Kind = "jira-issue", Key = i.Key, Name = i.Summary,
                Attributes = new Dictionary<string, string>
                {
                    ["project"] = i.Project,
                    ["status"] = i.Status,
                    ["priority"] = i.Priority,
                    ["assignee"] = i.Assignee ?? "Unassigned",
                    ["ageHours"] = ((int)i.AgeHours).ToString(),
                    ["url"] = i.Url,
                    ["roles"] = existingRoles,
                },
            };
        }
        foreach (var i in d.Pressing) Add(i, "pressing");
        foreach (var i in d.Unanswered) Add(i, "unanswered");
        foreach (var i in d.Timeclock) Add(i, "timeclock");
        return byKey.Values.ToList();
    }
}
