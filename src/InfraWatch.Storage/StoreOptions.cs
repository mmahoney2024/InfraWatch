namespace InfraWatch.Storage;

public sealed class StoreOptions
{
    /// <summary>
    /// Path to the SQLite database file. Relative paths are resolved against the content
    /// root. The containing directory is created if missing.
    /// </summary>
    public string DatabasePath { get; set; } = Path.Combine("data", "infrawatch.db");
}
