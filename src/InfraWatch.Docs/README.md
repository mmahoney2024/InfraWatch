# InfraWatch.Docs

The documentation renderer. Depends on Core + Storage.

Turns stored `InventoryRecord`s + history into the three documentation flavors from
`CONCEPT.md` §3:

1. **Inventory** — a living map of what exists, measured not typed.
2. **Change history** — the drift log, rendered as a timeline.
3. **Reports** — scheduled/on-demand Markdown / PDF ("state of the network", backup
   posture, incident timelines). Optional push to an internal wiki.

Docs are a *rendering of measured reality* — this project never invents facts, it only
renders what Storage already holds.
