using InfraWatch.Core;
using InfraWatch.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraWatch.Tests;

public class SqliteStoreTests
{
    [Fact]
    public async Task GetLatestHealth_returns_newest_record_per_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iw-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(
                Options.Create(new StoreOptions { DatabasePath = path }),
                NullLogger<SqliteStore>.Instance);
            await store.InitializeAsync();

            await store.SaveHealthAsync([Health(HealthStatus.Warning, 200, DateTimeOffset.UtcNow.AddMinutes(-5))]);
            await store.SaveHealthAsync([Health(HealthStatus.Healthy, 20, DateTimeOffset.UtcNow)]);

            var latest = await store.GetLatestHealthAsync();

            var rec = Assert.Single(latest);
            Assert.Equal(HealthStatus.Healthy, rec.Status);
            Assert.Equal(20, rec.Value);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(path);
        }
    }

    [Fact]
    public async Task History_preserves_every_record_and_round_trips_details()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iw-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(
                Options.Create(new StoreOptions { DatabasePath = path }),
                NullLogger<SqliteStore>.Instance);
            await store.InitializeAsync();

            var since = DateTimeOffset.UtcNow.AddMinutes(-10);
            await store.SaveHealthAsync([
                Health(HealthStatus.Healthy, 10, since.AddMinutes(1)) with
                {
                    Details = new Dictionary<string, string> { ["k"] = "v" },
                },
                Health(HealthStatus.Critical, 99, since.AddMinutes(2)),
            ]);

            var history = await store.GetHealthHistoryAsync("HostNet", "h", "ping", since);

            Assert.Equal(2, history.Count); // append-only: nothing overwritten
            Assert.Equal("v", history.Last().Details?["k"]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(path);
        }
    }

    private static HealthRecord Health(HealthStatus status, double value, DateTimeOffset ts) => new()
    {
        Pillar = "HostNet",
        Target = "h",
        Check = "ping",
        Status = status,
        Value = value,
        Unit = "ms",
        Timestamp = ts,
    };

    private static void TryDelete(string path)
    {
        foreach (var p in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
        }
    }
}
