# InfraWatch.Collectors.Dns

**Pillar:** DNS.

**Health checks:** resolve known records, verify answers match expectations, response
time, SERVFAIL / wrong-answer detection.

**Documentation:** zones/records visible, forwarders, authoritative servers.

**Access method:** `System.Net.Dns` for basic resolution; targeted resolver queries
(and, where authorized, zone enumeration) for deeper inventory.

**Privileges required:** query access to the resolvers/servers under test. Zone transfer
/ deep zone inventory needs explicit authorization (`CONCEPT.md` §6.1).
