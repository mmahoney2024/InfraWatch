# InfraWatch.Collectors.ActiveDirectory

**Pillar:** Active Directory. **Implemented (read-only).**

Discovers the domain/forest and reports health + inventory. Binds with the **service
account's own Windows credentials** (Negotiate/Kerberos) — no credentials in config.
**Must run on a domain-joined host.**

**Health checks:**
- **discovery** — domain reachable, DC count
- **ldap-bind** (per DC) — LDAP(S) bind + latency
- **replication** (per DC) — inbound replication neighbors; Critical if any failing
- **FSMO roles** — all 5 role holders identified

**Documentation (inventory):**
- `dc` — each domain controller (site, IP, OS, roles)
- `fsmo` — the 5 role holders (PDC, RID, Infrastructure, Schema, Domain Naming)
- `site` — AD sites (server count, subnets)

**Access method:** `System.DirectoryServices.ActiveDirectory` (discovery, FSMO, replication)
+ `System.DirectoryServices.Protocols` (LDAP bind).

**Privileges:** any authenticated domain account (read). Verified live against
`compass-tamu.tamu.edu` (4 DCs) — see [[compass-tamu-environment]].

## Config (`ActiveDirectory` section)

```jsonc
"ActiveDirectory": {
  "Interval": "00:02:00",
  "Domain": "",                 // empty = the host's own domain
  "DomainControllers": [],      // empty = discover; or list specific DC hosts to bind
  "UseLdaps": false,            // true = LDAPS on 636
  "LdapWarnMs": 500,
  "CheckReplication": true
}
```
