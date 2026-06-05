# InfraWatch.Collectors.Veeam

**Pillar:** Veeam Backup & Replication. **No Veeam ONE** — we build this monitoring
ourselves.

**Health checks:** per-job last result (success/warning/fail), **RPO alerts** (no
successful run in N hours), repository free-space trending, failed/slow sessions.

**Documentation:** backup posture — what's protected, when it last succeeded, where it
lands, headroom.

**Access method:** B&R **REST API** *or* the Veeam PowerShell module *or* the config DB.
Choice depends on B&R version/edition — confirm before building (`CONCEPT.md` §9).

**Privileges required:** a least-privilege Veeam account with read access to jobs,
sessions, and repositories.
