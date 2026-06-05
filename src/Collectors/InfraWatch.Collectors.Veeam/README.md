# InfraWatch.Collectors.Veeam

**Pillar:** Veeam Backup &amp; Replication. **Implemented; dormant until configured.**

We don't use Veeam ONE — InfraWatch reads posture directly from the **B&amp;R REST API**.

**Health (per B&amp;R server target):**
- **job `<name>`** — last result (Success/Warning/Failed) **and RPO**: Critical if no run
  within `RpoHours`. Disabled jobs are informational.
- **backup `<machine>`** — newest restore-point age per protected machine. **Catches VM
  backups that aren't exposed as REST "jobs"** (older console jobs). Stale (no point within
  `BackupRpoHours`) = Warning by default, or Critical if `BackupRpoCritical`. Grouped per
  machine across all its backup sets.
- **jobs** / **backups** — roll-ups
- **repo `<name>`** — repository free space % (Warning below `RepoFreeWarnPct`)
- **connection** — Critical if auth/connect fails

> Why: this B&amp;R server's VM (Hyper-V) backups don't appear in `/api/v1/jobs/states`
> (only file-share jobs do, and `/api/v1/jobs` is empty). They're read from
> `/api/v1/restorePoints` instead — that's where the per-machine last-backup time lives.

**Documentation (inventory):** `job` records (type, last result, last run) and `repository`
records (capacity / free / used GB).

**Access method:** REST API on `https://<server>:9419`. OAuth2 **password grant**
(`/api/oauth2/token`) with the required **`x-api-version`** header (version depends on the
B&amp;R build). B&amp;R's default cert is self-signed, so `IgnoreCertErrors` defaults true.

**Privileges:** a least-privilege Veeam account with **read** access (a Veeam *Restore
Operator* / read-only role is plenty). Password is a **secret** — inject via user-secrets/env.

## Config (`Veeam` section)

```jsonc
"Veeam": {
  "Interval": "00:10:00",
  "BaseUrl": "https://veeam.compass-tamu.tamu.edu:9419",
  "Username": "COMPASS-TAMU\\svc-infrawatch",   // a secret in practice
  "Password": "<secret>",                        // user-secret, never in source
  "ApiVersion": "1.1-rev2",   // v12.0=1.1-rev0, v12.1=1.1-rev2, v12.2=1.2-rev0, v12.3=1.2-rev1
  "RpoHours": 24,
  "RepoFreeWarnPct": 10,
  "IgnoreCertErrors": true     // B&R default cert is self-signed
}
```

> Status: collector + REST client written and compile; dormant (empty `BaseUrl`) until the
> B&amp;R server, version, and a read-only credential are provided, then verified live.
