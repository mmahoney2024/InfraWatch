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

## Active offer / lease test (opt-in)

In addition to the non-intrusive scope query, the collector can send a **real DHCP
DISCOVER** and confirm the server **OFFERs** an address — true end-to-end validation that a
client can lease. Optionally it completes the lease (REQUEST → ACK) and then **RELEASEs** it
so nothing is left consumed. It uses a fixed locally-administered probe MAC (`02:49:57:…`)
so the server hands out the same address each time.

```jsonc
"OfferTest": true,            // send DISCOVER, expect OFFER
"LeaseTest": false,           // also REQUEST/ACK then RELEASE (more intrusive)
"RelayAddress": "165.91.186.17", // a LOCAL IP on a subnet the server has a scope for
"OfferTimeoutMs": 4000
```

**Off by default — it is intrusive** and needs sign-off (`CONCEPT.md` §6.1). Notes:
- The probe acts as a **relay (giaddr)**: it puts `RelayAddress` in the DISCOVER so the
  server unicasts the OFFER back. `RelayAddress` must be a local IP on a subnet the server
  has a scope for. Binding UDP/67 may require the service to run with sufficient privilege.
- **Failover pairs:** in a load-balanced failover relationship only the node responsible for
  a given client MAC answers; the partner stays silent **by design**. So a per-server offer
  test will time out on the standby node — that's not a fault. Point it at the primary, or
  treat the pair together. (Verified live: fsdhcp03 OFFERed `165.91.186.55`; fs-dhcp05, its
  failover partner, correctly stayed silent for the probe MAC.)
