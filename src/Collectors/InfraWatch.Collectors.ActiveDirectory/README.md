# InfraWatch.Collectors.ActiveDirectory

**Pillar:** Active Directory.

**Health checks:** DC reachability, replication health, LDAP(S) bind + latency, FSMO
reachability, SYSVOL/Netlogon, time sync, lockout/replication events.

**Documentation:** DC list, FSMO roles, sites/subnets, OU + GPO inventory,
privileged-group membership (audit).

**Access method:** `System.DirectoryServices` / `System.DirectoryServices.Protocols`,
plus the `ActiveDirectory` PowerShell module where convenient.

**Privileges required:** an authenticated domain account with **read** across the
directory. Privileged-group auditing reads sensitive membership — least-privilege and
explicit sign-off (`CONCEPT.md` §6.1–6.4).
