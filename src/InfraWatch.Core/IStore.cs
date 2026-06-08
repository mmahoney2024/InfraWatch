namespace InfraWatch.Core;

/// <summary>
/// Append-only persistence for health + inventory. Current state is a view over the latest
/// records; history is every record ever written.
/// </summary>
public interface IStore
{
    /// <summary>Create the schema if needed. Safe to call repeatedly.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveHealthAsync(IEnumerable<HealthRecord> records, CancellationToken cancellationToken = default);

    Task SaveInventoryAsync(IEnumerable<InventoryRecord> records, CancellationToken cancellationToken = default);

    /// <summary>Latest health record per (pillar, target, check) — powers the tile wall.</summary>
    Task<IReadOnlyList<HealthRecord>> GetLatestHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Full history for one check, newest first — powers drill-down graphs.</summary>
    Task<IReadOnlyList<HealthRecord>> GetHealthHistoryAsync(
        string pillar, string target, string check, DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>Latest inventory record per key for a pillar — powers documentation views.</summary>
    Task<IReadOnlyList<InventoryRecord>> GetLatestInventoryAsync(
        string pillar, CancellationToken cancellationToken = default);

    /// <summary>Recent inventory drift events (items added/removed), newest first.</summary>
    Task<IReadOnlyList<ChangeRecord>> GetRecentChangesAsync(
        int limit = 200, CancellationToken cancellationToken = default);
}
