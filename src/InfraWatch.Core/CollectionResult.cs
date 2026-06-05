namespace InfraWatch.Core;

/// <summary>
/// What a single collector run produces: health observations plus inventory facts.
/// </summary>
public sealed record CollectionResult
{
    public IReadOnlyList<HealthRecord> Health { get; init; } = [];

    public IReadOnlyList<InventoryRecord> Inventory { get; init; } = [];

    public static CollectionResult Empty { get; } = new();

    public CollectionResult() { }

    public CollectionResult(
        IReadOnlyList<HealthRecord> health,
        IReadOnlyList<InventoryRecord>? inventory = null)
    {
        Health = health;
        Inventory = inventory ?? [];
    }
}
