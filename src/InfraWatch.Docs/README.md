# InfraWatch.Docs

The documentation renderer. **Implemented.** Turns stored inventory + health into the
self-maintaining documentation from `CONCEPT.md` §3 — *a rendering of measured reality, not
hand-typed*.

- **`NetworkReport`** — generates the **"State of the Network"** document (Markdown, and HTML
  via Markdig) from `IStore`: a health summary plus per-pillar inventory tables (DCs, VMs,
  shares, DNS records, DHCP scopes, TLS certs, Veeam jobs/backups/repos, …) with an
  "Attention" callout for anything Warning/Critical.

Served by the web app:
- **`/docs`** — the report rendered in the dashboard (linked from the header).
- **`/docs/report.md`** — the raw Markdown (downloadable / wiki-ready).

## Next in this project
- **Change / drift log** — what changed and when (store records inventory diffs).
- **Scheduled export** — write the report to a file / push to a wiki on a schedule.
