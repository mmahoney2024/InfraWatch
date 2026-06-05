# InfraWatch.Service

The composition root. Depends on everything.

A Windows service (`Microsoft.Extensions.Hosting` worker) that:

- discovers and registers the enabled collectors,
- wires collectors → store → engine → alerting,
- runs the engine's schedule on a long-lived background loop.

This is the deployable unit on the monitoring host. Configuration (which pillars are
enabled, targets, schedules, credentials references) is loaded here. Collectors for
pillars we lack access to should be cleanly disabled, not fatal.
