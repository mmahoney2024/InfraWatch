# InfraWatch — Concept & Proposal

> Unified monitoring **and** self-maintaining documentation for core infrastructure,
> rolled up into a single drill-down dashboard.

*Working name — rename freely. Draft for internal discussion.*

---

## 1. The problem we're solving

Two gaps, both currently unaddressed:

1. **Monitoring** — we don't have a reliable, at-a-glance way to know whether core
   services are healthy *right now*, or to be alerted *before* users notice.
2. **Documentation** — what we have is sparse, hand-maintained, and goes stale the
   moment it's written.

The core idea: **let the monitoring tool generate the documentation as a byproduct.**
Because the tool is already talking to everything to check health, it can also record
what it finds. Documentation becomes a *rendering of measured reality* instead of a
manual chore — so it's always true and never stale.

---

## 2. What gets monitored (the pillars)

| Pillar | Health checks | Auto-generated documentation |
|---|---|---|
| **DNS** | Resolve known records, verify answers, response time, SERVFAIL/wrong-answer detection | Zones/records visible, forwarders, authoritative servers |
| **DHCP** | Active offer test *or* server-service + lease-pool monitoring | Scopes, ranges, lease counts, reservations, exclusions |
| **SMB / File** | Connect, auth, list share, optional canary read/write | Share inventory per host, reachability |
| **Active Directory** | DC reachability, replication health, LDAP(S) bind + latency, FSMO reachability, SYSVOL/Netlogon, time sync, lockout/replication events | DC list, FSMO roles, sites/subnets, OU + GPO inventory, privileged-group membership (audit) |
| **Hyper-V** | Host CPU/RAM/storage, VM states, replica health, checkpoint sprawl, cluster node/quorum, CSV free space | VM inventory, VM-to-host mapping, vCPU/RAM allocation, virtual switch layout |
| **Veeam Backups** *(no Veeam ONE — we build this ourselves via B&R REST API / PowerShell / config DB)* | Per-job last result (success/warning/fail), **RPO alerts** (no successful run in N hrs), repository free space trending, failed/slow sessions | Backup posture report: what's protected, when it last succeeded, where it lands, headroom |
| **General host/net** | ICMP latency/jitter, TCP port reachability, **TLS cert expiry**, HTTP status/latency | Open ports, certs + expiry, hostnames, OS hints |

---

## 3. Documentation, in three flavors

1. **Inventory** ("what exists") — a living map of the network, measured not typed.
2. **Change history** ("what changed and when") — drift log: new shares appear, DHCP
   pool drains, DNS records change, certs approach expiry, checkpoints pile up.
   *Half of "why did it break?" is "what changed?" — and almost nobody records it.*
3. **Generated reports** ("the writeup") — scheduled or on-demand Markdown/PDF: a
   "state of the network" report, backup-posture report, and incident timelines.

---

## 4. The dashboard

- **Top level** — wall of status tiles grouped by pillar (Network / Files / AD /
  Hyper-V / Backups / Hosts), each green/yellow/red with a one-glance summary.
- **Drill in** → individual checks behind a tile, history/latency graphs,
  last-state-change, relevant documentation slice.
- **Drill again** → raw measured detail + change log for that specific item.

"Roll up, then drill down" — one pane of glass across everything.

---

## 5. Approach — built from the ground up, in-house

**Decision: fully custom. No third-party monitoring/doc products.**

Off-the-shelf tools exist for *pieces* of this (infra monitoring, backup monitoring,
IPAM), but none gives us "one pane of glass across network + files + AD + Hyper-V +
backups + living docs," and we want a system that is entirely ours — tailored to our
environment, no licensing, no vendor lock-in, fully under our control.

**What we gain:**
- One coherent system designed around *our* network, not generic assumptions
- Exactly the checks, dashboard, and documentation behavior we want — nothing we don't
- No license costs, no per-node/per-sensor pricing, no vendor roadmap dictating ours
- Free to integrate AD, Hyper-V, and Veeam as first-class peers from day one

**What we take on (eyes open):**
- We own everything the mature tools solved for free: retry/backoff, flap detection,
  time-series storage, alert deduplication, dashboard plumbing, scaling
- Long-term maintenance and the bus-factor/ownership question (see §6)
- A longer road to feature parity with battle-tested products

The plan below is structured so we get value early (read-only health + dashboard) and
build outward, keeping the codebase boring, documented, and maintainable so "custom"
never means "fragile."

---

## 6. Things to settle *before* building (work environment)

These are what turn a "cool project" into something defensible to management and security:

1. **Authorization.** Actively probing DNS/DHCP/SMB/AD and pulling Veeam/Hyper-V data
   on a production network must be sanctioned. Active checks (especially crafted DHCP
   packets or share enumeration) can resemble recon — security/network owners need a
   heads-up and sign-off.
2. **Credential handling.** This app concentrates privileged access (Veeam API, SMB
   auth, AD read, Hyper-V hosts). Requires **least-privilege service accounts** and a
   proper **secrets manager** — never plaintext in a config file.
3. **Ownership / bus factor.** A custom tool is great until it breaks while you're out.
   Decide who owns, secures, and can hand it off. Favor boring, documented, standard tech.
4. **Access depth = documentation depth.** Richer auto-discovery (DNS zone transfers,
   full DHCP scope detail, AD/GPO inventory) needs more access. The access we grant is
   the dial that controls how deep the docs go.
5. **Read-only first.** Default posture is observe-only. Any write action (canary files,
   etc.) is opt-in and clearly scoped.

---

## 7. Suggested architecture (conceptual)

- **Stack**: **C# / .NET** (decided). Best fit for a long-lived Windows service with
  native WMI/CIM access; can call PowerShell modules in-process where needed
  (`ActiveDirectory`, `Hyper-V`, `FailoverClusters`, `DhcpServer`, Veeam).
- **Runtime**: Windows host/VM (most pillars are Windows-native → CIM/WMI + PowerShell).
  Runs as a Windows service.
- **Collectors**: one module per pillar (DNS, DHCP, SMB, AD, Hyper-V, Veeam, host/net),
  each producing a normalized health + inventory record.
- **Store**: time-series/history for trends + change detection (start simple, e.g. SQLite).
- **Engine**: scheduler, baseline/drift detection, alerting rules.
- **Alerting**: email / Teams / ntfy / Discord — whatever's standard at work.
- **Web dashboard**: roll-up tiles + drill-down + report rendering.
- **Docs renderer**: exports Markdown/PDF; optionally pushes to an internal wiki.

---

## 8. Suggested phased rollout

- **Phase 0** — Authorization, service accounts, choose language/stack, pick alert channel, stand up the project skeleton (collector → store → engine → dashboard).
- **Phase 1** — Read-only health checks for DNS, AD, Hyper-V, Veeam + basic dashboard + alerting.
- **Phase 2** — Add DHCP, SMB, TLS/host checks; introduce change-history/drift logging.
- **Phase 3** — Auto-generated inventory + reports; wiki integration; richer drill-down.
- **Phase 4** — Hardening, handoff docs, secrets management, redundancy.

---

## 9. Open questions to bring to work

- Veeam: **no Veeam ONE license** — we monitor B&R ourselves via its REST API,
  PowerShell module, or config DB. Confirm B&R version/edition to pick the access method.
- Hyper-V: standalone hosts or failover cluster(s)? How many?
- AD: how many DCs / domains / sites?
- Preferred alert channel (Teams? email? other)?
- Existing monitoring we should integrate with rather than replace?
- Who would own this long-term?

---

*Origin: this document was drafted from an exploratory conversation about building a
combined infrastructure monitoring + documentation tool. It is a starting point for
discussion, not a committed design.*
