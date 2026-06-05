namespace InfraWatch.Integrations.Jira;

/// <summary>
/// Holds the most recent <see cref="JiraDashboard"/> so the web layer can render the Jira
/// widgets instantly without re-querying Jira on every page load. Updated by the collector.
/// </summary>
public sealed class JiraSnapshotCache
{
    private volatile JiraDashboard _current =
        new() { Configured = false, Message = "No Jira data collected yet." };

    public JiraDashboard Current => _current;

    public void Update(JiraDashboard dashboard) => _current = dashboard;
}
