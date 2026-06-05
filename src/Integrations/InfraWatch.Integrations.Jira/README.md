# InfraWatch.Integrations.Jira

Polls Jira Cloud (`sscserv.atlassian.net`) and feeds the dashboard's Jira widgets.

Unlike the infra pillars under `src/Collectors/`, this is an **external SaaS integration**,
but it follows the same `ICollector` pattern: on a schedule it runs the JQL queries,
emits normalized records (summary counts + issue lists + month-trend snapshots) into the
store, and lets the engine raise the **timeclock alert**.

**Widgets it powers** (full spec in [`docs/JIRA.md`](../../../docs/JIRA.md)):
- Jira summary roll-up tile
- Most pressing open tickets
- Unanswered tickets > 1 day old
- Line graph: open vs closed this month
- Total tickets this month
- ⏰ Timeclock alert (red when any open timeclock ticket is unaddressed)

**Access method:** Jira Cloud REST API v3, HTTP Basic auth (`email:api-token`).

**Privileges required:** a least-privilege Jira account with **read** (`read:jira-work`)
on the configured projects (default: `IMS`, `CHG`, `CSI`). API token stored as a secret, never in
source (see [`docs/DEPLOYMENT.md`](../../../docs/DEPLOYMENT.md#5-service-account--secrets)).
