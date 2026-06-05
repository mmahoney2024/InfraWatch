# InfraWatch.Collectors.Dhcp

**Pillar:** DHCP.

**Health checks:** active offer test *or* server-service + lease-pool monitoring
(prefer the non-intrusive service/lease approach by default).

**Documentation:** scopes, ranges, lease counts, reservations, exclusions.

**Access method:** `DhcpServer` PowerShell module / service queries; lease-pool stats.

**Privileges required:** read access to the DHCP server (DHCP Users / server-local read).
Crafted-offer testing can resemble recon — gated behind explicit authorization
(`CONCEPT.md` §6.1) and off by default.
