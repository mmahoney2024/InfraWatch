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

app.Run();
