# InfraWatch.Collectors.Dns

**Pillar:** DNS. **Implemented.**

Resolves configured records, verifies the expected answer, measures query latency, and
detects NXDOMAIN / SERVFAIL. Queries the **system resolver** by default, or a **specific
DNS server** per check (so each resolver / AD DNS server can be monitored individually).
Uses the [DnsClient](https://dnsclient.michaco.net/) library.

**Health:** per check — `Healthy` (resolved, under the latency threshold), `Warning`
(slow, or no records), `Critical` (NXDOMAIN, SERVFAIL, unreachable, or an answer that
doesn't match `Expect`).

**Documentation:** each resolved record set is stored as inventory (`dns-record`).

**Access:** none beyond UDP/53 reachability to the resolvers under test.

## Config (`Dns` section)

```jsonc
"Dns": {
  "Interval": "00:01:00",
  "WarnMs": 200,
  "Checks": [
    // query each internal AD/DNS server for the domain record and verify the subnet
    { "Name": "compass-tamu.tamu.edu", "Type": "A", "Server": "128.194.183.47", "Expect": "128.194.183." },
    { "Name": "compass-tamu.tamu.edu", "Type": "A", "Server": "128.194.183.97", "Expect": "128.194.183." },
    { "Name": "github.com", "Type": "A" }   // Server omitted = system resolver
  ]
}
```

`Type`: A, AAAA, CNAME, MX, TXT, NS, PTR, SOA. `Server`: resolver IP (omit for system).
`Expect`: substring at least one answer must contain.

> Note: `Checks` is empty by default in code — .NET config binding *appends* to a non-empty
> list rather than replacing it, so the real defaults live in `appsettings.json`.
