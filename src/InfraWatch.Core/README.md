# InfraWatch.Core

Domain models and abstractions. **No I/O, no third-party dependencies.**

- `HealthRecord` — "is it OK right now?" (target, check, status, measured values, time).
- `InventoryRecord` — "what exists?" (the measured facts that become documentation).
- Severity / status types.
- `ICollector` — a pillar that probes infra and emits the two record types.
- `IStore` — persistence contract (current state, history, change/drift log).
- `IAlertChannel` — an alert delivery channel.

Everything else depends on Core; Core depends on nothing. Keep it that way.
