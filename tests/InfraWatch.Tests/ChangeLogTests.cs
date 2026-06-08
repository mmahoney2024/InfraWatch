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

            // vm-b removed, vm-c added.
            await store.SaveInventoryAsync([Vm("vm-a"), Vm("vm-c")]);

            var changes = await store.GetRecentChangesAsync();
            Assert.Equal(2, changes.Count);
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

    private static InventoryRecord Vm(string name) => new()
    {
        Pillar = "HyperV", Kind = "vm", Key = $"host/{name}", Name = name,
        Attributes = new Dictionary<string, string> { ["host"] = "host", ["state"] = "Running" },
    };
}
