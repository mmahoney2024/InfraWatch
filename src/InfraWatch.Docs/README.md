# InfraWatch.Docs

The documentation renderer. **Implemented.** Turns stored inventory + health into the
self-maintaining documentation from `CONCEPT.md` §3 — *a rendering of measured reality, not
hand-typed*.

- **`NetworkReport`** — generates the **"State of the Network"** document (Markdown, and HTML
  via Markdig) from `IStore`: a health summary plus per-pillar inventory tables (DCs, VMs,
  shares, DNS records, DHCP scopes, TLS certs, Veeam jobs/backups/repos, …) with an
  "Attention" callout for anything Warning/Critical.

- **Change / drift log** — `IStore.GetRecentChangesAsync`: inventory items added/removed
  over time (the store diffs each batch against the prior set). Shown at `/docs/changes` and
  as a "Recent changes" section in the report.
- **`DocsExporter`** — a hosted service that, on a schedule, writes the report Markdown to a
  file and/or **publishes it to a Confluence page** (updates the page body via the REST API).

Served by the web app:
- **`/docs`** — the report rendered in the dashboard (linked from the header).
- **`/docs/report.md`** — the raw Markdown (downloadable / wiki-ready).
- **`/docs/changes`** — the change/drift log.

## Config (`DocsExport` section)

```jsonc
"DocsExport": {
  "Interval": "06:00:00",
  "FileEnabled": true,
  "FilePath": "docs/state-of-the-network.md",   // a share / wiki-watched folder
  "Confluence": {
    "Enabled": false, "BaseUrl": "https://sscserv.atlassian.net/wiki",
    "Email": "you@sscserv.com", "ApiToken": "<secret>",
    "PageId": "<existing page id>", "Title": "InfraWatch — State of the Network"
  }
}
```

> Confluence publishing is built but unverified (needs a target page + token). File export
> is verified.
