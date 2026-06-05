# InfraWatch.Alerting

Alert delivery channels. Implements `IAlertChannel` from Core.

**Implemented:**
- **`TeamsAlertChannel`** — posts a MessageCard to a Microsoft Teams **incoming webhook**.
- **`EmailAlertChannel`** — sends via **SMTP** (`System.Net.Mail`).

The engine (`InfraWatch.Engine.AlertEvaluator`) decides *what* to alert on and *when* —
it fires on transition **into** Critical (e.g. the timeclock alert) and a recovery notice
on return to Healthy, seeded from the store at startup so a restart doesn't re-alert. This
project only handles *delivery*. Each channel **no-ops until enabled/configured**, so the
app always runs without alert config.

## Configuration (`Alerting` section)

```jsonc
"Alerting": {
  "Teams": { "Enabled": true, "WebhookUrl": "<secret>" },
  "Email": {
    "Enabled": true, "Host": "smtp.example.com", "Port": 587, "UseSsl": true,
    "Username": "<secret>", "Password": "<secret>",
    "From": "infrawatch@sscserv.com", "To": [ "ithelp@sscserv.com" ]
  }
}
```

Webhook URLs and SMTP passwords are **secrets** — inject via env / user-secrets, never in
source (`.gitignore` blocks `appsettings.*.local.json` and `secrets.json`).

> **Teams note:** the payload is the legacy MessageCard format used by classic incoming
> webhooks. If you wire it to a newer Teams **Workflows** webhook (Power Automate), that
> expects an Adaptive Card envelope instead — swap the payload in `TeamsAlertChannel`.
