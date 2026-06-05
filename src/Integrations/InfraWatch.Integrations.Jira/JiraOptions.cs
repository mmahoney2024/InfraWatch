namespace InfraWatch.Integrations.Jira;

public sealed class JiraOptions
{
    /// <summary>e.g. https://sscserv.atlassian.net</summary>
    public string BaseUrl { get; set; } = "https://sscserv.atlassian.net";

    /// <summary>Account email for Basic auth. Leave empty to disable the integration.</summary>
    public string Email { get; set; } = "";

    /// <summary>API token (a secret — inject via env/user-secrets, not appsettings in source).</summary>
    public string ApiToken { get; set; } = "";

    /// <summary>Service-desk projects to cover.</summary>
    public List<string> Projects { get; set; } = ["IMS", "CHG", "CSI"];

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A "Waiting for support" ticket older than this counts as unanswered.</summary>
    public int UnansweredAgeHours { get; set; } = 24;

    public int MaxPressing { get; set; } = 10;
    public int MaxUnanswered { get; set; } = 15;
    public int MaxTimeclock { get; set; } = 25;

    public TimeclockOptions Timeclock { get; set; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(ApiToken)
        && Projects.Count > 0;

    public sealed class TimeclockOptions
    {
        public List<string> Keywords { get; set; } = ["timeclock", "time clock"];
        public bool AlertWhenOpen { get; set; } = true;
    }
}
