# InfraWatch — Deploying to a Windows Server

This package runs InfraWatch as an **auto-start Windows service** on one server, serving the
dashboard on port **8080**. It is self-contained — the target server does **not** need .NET
installed.

## Contents

- `InfraWatch.Web.exe` + supporting files — the app (self-contained .NET 9 / win-x64)
- `appsettings.json` — non-secret configuration (pillars, hosts, thresholds)
- `appsettings.Local.json` — **your credentials + the listen URL** (keep this file private)
- `Install-InfraWatch.ps1` / `Uninstall-InfraWatch.ps1`
- `data\` (created on first run) — the SQLite store; `docs\` — the exported report

## Prerequisites

- A Windows Server (domain-joined to `compass-tamu.tamu.edu`).
- A **domain service account** for the service to run as — it needs rights to query the
  monitored systems with integrated auth (AD/LDAP, Hyper-V & Print WMI, DHCP via RSAT, SMB).
  A domain admin or a dedicated account with read/WMI rights on those hosts works. Plain
  `LocalSystem` will **not** have cross-server rights, so most pillars would fail.
- The RSAT **DhcpServer** PowerShell module installed on the server (for the DHCP pillar).

## Install

1. Copy this folder (or the zip) to the server, e.g. `C:\InfraWatch`. Avoid `C:\Program Files`
   so the service account can write the `data\` and `docs\` folders.
2. Open **PowerShell as Administrator**, `cd C:\InfraWatch`.
3. Run:

   ```powershell
   .\Install-InfraWatch.ps1
   ```

   You'll be prompted for the domain service account (e.g. `COMPASS-TAMU\svc-infrawatch`)
   and its password. The password is **validated against the domain immediately** — if it's
   wrong, you're told and asked to re-enter (it won't create a broken service). The script then
   registers the service (auto-start + auto-restart), grants the account write access to
   `data\`/`docs\`, opens TCP 8080 in the firewall, starts it, and **confirms the dashboard
   responds** before finishing.
4. Browse to `http://<server-name>:8080/`.

### Down alerts (watchdog)

The installer also registers a **scheduled task** ("InfraWatch Watchdog") that runs every 5
minutes and emails if the service or dashboard goes down (and once when it recovers). It only
mails on a state change, so you won't be spammed. Recipients come from `-AlertEmail`, else the
`Alerting:Email:To` list in `appsettings.Local.json`, else it prompts (blank skips it). Mail is
sent via the same relay the app uses. To add/replace it later, re-run install with
`-AlertEmail you@sscserv.com`.

> The service is also set to **auto-restart on crash** by Windows itself; the watchdog covers
> the case where it stays down and a human needs to know.

## Verify

- `Get-Service InfraWatch` shows **Running**.
- The dashboard loads and pillars populate within a few minutes (collectors run on their
  intervals; Veeam/Print can take a cycle or two).
- If a pillar is red/unknown, it's almost always the **service account** lacking rights on that
  host — confirm the account can run the equivalent query manually.

## Credentials & config

- Secrets live in `appsettings.Local.json` (Jira, Confluence, Veeam, Teams webhook, email).
  This package was built with them **pre-filled**. To rotate one, edit the file and restart:
  `Restart-Service InfraWatch`.
- Non-secret settings (which hosts/sites to monitor, thresholds) are in `appsettings.json`.
- The listen URL is `appsettings.Local.json` → `Urls` (`http://*:8080`). Change the port there
  and re-run install with `-Port` to match the firewall rule.

## Updating to a new build

1. `Stop-Service InfraWatch`
2. Replace the app files (keep your `appsettings.Local.json` and the `data\` folder).
3. `Start-Service InfraWatch`

## Uninstall

```powershell
.\Uninstall-InfraWatch.ps1 -RemoveFirewallRule
```

Removes the service (and optionally the firewall rule); leaves the files and `data\` in place.

## Security note

`appsettings.Local.json` contains live credentials in plaintext. Restrict the install folder's
NTFS permissions to admins + the service account, and don't copy the package to shared
locations. Rotate any secret that may have been over-shared.
