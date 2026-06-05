# InfraWatch

> Unified monitoring **and** self-maintaining documentation for core infrastructure,
> rolled up into a single drill-down dashboard.

InfraWatch watches the core pillars of a Windows-centric network — DNS, DHCP, SMB/File,
Active Directory, Hyper-V, Veeam backups, and general host/network health — and turns
what it measures into **living documentation**. Because the tool is already talking to
everything to check health, it records what it finds: documentation becomes a *rendering
of measured reality* instead of a manual chore, so it's always true and never stale.

See [`docs/CONCEPT.md`](docs/CONCEPT.md) for the full proposal,
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for how the code is organized,
[`docs/JIRA.md`](docs/JIRA.md) for the Jira dashboard integration, and
[`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) for installing it on a Windows server.

> **Status:** Phase 0 — project skeleton. No functional collectors yet.

---

## What it does (target)

- **Monitor** — at-a-glance health of core services, with alerts *before* users notice.
- **Document** — auto-generated inventory ("what exists"), change history ("what changed
  and when"), and on-demand reports ("the writeup").
- **Jira at a glance** — a high-level view of the IT service desk next to the infra tiles:
  pressing tickets, day-old unanswered tickets, open-vs-closed/month graphs, and a
  **timeclock alert**. See [`docs/JIRA.md`](docs/JIRA.md).
- **One pane of glass** — roll-up status tiles per pillar, drill down to individual
  checks, drill again to raw measured detail + change log.

## Deployment

Runs on **one Windows server** as a single service: `InfraWatch.Web` serves the dashboard
*and* hosts the collector engine in the background, so the install is reachable as a
webpage on the internal network (direct Kestrel, or behind IIS for TLS + Windows auth).
Full steps in [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md).

## Pillars

| Pillar | Health checks | Auto-generated docs |
|---|---|---|
| **Host / Net** | ICMP latency/jitter, TCP port reachability, TLS cert expiry, HTTP status/latency | Open ports, certs + expiry, hostnames, OS hints |
| **DNS** | Resolve known records, verify answers, response time, SERVFAIL detection | Zones/records, forwarders, authoritative servers |
| **DHCP** | Offer test or server-service + lease-pool monitoring | Scopes, ranges, lease counts, reservations, exclusions |
| **SMB / File** | Connect, auth, list share, optional canary read/write | Share inventory per host, reachability |
| **Active Directory** | DC reachability, replication, LDAP(S) bind + latency, FSMO, SYSVOL/Netlogon, time sync | DC list, FSMO roles, sites/subnets, OU + GPO inventory |
| **Hyper-V** | Host CPU/RAM/storage, VM states, replica health, checkpoint sprawl, cluster quorum, CSV free space | VM inventory, VM-to-host mapping, allocations, vSwitch layout |
| **Veeam** | Per-job last result, RPO alerts, repository free-space trending, failed/slow sessions | Backup posture: what's protected, last success, where it lands, headroom |

## Stack

- **C# / .NET** — best fit for a long-lived Windows service with native WMI/CIM access;
  can call PowerShell modules in-process (`ActiveDirectory`, `Hyper-V`,
  `FailoverClusters`, `DhcpServer`, Veeam) where needed.
- **Runtime** — Windows host/VM, runs as a Windows service.
- **Store** — SQLite to start (time-series/history for trends + change detection).
- **Dashboard** — ASP.NET Core web app: roll-up tiles, drill-down, report rendering.

## Repository layout

```
InfraWatch/
├── docs/                          Concept, architecture, decisions
├── src/
│   ├── InfraWatch.Core/           Domain models + collector/store abstractions
│   ├── InfraWatch.Storage/        SQLite persistence, history, change/drift log
│   ├── InfraWatch.Engine/         Scheduler, drift detection, alert rules
│   ├── InfraWatch.Alerting/       Alert channels (email / Teams / ntfy / Discord)
│   ├── InfraWatch.Docs/           Docs renderer (Markdown / PDF)
│   ├── InfraWatch.Service/        Windows service host — split-deploy composition root
│   ├── InfraWatch.Web/            ASP.NET Core dashboard + engine host (deployable unit)
│   ├── Collectors/                One project per infra pillar
│   │   ├── InfraWatch.Collectors.HostNet/
│   │   ├── InfraWatch.Collectors.Dns/
│   │   ├── InfraWatch.Collectors.Dhcp/
│   │   ├── InfraWatch.Collectors.Smb/
│   │   ├── InfraWatch.Collectors.ActiveDirectory/
│   │   ├── InfraWatch.Collectors.HyperV/
│   │   └── InfraWatch.Collectors.Veeam/
│   └── Integrations/              External SaaS data sources
│       └── InfraWatch.Integrations.Jira/   Jira service-desk widgets + timeclock alert
└── tests/
    └── InfraWatch.Tests/
```

Each folder currently holds a `README.md` describing its responsibility. The `.csproj`
projects and `InfraWatch.sln` are added in the next step (see
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md#status--next-steps)).

## Getting started (developers)

Prerequisites:

- [.NET SDK](https://dotnet.microsoft.com/download) 9.0 or later
- Windows (most pillars are Windows-native: CIM/WMI + PowerShell)

```powershell
# once projects exist:
dotnet build
dotnet test
```

## Guardrails (read before pointing this at production)

InfraWatch concentrates privileged access and actively probes infrastructure. Before
running against a real network, settle the items in [`docs/CONCEPT.md`](docs/CONCEPT.md)
§6 — authorization, least-privilege service accounts, a real secrets manager, ownership,
and a **read-only-first** posture. Any write action (canary files, etc.) is opt-in and
clearly scoped.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md).
