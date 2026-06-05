# InfraWatch.Engine

The brain. Depends on Core + Storage.

- **Scheduler** — decides when each collector runs.
- **Baseline / drift detection** — compares new inventory against stored state.
- **Alert rules** — evaluates health into alert conditions (e.g. RPO breach, cert expiry).
- **Flap detection & dedup** — avoids alert storms on a flapping check.

This is where most of "what mature tools gave us for free" gets reimplemented
(retry/backoff, flap detection, alert dedup). Keep it boring and well-tested.
