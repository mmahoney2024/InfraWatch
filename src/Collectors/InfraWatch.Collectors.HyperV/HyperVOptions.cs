namespace InfraWatch.Collectors.HyperV;

public sealed class HyperVOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Hyper-V hosts to query. Empty = the local machine.</summary>
    public List<string> Hosts { get; set; } = [];

    /// <summary>Host CPU at or above this percent is Warning.</summary>
    public double CpuWarnPct { get; set; } = 85;

    /// <summary>Free host RAM below this percent is Warning.</summary>
    public double MemFreeWarnPct { get; set; } = 10;

    /// <summary>More than this many checkpoints on a host is Warning (sprawl).</summary>
    public int CheckpointWarn { get; set; } = 10;
}
