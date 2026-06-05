# InfraWatch — Jira Integration

The dashboard includes a high-level Jira view alongside the infrastructure pillars. This
document specifies the widgets, the exact JQL behind them, and the field mappings —
grounded in the **`sscserv.atlassian.net`** instance and the **IMS** service desk.

> **Status:** design. No code yet (Phase 0).

---

## 1. Instance & scope

| Setting | Value |
|---|---|
| Site | `https://sscserv.atlassian.net` |
| Cloud ID | `abefebe5-f2a3-49ce-8cad-8db0e08a9ead` |
| Projects covered | **IMS** (IT helpdesk), **CHG** (Change Mgmt), **CSI** (CSI Refreshe) |
| Other live desk | CS (Customer Service) — available, off by default |
| Excluded | KAN / MDP / PBV (appear to be demo/sandbox projects) |

Which projects feed the widgets is **configurable**; the default set is `IMS, CHG, CSI`.

### Auth

Jira Cloud REST API v3 with an **API token** (HTTP Basic: `email:api-token`, base64). The
token belongs to a least-privilege account with read access to the monitored projects.
Stored as a secret (see [`DEPLOYMENT.md`](DEPLOYMENT.md#5-service-account--secrets)), never
in source. OAuth 2.0 (3LO) is a later option if a service principal is preferred.

## 2. Definitions (how we classify a ticket)

These are the assumptions baked into the JQL; all are configurable.

- **Open** — `statusCategory != Done`. In IMS that means *Waiting for support* /
  *In Progress*, i.e. not *Resolved* and not *Closed*.
- **Unanswered** — open, status `"Waiting for support"`, **older than 24h**, and **no
  agent has replied yet**. JQL gives the candidates (status + age); the code then keeps
  only those with no comment from a real agent — a comment counts as a reply only when its
  author's Jira `accountType` is `atlassian`, so JSM **automation** (`app`) auto-replies
  and **customer** (`customer`) comments are correctly ignored. (Production-grade option:
  the JSM "Time to first response" SLA field.)
- **Pressing** — open and high urgency: priority `High`/`Highest`, then oldest first.
- **Timeclock ticket** — summary or description matches `timeclock` **or** `time clock`.
  There is **no dedicated label or component** for these in IMS today — it is a keyword
  match. (Confirmed against real tickets: IMS-181, IMS-535, IMS-401, etc.) Keywords are
  configurable; if a `timeclock` label is later adopted, switch to `labels = timeclock`.
- **This month** — `>= startOfMonth()` in the instance timezone (US/Central).

## 3. Dashboard widgets

### 3.1 Jira summary tile (roll-up)
One tile per the pillar wall: open / waiting / in-progress / resolved-this-month counts
for the configured projects. Click to drill into the lists below.

### 3.2 Most pressing tickets
Top N (default 10) open tickets, highest urgency and oldest first.
```sql
project in (IMS, CHG, CSI) AND statusCategory != Done
ORDER BY priority DESC, created ASC
```

### 3.3 Unanswered tickets > 1 day old
Open tickets aging without a first **agent** response.
```sql
project in (IMS, CHG, CSI) AND statusCategory != Done
  AND status = "Waiting for support"
  AND created <= -1d
ORDER BY created ASC
```
Then drop any issue that already has a comment from an agent (`accountType == "atlassian"`);
automation and customer comments don't count. Shown as a list with age badges.

### 3.4 Line graph — open vs closed this month
Daily **created** vs **resolved** counts for the current month.
```sql
-- created per day this month
project in (IMS, CHG, CSI) AND created >= startOfMonth()
-- resolved per day this month
project in (IMS, CHG, CSI) AND resolved >= startOfMonth()
```
Backed by daily snapshots in the InfraWatch store, so the trend keeps building over time
(the append-only store is exactly what this needs — see `ARCHITECTURE.md`).

### 3.5 Total tickets this month (stat)
```sql
project in (IMS, CHG, CSI) AND created >= startOfMonth()
```
Single headline number, with prior-month delta.

### 3.6 Timeclock alert ⏰ (RED tile)
Fires if **any** open timeclock ticket is unaddressed.
```sql
project in (IMS, CHG, CSI) AND statusCategory != Done
  AND (summary ~ "timeclock" OR summary ~ "time clock"
       OR description ~ "timeclock" OR description ~ "time clock")
ORDER BY created ASC
```
- **Green** — no open timeclock tickets.
- **Red** — one or more open; tile shows the count and links straight to them. Optionally
  raises an alert through the configured channel (email/Teams/etc.) like any other red
  pillar.

## 4. Where this lives in the code

`src/Integrations/InfraWatch.Integrations.Jira` — a polling integration that implements
the same `ICollector` pattern as the infra pillars: on a schedule it queries the JQL above,
emits normalized records (counts + the issue lists) into the store, and lets the engine
raise the timeclock alert. The dashboard reads stored Jira snapshots, so widgets render
fast and the month-trend graph accumulates history.

Config (illustrative — secrets injected separately):
```jsonc
"Jira": {
  "BaseUrl": "https://sscserv.atlassian.net",
  "Projects": [ "IMS", "CHG", "CSI" ],
  "PollIntervalMinutes": 5,
  "UnansweredAgeHours": 24,
  "PressingPriorities": [ "Highest", "High" ],
  "Timeclock": { "Keywords": [ "timeclock", "time clock" ], "AlertWhenOpen": true }
}
```

## 5. Open questions for you

- Projects covered: **IMS + CHG + CSI** (decided 2026-06-05). Add **CS** too? (default: no)
- "Unanswered" = *no agent comment in 24h*, or simply *still "Waiting for support" after
  24h*? (default: no agent comment)
- Should the timeclock alert also push to a channel (Teams/email), or just light up the
  tile? (default: tile only until alerting is wired)
