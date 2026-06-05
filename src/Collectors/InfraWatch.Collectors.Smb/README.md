# InfraWatch.Collectors.Smb

**Pillar:** SMB / File. **Implemented (read-only by default).**

Two jobs, using the service account's own Windows credentials:

1. **Access-check** each configured share — connect + authenticate + list (latency).
   Optional **canary read/write** (opt-in, off by default; writes a temp file then deletes it).
2. **Enumerate** a host's shares (WMI `Win32_Share`) for inventory.

**Health:**
- **access** (per share) — Healthy/latency, Warning (slow), Critical (denied / not found / unreachable)
- **canary-write** (per share, if enabled) — read/write OK
- **shares** (per enumerated host) — count published

**Documentation (inventory):** `share` records — checked shares (path, latency) and each
host's published shares (name, path, type, description).

**Access method:** `System.IO` over UNC (integrated auth) for access-checks;
`System.Management` (WMI) for share enumeration. **Verified live** against
`fs-aio.compass-tamu.tamu.edu` (5 shares enumerated).

## Config (`Smb` section)

```jsonc
"Smb": {
  "Interval": "00:05:00",
  "Shares": [ "\\\\fs-aio\\Data", "\\\\fs-aio\\Profiles" ],  // UNC paths to access-check
  "EnumerateHosts": [ "fs-aio.compass-tamu.tamu.edu" ],       // hosts to inventory shares
  "ListWarnMs": 2000,
  "CanaryWrite": false                                        // opt-in write test
}
```
