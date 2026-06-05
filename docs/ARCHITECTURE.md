# InfraWatch — Architecture

This document describes how the code is organized and how data flows through the system.
It is the developer-facing companion to [`CONCEPT.md`](CONCEPT.md), which explains *why*
the project exists.

---

## 1. Data flow

```
                 ┌──────────────┐
   probe infra → │  Collectors  │ → normalized HealthRecord + InventoryRecord
                 └──────┬───────┘
                        │
                 ┌──────▼───────┐
                 │   Storage    │  append-only history + current state + change/drift log
                 └──────┬───────┘
                        │
                 ┌──────▼───────┐
                 │    Engine    │  schedule collectors, detect drift, evaluate alert rules
                 └──┬────────┬──┘
                    │        │
          ┌─────────▼──┐  ┌──▼──────────┐
          │  Alerting  │  │ Docs render │  Markdown / PDF; optional wiki push
          └────────────┘  └─────────────┘
                    ▲
            ┌───────┴────────┐
            │  Web dashboard │  roll-up tiles → drill-down → raw detail + change log
            └────────────────┘
```

Everything is built around two normalized record types every collector emits:

- **HealthRecord** — "is it OK right now?" — target, check, status (green/yellow/red),
  measured values (latency, free space, lease %, cert days-to-expiry…), timestamp.
- **InventoryRecord** — "what exists?" — the measured facts that become documentation
  (a share, a DC, a VM, a DHCP scope, a backup job).

Documentation is a *rendering of stored InventoryRecords*; change history is the diff of
those records over time. Half of "why did it break?" is "what changed?" — so the store is
append-only and the engine computes drift.

---

## 2. Projects

| Project | Responsibility | Depends on |
|---|---|---|
| `InfraWatch.Core` | Domain models (`HealthRecord`, `InventoryRecord`, severity, target), and the `ICollector` / `IStore` / `IAlertChannel` abstractions. No I/O. | — |
| `InfraWatch.Storage` | SQLite-backed persistence: current state, history (time-series), and the change/drift log. Implements `IStore`. | Core |
| `InfraWatch.Engine` | Scheduler (when each collector runs), baseline/drift detection, alert-rule evaluation, flap detection, dedup. | Core, Storage |
| `InfraWatch.Alerting` | `IAlertChannel` implementations: email / Teams / ntfy / Discord. | Core |
| `InfraWatch.Docs` | Renders stored inventory + history to Markdown / PDF; optional wiki push. | Core, Storage |
| `InfraWatch.Service` | Composition root for a **split deployment** (collector host on a different box than the web host). Hosts the engine as a Windows service. Wires collectors → store → engine → alerting. | all |
| `InfraWatch.Web` | ASP.NET Core dashboard **and** the default deployable unit: serves the UI/API *and* runs the engine + collectors as hosted background services, so one install is web-accessible. Runs as a Windows service (`AddWindowsService()`), optionally behind IIS. | Core, Storage, Engine, Docs, collectors, integrations |
| `Collectors/InfraWatch.Collectors.*` | One project per infra pillar. Each implements `ICollector` and produces normalized health + inventory. | Core |
| `Integrations/InfraWatch.Integrations.*` | External SaaS data sources (e.g. **Jira**). Same `ICollector` pattern; poll an external API and emit normalized records. | Core |
| `tests/InfraWatch.Tests` | Unit/integration tests. | (project under test) |

### Collector projects

| Project | Pillar | Likely access method |
|---|---|---|
| `InfraWatch.Collectors.HostNet` | Host / Net | ICMP, raw TCP, `SslStream` (TLS cert), `HttpClient` |
| `InfraWatch.Collectors.Dns` | DNS | DnsClient — per-server or system resolver, A/AAAA/MX/TXT/NS/… |
| `InfraWatch.Collectors.Dhcp` | DHCP | `DhcpServer` PowerShell module / service + lease query |
| `InfraWatch.Collectors.Smb` | SMB / File | SMB client connect, share enumeration |
| `InfraWatch.Collectors.ActiveDirectory` | AD | `System.DirectoryServices`, `ActiveDirectory` PS module |
| `InfraWatch.Collectors.HyperV` | Hyper-V | CIM/WMI, `Hyper-V` + `FailoverClusters` PS modules |
| `InfraWatch.Collectors.Veeam` | Veeam B&R | B&R REST API / PowerShell module / config DB |

Why separate projects per collector: each pillar pulls in different, sometimes heavy
dependencies (PowerShell SDK, directory services, vendor APIs). Isolating them keeps the
core dependency-free, lets pillars be built/tested in isolation, and makes it trivial to
disable a pillar we don't have access to yet.

### Integration projects

| Project | Source | Access method |
|---|---|---|
| `InfraWatch.Integrations.Jira` | Jira Cloud (`sscserv.atlassian.net`, projects `IMS`, `CHG`, `CSI`) | REST API v3, Basic auth (`email:api-token`) |

Integrations are non-infra data sources that still feed the dashboard. Jira polls the
service desk on a schedule and emits counts + issue lists + a timeclock alert. Full spec
in [`JIRA.md`](JIRA.md).

---

## 2a. Deployment & hosting

The default deployable unit is **`InfraWatch.Web`**: it serves the dashboard and runs the
engine + collectors as `IHostedService` background services in the same process, installed
as a Windows service on one server and reached as a webpage (direct Kestrel, or behind IIS
for TLS + Windows auth). `InfraWatch.Service` covers a later **split** topology where the
collector host and web host are separate machines sharing a database. Full steps in
[`DEPLOYMENT.md`](DEPLOYMENT.md).

---

## 3. Key design decisions

- **Built fully in-house, no third-party monitoring/doc products.** See `CONCEPT.md` §5
  for the trade-offs we're accepting (we own retry/backoff, flap detection, time-series
  storage, alert dedup, dashboard plumbing).
- **Read-only first.** Default posture is observe-only. Writes (canary files) are opt-in
  and scoped.
- **Core has no I/O.** Models and interfaces only, so it stays stable and testable while
  collectors churn.
- **Append-only store.** History and drift detection depend on never overwriting; current
  state is a view over the latest records.
- **SQLite to start.** Boring and embeddable. The `IStore` abstraction leaves room to
  swap in a dedicated time-series DB later if volume demands it.
- **Normalize at the edge.** Collectors convert pillar-specific reality into the two
  shared record types so the engine, dashboard, and docs renderer never special-case a
  pillar.

---

## 4. Status & next steps

**Status: Phase 0 — walking skeleton runs end-to-end.** Done:

- ✅ Solution + projects scaffolded (`global.json` pins SDK 9.0.314); references wired.
- ✅ **Core** — `HealthRecord`, `InventoryRecord`, `HealthStatus`, `ICollector`, `IStore`,
  `IAlertChannel`.
- ✅ **Storage** — append-only SQLite store (current-state + history + detail JSON), unit-tested.
- ✅ **Engine** — hosted scheduler runs each collector on its interval, persists results,
  isolates failures.
- ✅ **HostNet collector** — ICMP latency + TLS cert expiry (verified live).
- ✅ **DNS collector** — resolves records (per-server or system resolver), verifies expected
  answers, latency, NXDOMAIN/SERVFAIL (DnsClient). Verified live against the four
  `compass-tamu.tamu.edu` AD/DNS servers.
- ✅ **Active Directory collector** — domain/forest discovery, DC + FSMO + site inventory,
  per-DC LDAP(S) bind latency, replication-neighbor health (integrated auth). Verified live
  against `compass-tamu.tamu.edu` (4 DCs, all bind ~8ms, replication in sync).
- ✅ **Pillar-generic dashboard** — tiles + check tables render per infra pillar present, so
  new pillars appear with no renderer changes. Dark-mode toggle (cookie-persisted).
- ✅ **Jira integration** — REST client + JQL for all six widgets across IMS/CHG/CSI;
  validated against the live instance. "Unanswered" uses agent-comment detection
  (`accountType == "atlassian"`), ignoring automation/customer replies.
- ✅ **Alerting** — `TeamsAlertChannel` (webhook) + `EmailAlertChannel` (SMTP), driven by
  the engine's `AlertEvaluator` (fires on transition into Critical, seeded at startup);
  end-to-end dispatch verified. No-ops until configured.
- ✅ **Web** — ASP.NET Core host that runs the engine in-process and renders the dashboard
  (tiles + Jira widgets + inline SVG trend chart + timeclock alert); `UseWindowsService()`.

Next steps, in order:

1. **Docs + Service projects** — render inventory/history to Markdown/PDF; add the
   split-deployment `Service` host.
2. **Phase 1 pillars** — read-only health for DNS, AD, Hyper-V, Veeam (per `CONCEPT.md` §8).
3. **Deploy** — publish and install as a Windows service per [`DEPLOYMENT.md`](DEPLOYMENT.md);
   supply Jira token + alert webhook/SMTP as secrets.

See [`CONCEPT.md`](CONCEPT.md) §8 for the full phased rollout.
