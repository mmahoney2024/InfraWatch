# InfraWatch — Deployment

How to install InfraWatch on a Windows server so the dashboard is reachable as a webpage
on the internal network.

> **Status:** target design. No deployable build exists yet (Phase 0).

---

## 1. Topology

InfraWatch is designed to run on **one Windows server** as a single process that does both
jobs:

- **Background engine** — runs the collectors on a schedule and writes to the store.
- **Web dashboard** — serves the roll-up/drill-down UI and the Jira widgets.

```
┌─────────────────────────── Windows Server (e.g. INFRAWATCH01) ───────────────────────────┐
│                                                                                            │
│   IIS (optional reverse proxy, :443)  ──►  InfraWatch.Web (Kestrel, :8080)                 │
│        TLS, Windows auth                      ├── ASP.NET Core dashboard + API             │
│                                               └── Engine + Collectors (IHostedService)     │
│                                                          │                                 │
│                                                   SQLite store (data\infrawatch.db)        │
│                                               ┌──────────┴───────────┐                     │
│                                       probes LAN infra        polls sscserv.atlassian.net  │
└────────────────────────────────────────────────────────────────────────────────────────────┘
                       │                                              │
        DNS/DHCP/SMB/AD/Hyper-V/Veeam/hosts                   Jira Cloud (REST API)
```

The dashboard host (`InfraWatch.Web`) runs the engine as a hosted background service, so a
single install is "accessible via a webpage" with nothing else to deploy. (The separate
`InfraWatch.Service` project remains available for a future split deployment where the
collector host and the web host are different machines sharing a database.)

## 2. Two ways to make it web-accessible

**A. Behind IIS (recommended for an internal tool).** Use the
[ASP.NET Core Module](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/) so IIS
reverse-proxies to Kestrel. You get TLS termination, port 80/443, and easy **Windows
Integrated Authentication** (so only domain users see the dashboard). Boring and standard.

**B. Standalone Windows Service.** Publish self-contained, install with `sc.exe` /
`New-Service`, Kestrel listens directly on the LAN (e.g. `http://infrawatch01:8080`). Add
a reverse proxy later if you need TLS/auth. Fewer moving parts, but you own TLS and auth.

Either way the app runs unattended as a service and restarts with the server.

## 3. Build & publish

```powershell
# from the repo root, once the solution exists
dotnet publish src/InfraWatch.Web -c Release -r win-x64 --self-contained `
    -o C:\InfraWatch\app
```

## 4. Install as a Windows service

```powershell
New-Service -Name InfraWatch -BinaryPathName "C:\InfraWatch\app\InfraWatch.Web.exe" `
    -DisplayName "InfraWatch" -StartupType Automatic `
    -Credential (Get-Credential)          # the least-privilege service account
Start-Service InfraWatch
```

The app uses `services.AddWindowsService()` so it runs correctly under the Windows Service
Control Manager. Logs go to the Windows Event Log and `C:\InfraWatch\logs`.

## 5. Service account & secrets

- Run under a **dedicated least-privilege domain service account** (e.g.
  `SSCSERV\svc-infrawatch`), not a personal admin account. See `CONCEPT.md` §6.2.
- The account needs only the **read** rights each enabled collector documents (see each
  collector's `README.md`).
- **Secrets** (Jira API token, SMB/AD/Veeam creds) never live in `appsettings.json` in
  source control. Options, simplest first:
  - Windows **DPAPI**-protected config on the server, or
  - Environment variables on the service, or
  - **Windows Credential Manager** / a secrets manager.
  See `docs/JIRA.md` for the Jira token specifically.

## 6. Networking checklist

- Open the dashboard port (443 via IIS, or 8080 direct) to the admin subnet only.
- Outbound HTTPS to `sscserv.atlassian.net` for the Jira integration.
- Outbound access from the server to each monitored pillar (LDAP(S), SMB, WMI/CIM, Veeam
  REST, DNS, DHCP).
- Prefer TLS for the dashboard; restrict access to IT staff (Windows auth or a reverse
  proxy ACL).

## 7. Updating

Re-publish to a new folder, stop the service, swap folders, start the service:

```powershell
Stop-Service InfraWatch
# swap C:\InfraWatch\app with the new publish output
Start-Service InfraWatch
```
