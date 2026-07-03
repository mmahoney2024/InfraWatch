using InfraWatch.Core;
using InfraWatch.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraWatch.Tests;

public class IncidentTests
{
    [Fact]
    public async Task Critical_then_healthy_yields_one_closed_incident()
    {
        var (store, path) = await NewStore();
        try
        {
            var t0 = DateTimeOffset.UtcNow.AddHours(-2);
            await store.SaveHealthAsync([
                Health(HealthStatus.Healthy, t0),
                Health(HealthStatus.Critical, t0.AddMinutes(10), "unreachable: Shutting down"),
                Health(HealthStatus.Critical, t0.AddMinutes(11)), // repeat — must not re-open
                Health(HealthStatus.Critical, t0.AddMinutes(12)),
                Health(HealthStatus.Healthy, t0.AddMinutes(15)),
                Health(HealthStatus.Healthy, t0.AddMinutes(16)),
            ]);

            var incidents = await store.GetIncidentsAsync(t0.AddMinutes(-1));

            var i = Assert.Single(incidents);
            Assert.False(i.IsOpen);
            Assert.Equal(t0.AddMinutes(10), i.Start, TimeSpan.FromSeconds(1));
            Assert.Equal(t0.AddMinutes(15), i.End!.Value, TimeSpan.FromSeconds(1));
            Assert.Equal(5, i.Duration!.Value.TotalMinutes, 0.1);
            Assert.Equal("unreachable: Shutting down", i.Error);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Still_critical_yields_open_incident()
    {
        var (store, path) = await NewStore();
        try
        {
            var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
            await store.SaveHealthAsync([
                Health(HealthStatus.Healthy, t0),
                Health(HealthStatus.Critical, t0.AddMinutes(5), "spooler dead"),
            ]);

            var incidents = await store.GetIncidentsAsync(t0.AddMinutes(-1));

            var i = Assert.Single(incidents);
            Assert.True(i.IsOpen);
            Assert.Null(i.End);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Warning_neither_opens_nor_closes_and_keys_are_independent()
    {
        var (store, path) = await NewStore();
        try
        {
            var t0 = DateTimeOffset.UtcNow.AddHours(-1);
            await store.SaveHealthAsync([
                // Warning only — no incident.
                Health(HealthStatus.Warning, t0, check: "tls-expiry"),
                Health(HealthStatus.Healthy, t0.AddMinutes(5), check: "tls-expiry"),
                // Critical → Warning → Healthy: closes at the Healthy sample, not the Warning.
                Health(HealthStatus.Critical, t0.AddMinutes(10), "down", check: "ping"),
                Health(HealthStatus.Warning, t0.AddMinutes(15), check: "ping"),
                Health(HealthStatus.Healthy, t0.AddMinutes(20), check: "ping"),
                // A second target's incident is tracked separately.
                Health(HealthStatus.Critical, t0.AddMinutes(12), "down", target: "other"),
                Health(HealthStatus.Healthy, t0.AddMinutes(13), target: "other"),
            ]);

            var incidents = await store.GetIncidentsAsync(t0.AddMinutes(-1));

            Assert.Equal(2, incidents.Count);
            var ping = Assert.Single(incidents, i => i.Check == "ping" && i.Target == "h");
            Assert.Equal(10, ping.Duration!.Value.TotalMinutes, 0.1);
            var other = Assert.Single(incidents, i => i.Target == "other");
            Assert.Equal(1, other.Duration!.Value.TotalMinutes, 0.1);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Incidents_before_window_are_excluded()
    {
        var (store, path) = await NewStore();
        try
        {
            var old = DateTimeOffset.UtcNow.AddDays(-10);
            var t0 = DateTimeOffset.UtcNow.AddHours(-1);
            await store.SaveHealthAsync([
                Health(HealthStatus.Critical, old, "old outage"),
                Health(HealthStatus.Healthy, old.AddMinutes(5)),
                Health(HealthStatus.Critical, t0, "new outage"),
                Health(HealthStatus.Healthy, t0.AddMinutes(5)),
            ]);

            var incidents = await store.GetIncidentsAsync(DateTimeOffset.UtcNow.AddDays(-1));

            var i = Assert.Single(incidents);
            Assert.Equal("new outage", i.Error);
        }
        finally { Cleanup(path); }
    }

    // ---- helpers -----------------------------------------------------------

    private static async Task<(SqliteStore Store, string Path)> NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iw-test-{Guid.NewGuid():N}.db");
        var store = new SqliteStore(
            Options.Create(new StoreOptions { DatabasePath = path }),
            NullLogger<SqliteStore>.Instance);
        await store.InitializeAsync();
        return (store, path);
    }

    private static HealthRecord Health(
        HealthStatus status, DateTimeOffset ts, string? summary = null,
        string target = "h", string check = "ping") => new()
    {
        Pillar = "HostNet",
        Target = target,
        Check = check,
        Status = status,
        Summary = summary,
        Timestamp = ts,
    };

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
