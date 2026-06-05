# InfraWatch.Collectors.HostNet

**Pillar:** general host / network health. The recommended first collector for the
walking skeleton — pure .NET, no special access required.

**Health checks:** ICMP latency/jitter, TCP port reachability, TLS cert expiry, HTTP
status/latency.

**Documentation:** open ports, certs + expiry, hostnames, OS hints.

**Access method:** `System.Net.NetworkInformation.Ping`, raw `TcpClient`, `SslStream`
(read cert), `HttpClient`.

**Privileges required:** none beyond network egress to the targets. ICMP may need raw
socket / firewall allowances on some hosts.
