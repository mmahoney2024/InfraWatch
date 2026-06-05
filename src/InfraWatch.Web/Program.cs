using InfraWatch.Alerting;
using InfraWatch.Collectors.HostNet;
using InfraWatch.Core;
using InfraWatch.Engine;
using InfraWatch.Integrations.Jira;
using InfraWatch.Storage;
using InfraWatch.Web;

var builder = WebApplication.CreateBuilder(args);

// Run correctly under the Windows Service Control Manager when installed as a service.
builder.Host.UseWindowsService();

// Load secrets in EVERY environment (CreateBuilder only loads user-secrets in Development):
//  - user-secrets: works for local runs as your own user account
//  - appsettings.Local.json: a gitignored file next to the app — the right place for a
//    deployed Windows service (whose service account can't see your dev user-secrets)
// Env vars (e.g. Jira__ApiToken) also work and override these.
builder.Configuration.AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly(), optional: true);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Store -> engine -> collectors/integrations, all hosted in this one web process.
builder.Services.AddSqliteStore(o =>
{
    var path = builder.Configuration["Storage:DatabasePath"];
    if (!string.IsNullOrWhiteSpace(path))
        o.DatabasePath = path;
});
builder.Services.AddEngine();
builder.Services.AddAlerting(builder.Configuration);
builder.Services.AddHostNetCollector(builder.Configuration);
builder.Services.AddJiraIntegration(builder.Configuration);

var app = builder.Build();

// One pane of glass.
app.MapGet("/", async (IStore store, JiraSnapshotCache jira) =>
{
    var health = await store.GetLatestHealthAsync();
    return Results.Content(DashboardRenderer.Render(health, jira.Current), "text/html");
});

// Machine-readable state, handy for debugging / a future SPA.
app.MapGet("/api/state", async (IStore store, JiraSnapshotCache jira) =>
{
    var health = await store.GetLatestHealthAsync();
    return Results.Json(new { health, jira = jira.Current });
});

app.MapGet("/healthz", () => Results.Text("ok"));

// Send a sample alert through every configured channel — handy for verifying delivery.
app.MapGet("/test-alert", async (IEnumerable<IAlertChannel> channels) =>
{
    var alert = new Alert
    {
        Title = "InfraWatch test alert",
        Message = "This is a test alert from InfraWatch. If you received this, alerting works.",
        Severity = HealthStatus.Warning,
        Pillar = "Test",
    };

    var attempted = new List<string>();
    foreach (var channel in channels)
    {
        await channel.SendAsync(alert);
        attempted.Add(channel.Name);
    }

    return Results.Text(attempted.Count == 0
        ? "No alert channels registered."
        : $"Test alert dispatched to: {string.Join(", ", attempted)}. " +
          "Disabled/unconfigured channels silently no-op; check the app log for send results.");
});

app.Run();
