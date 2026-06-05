# InfraWatch.Storage

SQLite-backed persistence. Implements `IStore` from Core.

- **Current state** — latest record per (target, check).
- **History** — append-only time-series for trends and graphs.
- **Change / drift log** — diffs of inventory over time ("what changed and when").

Append-only by design: history and drift detection depend on never overwriting. The
`IStore` abstraction leaves room to swap in a dedicated time-series DB later.
