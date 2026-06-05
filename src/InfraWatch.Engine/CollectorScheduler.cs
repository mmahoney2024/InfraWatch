using InfraWatch.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfraWatch.Engine;

/// <summary>
/// Runs every registered <see cref="ICollector"/> on its own interval and persists what it
/// produces. Each collector runs once at startup, then repeats. A failing collector is
/// logged and retried next tick — it never takes the others down.
/// </summary>
public sealed class CollectorScheduler : BackgroundService
{
    private readonly IReadOnlyList<ICollector> _collectors;
    private readonly IStore _store;
    private readonly ILogger<CollectorScheduler> _logger;

    public CollectorScheduler(
        IEnumerable<ICollector> collectors, IStore store, ILogger<CollectorScheduler> logger)
    {
        _collectors = collectors.ToList();
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _store.InitializeAsync(stoppingToken);

        if (_collectors.Count == 0)
        {
            _logger.LogWarning("No collectors registered; engine has nothing to run.");
            return;
        }

        _logger.LogInformation(
            "Engine starting with {Count} collector(s): {Names}",
            _collectors.Count, string.Join(", ", _collectors.Select(c => c.Name)));

        var loops = _collectors.Select(c => RunLoopAsync(c, stoppingToken));
        await Task.WhenAll(loops);
    }

    private async Task RunLoopAsync(ICollector collector, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RunOnceAsync(collector, ct);

            try
            {
                await Task.Delay(collector.Interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(ICollector collector, CancellationToken ct)
    {
        try
        {
            var result = await collector.CollectAsync(ct);

            if (result.Health.Count > 0)
                await _store.SaveHealthAsync(result.Health, ct);
            if (result.Inventory.Count > 0)
                await _store.SaveInventoryAsync(result.Inventory, ct);

            _logger.LogInformation(
                "Collector {Name}: {Health} health, {Inventory} inventory record(s)",
                collector.Name, result.Health.Count, result.Inventory.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Collector {Name} failed; will retry next interval.", collector.Name);
        }
    }
}
