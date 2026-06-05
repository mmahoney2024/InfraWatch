# InfraWatch.Collectors.HyperV

**Pillar:** Hyper-V (standalone hosts). **Implemented (read-only).**

Per configured host, over WMI/CIM (`root\virtualization\v2` + `root\cimv2`), using the
service account's own Windows credentials — **no credentials in config**.

**Health checks (per host):**
- **host** — reachability (WMI connect)
- **cpu** — average CPU load %
- **memory** — free host RAM %
- **vm-states** — running / off / other counts
- **checkpoints** — total checkpoints (warns on sprawl)

**Documentation (inventory):**
- `host` — VM count, running count
- `vm` — each VM (host, state, health)

**Access method:** `System.Management` (WMI). Remote hosts are reached via DCOM/RPC
(port 135 + dynamic RPC) — the host must be reachable and the account must have rights to
query Hyper-V WMI (typically local admin on the host). Unreachable hosts degrade to a clear
`Critical` "unreachable" status.

**Verified live** against `fs-aio.compass-tamu.tamu.edu` — see [[compass-tamu-environment]].

## Config (`HyperV` section)

```jsonc
"HyperV": {
  "Interval": "00:02:00",
  "Hosts": [ "fs-aio.compass-tamu.tamu.edu" ],  // empty = the local machine
  "CpuWarnPct": 85,
  "MemFreeWarnPct": 10,
  "CheckpointWarn": 10
}
```

> Follow-up: per-VM vCPU/RAM allocation, replica health, and failover-cluster/CSV checks
> (the latter via `FailoverClusters`).
