using InfraWatch.Core;
using InfraWatch.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraWatch.Tests;

public class ChangeLogTests
{
    [Fact]
    public async Task Detects_added_and_removed_inventory_after_baseline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iw-chg-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(
                Options.Create(new StoreOptions { DatabasePath = path }),
                NullLogger<SqliteStore>.Instance);
            await store.InitializeAsync();

            // Baseline: two VMs. No drift recorded for the first sighting.
            await store.SaveInventoryAsync([Vm("vm-a"), Vm("vm-b")]);
            Assert.Empty(await store.GetRecentChangesAsync());

            // vm-b absent, vm-c added. Addition is immediate; removal is debounced (one transient
            // miss must not be reported), so vm-b is only flagged pending — not yet "removed".
            await store.SaveInventoryAsync([Vm("vm-a"), Vm("vm-c")]);

            var afterFirst = await store.GetRecentChangesAsync();
            Assert.Contains(afterFirst, c => c.ChangeType == "added" && c.Name == "vm-c");
            Assert.DoesNotContain(afterFirst, c => c.ChangeType == "removed" && c.Name == "vm-b");

            // vm-b still absent on the next collection → removal is now confirmed.
            await store.SaveInventoryAsync([Vm("vm-a"), Vm("vm-c")]);

            var changes = await store.GetRecentChangesAsync();
            Assert.Contains(changes, c => c.ChangeType == "added" && c.Name == "vm-c");
            Assert.Contains(changes, c => c.ChangeType == "removed" && c.Name == "vm-b");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Detects_configuration_change_but_ignores_volatile_only_change()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iw-cfg-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(
                Options.Create(new StoreOptions { DatabasePath = path }),
                NullLogger<SqliteStore>.Instance);
            await store.InitializeAsync();

            // Baseline.
            await store.SaveInventoryAsync([Vm("vm-a", host: "fs-aio", state: "Running", latencyMs: "5")]);
            Assert.Empty(await store.GetRecentChangesAsync());

            // Only a volatile attribute (latencyMs) changes — must NOT be recorded as drift.
            await store.SaveInventoryAsync([Vm("vm-a", host: "fs-aio", state: "Running", latencyMs: "42")]);
            Assert.Empty(await store.GetRecentChangesAsync());

            // A configuration attribute changes (VM migrated to a different host) — recorded.
            await store.SaveInventoryAsync([Vm("vm-a", host: "fs-hyper-v", state: "Running", latencyMs: "7")]);

            var changes = await store.GetRecentChangesAsync();
            var changed = Assert.Single(changes, c => c.ChangeType == "changed");
            Assert.Equal("vm-a", changed.Name);
            Assert.Contains("host:", changed.Detail);
            Assert.Contains("fs-aio", changed.Detail);
            Assert.Contains("fs-hyper-v", changed.Detail);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
        }
    }

    private static InventoryRecord Vm(string name) => new()
    {
        Pillar = "HyperV", Kind = "vm", Key = $"host/{name}", Name = name,
        Attributes = new Dictionary<string, string> { ["host"] = "host", ["state"] = "Running" },
    };

    private static InventoryRecord Vm(string name, string host, string state, string latencyMs) => new()
    {
        Pillar = "HyperV", Kind = "vm", Key = $"vm/{name}", Name = name,
        Attributes = new Dictionary<string, string>
        {
            ["host"] = host, ["state"] = state, ["latencyMs"] = latencyMs,
        },
    };
}
