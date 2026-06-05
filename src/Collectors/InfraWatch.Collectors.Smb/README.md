# InfraWatch.Collectors.Smb

**Pillar:** SMB / File.

**Health checks:** connect, authenticate, list a share, optional canary read/write
(**write is opt-in and scoped** — `CONCEPT.md` §6.5).

**Documentation:** share inventory per host, reachability.

**Access method:** SMB client connect + share enumeration.

**Privileges required:** a least-privilege account that can connect and list the target
shares. Canary write needs a dedicated, clearly-scoped path and is off by default.
