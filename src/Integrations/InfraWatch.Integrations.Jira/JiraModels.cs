namespace InfraWatch.Integrations.Jira;

/// <summary>One Jira issue, flattened for the dashboard.</summary>
public sealed record JiraIssue(
    string Key,
    string Summary,
    string Project,
    string Status,
    string StatusCategory,
    string Priority,
    string? Assignee,
    DateTimeOffset Created,
    double AgeHours,
    string Url,
    DateTimeOffset? LastSupportReply = null);

/// <summary>Created/resolved counts for a single day, for the month line graph.</summary>
public sealed record DayCount(string Date, int Created, int Resolved);

/// <summary>
/// The computed Jira view the dashboard renders. Recomputed each poll and held in
/// <see cref="JiraSnapshotCache"/>.
/// </summary>
public sealed record JiraDashboard
{
    public bool Configured { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public string BaseUrl { get; init; } = "";
    public IReadOnlyList<string> Projects { get; init; } = [];

    public int OpenCount { get; init; }
    public int WaitingCount { get; init; }
    public int CreatedThisMonth { get; init; }
    public int ResolvedThisMonth { get; init; }
    // Same month-to-date window in the previous month ("this point last month").
    public int CreatedLastMonthToDate { get; init; }
    public int ResolvedLastMonthToDate { get; init; }

    public IReadOnlyList<JiraIssue> Pressing { get; init; } = [];
    public IReadOnlyList<JiraIssue> Unanswered { get; init; } = [];
    public IReadOnlyList<JiraIssue> Timeclock { get; init; } = [];

    public bool TimeclockAlert { get; init; }
    public IReadOnlyList<DayCount> Trend { get; init; } = [];

    public static JiraDashboard NotConfigured(string baseUrl, IReadOnlyList<string> projects) => new()
    {
        Configured = false,
        Message = "Jira is not configured. Set Jira:Email and Jira:ApiToken (a secret) to enable.",
        BaseUrl = baseUrl,
        Projects = projects,
    };
}
