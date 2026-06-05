# InfraWatch.Tests

Unit and integration tests for InfraWatch.

- **Core** — record/model behavior, status rollup logic.
- **Engine** — scheduling, drift detection, flap detection, alert dedup (the tricky,
  battle-tested-elsewhere logic we now own).
- **Storage** — round-trip and change-log/diff behavior against an in-memory/temp SQLite.
- **Collectors** — normalization (pillar reality → shared records), graceful degradation
  when access is missing. Avoid tests that require live production infrastructure; mock
  the access boundary.

Framework TBD when the solution is scaffolded (xUnit by default).
