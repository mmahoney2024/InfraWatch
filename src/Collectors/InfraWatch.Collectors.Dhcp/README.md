# InfraWatch.Collectors.Dhcp

**Pillar:** DHCP. **Implemented (read-only).**

Per configured DHCP server, queries the **`DhcpServer` PowerShell module** (RSAT) for scope
statistics and reports each scope's pool pressure. Uses the service account's own Windows
credentials.

**Health:**
- **service** (per server) — DHCP responding + scope count (Critical if unreachable)
- **scope `<id>`** (per active scope) — % of pool in use (Warning ≥ 90%, Critical ≥ 98%);
  inactive scopes are reported but not alerted

**Documentation (inventory):** `scope` records — id, name, state, free / in-use / reserved
counts, % in use.

**Access method:** shells out to `powershell.exe` with the `DhcpServer` module
(`Get-DhcpServerv4Scope` + `Get-DhcpServerv4ScopeStatistics`) and parses JSON. The host
running InfraWatch needs **RSAT-DHCP** installed; the account needs DHCP read access.
Server reachability uses DCOM/RPC (the same as the module). **Verified live** against
`fs-dhcp05` and `fsdhcp03` (a failover pair).

## Config (`Dhcp` section)

```jsonc
"Dhcp": {
  "Interval": "00:05:00",
  "Servers": [ "fs-dhcp05.compass-tamu.tamu.edu", "fsdhcp03.compass-tamu.tamu.edu" ],
  "PercentInUseWarn": 90,
  "PercentInUseCrit": 98
}
```

> Note: an active offer test (crafted DHCP DISCOVER) is intentionally **not** done — it can
> resemble recon and needs explicit authorization (`CONCEPT.md` §6.1). This pillar uses the
> non-intrusive server/scope query instead.
