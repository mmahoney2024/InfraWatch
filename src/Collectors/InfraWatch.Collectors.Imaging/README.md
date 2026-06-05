# InfraWatch.Collectors.Imaging

**Pillar:** WDS / MDT imaging. **Implemented; not yet verified live** (server TBD — dormant
until `Imaging:Server` is set).

Three read-only layers (the TFTP download is a passive read, like a PXE client):

1. **TFTP boot-file download** — downloads the configured PXE boot file(s) over TFTP/UDP-69
   and reports success / size / latency ("what file it downloads"). Caps the download so a
   huge `boot.wim` isn't pulled in full.
2. **Deployment-share files** — confirms key files (boot WIM, `Bootstrap.ini`,
   `CustomSettings.ini`, OS WIMs…) exist, are readable, and (optionally) aren't stale.
3. **WDS service** — confirms the `WDSServer` service is running (remote `ServiceController`).

Uses the service account's own Windows credentials. **Access method:** UDP/69 (TFTP),
`System.IO` over UNC (share), `System.ServiceProcess.ServiceController` (service).

## Config (`Imaging` section)

```jsonc
"Imaging": {
  "Interval": "00:10:00",
  "Server": "fs-mdt.compass-tamu.tamu.edu",       // empty = pillar dormant
  "TftpFiles": [ "boot\\x64\\wdsnbp.com" ],         // PXE boot program(s)
  "DeploymentShare": "\\\\fs-mdt\\DeploymentShare$",
  "ShareFiles": [ "Boot\\LiteTouchPE_x64.wim", "Control\\Bootstrap.ini", "Control\\CustomSettings.ini" ],
  "StaleDays": 0,                                   // >0 = warn if a file is older than N days
  "CheckService": true,
  "ServiceName": "WDSServer"
}
```

> Status: collector + a minimal read-only TFTP client are written and compile; awaiting the
> actual WDS/MDT server name to verify end-to-end (and to add an optional on-demand
> "Run TFTP test" button, like DHCP's).
