# Collectors

One project per pillar. Each implements `ICollector` from `InfraWatch.Core` and emits
normalized `HealthRecord` + `InventoryRecord`.

**Why one project each:** pillars pull in different, sometimes heavy dependencies
(PowerShell SDK, directory services, vendor APIs). Isolating them keeps Core
dependency-free, lets pillars build/test independently, and makes it trivial to disable a
pillar we don't yet have access to.

Each collector's `README.md` documents the **exact privileges** it needs — access depth
is the dial that controls documentation depth (`CONCEPT.md` §6.4).

| Project | Pillar |
|---|---|
| `InfraWatch.Collectors.HostNet` | Host / Net (ICMP, TCP, TLS, HTTP) |
| `InfraWatch.Collectors.Dns` | DNS |
| `InfraWatch.Collectors.Dhcp` | DHCP |
| `InfraWatch.Collectors.Smb` | SMB / File |
| `InfraWatch.Collectors.ActiveDirectory` | Active Directory |
| `InfraWatch.Collectors.HyperV` | Hyper-V |
| `InfraWatch.Collectors.Veeam` | Veeam B&R |
