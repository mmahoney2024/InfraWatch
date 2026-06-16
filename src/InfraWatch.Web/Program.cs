using InfraWatch.Alerting;
using InfraWatch.Collectors.ActiveDirectory;
using InfraWatch.Collectors.Dhcp;
using InfraWatch.Collectors.Dns;
using InfraWatch.Collectors.HostNet;
using InfraWatch.Collectors.HyperV;
using InfraWatch.Collectors.Imaging;
using InfraWatch.Collectors.Smb;
using InfraWatch.Collectors.Veeam;
using InfraWatch.Core;
using InfraWatch.Docs;
using InfraWatch.Engine;
using InfraWatch.Integrations.Jira;
using InfraWatch.Storage;
using InfraWatch.Web;
using Microsoft.Extensions.Options;

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
builder.Services.AddDocs(builder.Configuration);
builder.Services.AddDocsExport(builder.Configuration);
builder.Services.AddAlerting(builder.Configuration);
builder.Services.AddHostNetCollector(builder.Configuration);
builder.Services.AddDnsCollector(builder.Configuration);
builder.Services.AddActiveDirectoryCollector(builder.Configuration);
builder.Services.AddHyperVCollector(builder.Configuration);
builder.Services.AddSmbCollector(builder.Configuration);
builder.Services.AddDhcpCollector(builder.Configuration);
builder.Services.AddImagingCollector(builder.Configuration);
builder.Services.AddVeeamCollector(builder.Configuration);
builder.Services.AddJiraIntegration(builder.Configuration);

var app = builder.Build();

// Surface the Confluence documentation links in the dashboard (set once from the "Assets" config).
{
    var assets = app.Services.GetRequiredService<IOptions<AssetCatalogOptions>>().Value;
    DashboardRenderer.ConfluenceHubUrl = assets.InfrastructureDocUrl;
    DashboardRenderer.ConfluenceReportUrl = assets.StateOfNetworkUrl;
    DashboardRenderer.ConfluenceInventoryUrl = assets.InventoryPageUrl;
}

static bool IsDark(HttpContext http) =>
    http.Request.Cookies.TryGetValue("iw-theme", out var t)
    && string.Equals(t, "dark", StringComparison.OrdinalIgnoreCase);

// Show only checks seen recently, so a removed/renamed check ages out of the live view
// instead of lingering forever. (A failing check still updates every cycle, so it stays.)
static IReadOnlyList<HealthRecord> Fresh(IReadOnlyList<HealthRecord> health)
{
    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
    return health.Where(h => h.Timestamp >= cutoff).ToList();
}

// Overview — the wall of tiles + Jira widgets.
app.MapGet("/", async (HttpContext http, IStore store, JiraSnapshotCache jira) =>
{
    var health = Fresh(await store.GetLatestHealthAsync());
    return Results.Content(DashboardRenderer.RenderOverview(health, jira.Current, IsDark(http)), "text/html");
});

// Drill 1 — one pillar's checks + inventory.
app.MapGet("/pillar", async (string name, HttpContext http, IStore store) =>
{
    var health = Fresh(await store.GetLatestHealthAsync());
    var inventory = await store.GetLatestInventoryAsync(name);
    return Results.Content(DashboardRenderer.RenderPillar(name, health, inventory, IsDark(http)), "text/html");
});

// Drill 2 — one target/server's checks + its inventory.
app.MapGet("/target", async (string pillar, string target, HttpContext http, IStore store) =>
{
    var health = Fresh(await store.GetLatestHealthAsync());
    var inventory = await store.GetLatestInventoryAsync(pillar);
    return Results.Content(DashboardRenderer.RenderTarget(pillar, target, health, inventory, IsDark(http)), "text/html");
});

// Drill 3 — a single check's latest state + history.
app.MapGet("/check", async (string pillar, string target, string check, HttpContext http, IStore store) =>
{
    var since = DateTimeOffset.UtcNow.AddDays(-1);
    var history = await store.GetHealthHistoryAsync(pillar, target, check, since);
    return Results.Content(DashboardRenderer.RenderCheck(pillar, target, check, history, IsDark(http)), "text/html");
});

// Machine-readable state, handy for debugging / a future SPA.
app.MapGet("/api/state", async (IStore store, JiraSnapshotCache jira) =>
{
    var health = Fresh(await store.GetLatestHealthAsync());
    return Results.Json(new { health, jira = jira.Current });
});

// Generated documentation — "State of the Network".
app.MapGet("/docs", async (HttpContext http, NetworkReport report) =>
{
    var body = await report.GenerateHtmlBodyAsync();
    return Results.Content(DashboardRenderer.RenderDocs(body, IsDark(http)), "text/html");
});

app.MapGet("/docs/report.md", async (NetworkReport report) =>
{
    var md = await report.GenerateMarkdownAsync();
    return Results.Text(md, "text/markdown");
});

app.MapGet("/docs/changes", async (HttpContext http, IStore store) =>
{
    var changes = await store.GetRecentChangesAsync(300);
    return Results.Content(DashboardRenderer.RenderChanges(changes, IsDark(http)), "text/html");
});

app.MapGet("/healthz", () => Results.Text("ok"));

// On-demand active DHCP offer/lease test (triggered by the button on a DHCP server page).
app.MapGet("/dhcp-test", async (string server, bool? lease, DhcpTester tester) =>
{
    var outcome = await Task.Run(() => tester.Run(server, lease ?? false));
    return Results.Text(outcome.Message);
});

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
