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
| `InfraWatch.Service` | Composition root. Hosts the engine as a Windows service (`Microsoft.Extensions.Hosting`). Wires collectors → store → engine → alerting. | all |
| `InfraWatch.Web` | ASP.NET Core dashboard: roll-up tiles, drill-down, report rendering. Reads from the store. | Core, Storage, Docs |
| `Collectors/InfraWatch.Collectors.*` | One project per pillar. Each implements `ICollector` and produces normalized health + inventory. | Core |
| `tests/InfraWatch.Tests` | Unit/integration tests. | (project under test) |

### Collector projects

| Project | Pillar | Likely access method |
|---|---|---|
| `InfraWatch.Collectors.HostNet` | Host / Net | ICMP, raw TCP, `SslStream` (TLS cert), `HttpClient` |
| `InfraWatch.Collectors.Dns` | DNS | `System.Net.Dns` + targeted resolver queries |
| `InfraWatch.Collectors.Dhcp` | DHCP | `DhcpServer` PowerShell module / service + lease query |
| `InfraWatch.Collectors.Smb` | SMB / File | SMB client connect, share enumeration |
| `InfraWatch.Collectors.ActiveDirectory` | AD | `System.DirectoryServices`, `ActiveDirectory` PS module |
| `InfraWatch.Collectors.HyperV` | Hyper-V | CIM/WMI, `Hyper-V` + `FailoverClusters` PS modules |
| `InfraWatch.Collectors.Veeam` | Veeam B&R | B&R REST API / PowerShell module / config DB |

Why separate projects per collector: each pillar pulls in different, sometimes heavy
dependencies (PowerShell SDK, directory services, vendor APIs). Isolating them keeps the
core dependency-free, lets pillars be built/tested in isolation, and makes it trivial to
disable a pillar we don't have access to yet.

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

**Status: Phase 0 — skeleton.** Folder structure + docs are in place. No `.csproj`
projects or functional code yet.

Next steps, in order:

1. **Scaffold the solution** — `dotnet new sln`; create each project above as a
   `classlib` (or `worker` for Service, `web` for Web); wire project references per the
   dependency table.
2. **Define Core** — `HealthRecord`, `InventoryRecord`, severity, `ICollector`,
   `IStore`, `IAlertChannel`.
3. **Walking skeleton** — implement `InfraWatch.Collectors.HostNet` (ping + TLS expiry) →
   SQLite store → engine schedule → one dashboard tile. Prove the architecture
   end-to-end before widening to other pillars.
4. **Phase 1** — read-only health for DNS, AD, Hyper-V, Veeam + dashboard + alerting
   (per `CONCEPT.md` §8).

See [`CONCEPT.md`](CONCEPT.md) §8 for the full phased rollout.
