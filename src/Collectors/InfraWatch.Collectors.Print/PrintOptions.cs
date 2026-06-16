namespace InfraWatch.Collectors.Print;

public sealed class PrintOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Print server host (WMI target), e.g. <c>fsprint.compass-tamu.tamu.edu</c>.</summary>
    public string Server { get; set; } = "";

    /// <summary>A printer with at least this many jobs queued is flagged Warning (a backlog /
    /// stuck queue). 0 disables the queue-depth check.</summary>
    public int QueueWarnJobs { get; set; } = 50;
}
