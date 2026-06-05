# InfraWatch.Web

The dashboard. ASP.NET Core. Depends on Core + Storage + Docs.

The "one pane of glass" from `CONCEPT.md` §4:

- **Top level** — wall of status tiles grouped by pillar, each green/yellow/red with a
  one-glance summary.
- **Drill in** — individual checks behind a tile, history/latency graphs,
  last-state-change, the relevant documentation slice.
- **Drill again** — raw measured detail + change log for that specific item.

Read-only over the store; renders reports via `InfraWatch.Docs`. It observes; it does not
probe infrastructure itself.
