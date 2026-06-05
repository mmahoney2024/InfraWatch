# InfraWatch.Collectors.HyperV

**Pillar:** Hyper-V (standalone hosts and/or failover clusters).

**Health checks:** host CPU/RAM/storage, VM states, replica health, checkpoint sprawl,
cluster node/quorum, CSV free space.

**Documentation:** VM inventory, VM-to-host mapping, vCPU/RAM allocation, virtual switch
layout.

**Access method:** CIM/WMI, plus the `Hyper-V` and `FailoverClusters` PowerShell modules.

**Privileges required:** read access on the Hyper-V hosts / cluster (Hyper-V
Administrators or an equivalent least-privilege read role). Open question: standalone vs
cluster, and how many (`CONCEPT.md` §9).
