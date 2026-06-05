# InfraWatch.Alerting

Alert delivery channels. Implements `IAlertChannel` from Core.

Planned channels: **email**, **Teams**, **ntfy**, **Discord** — whatever's standard at
work. The engine decides *what* to alert on and *when* (dedup/flap handled there); this
project only handles *delivery*.

Channel config (webhooks, SMTP) is secrets — load from a secrets manager / user-secrets,
never committed.
