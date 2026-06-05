using System.Text.Json;
using Dapper;
using InfraWatch.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Storage;

/// <summary>
/// Append-only SQLite implementation of <see cref="IStore"/>. Every health/inventory record
/// is inserted; "current state" is the max-id row per natural key.
/// </summary>
public sealed class SqliteStore : IStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteStore> _logger;

    static SqliteStore()
    {
        // Map columns like check_name -> CheckName without per-query aliases.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public SqliteStore(IOptions<StoreOptions> options, ILogger<SqliteStore> logger)
    {
        _logger = logger;
        var path = Path.GetFullPath(options.Value.DatabasePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.ExecuteAsync("PRAGMA journal_mode=WAL;");
        await conn.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS health (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                pillar     TEXT    NOT NULL,
                target     TEXT    NOT NULL,
                check_name TEXT    NOT NULL,
                status     INTEGER NOT NULL,
                summary    TEXT,
                value      REAL,
                unit       TEXT,
                ts         TEXT    NOT NULL,
                details    TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_health_key ON health (pillar, target, check_name, id);
            CREATE INDEX IF NOT EXISTS ix_health_ts  ON health (ts);

            CREATE TABLE IF NOT EXISTS inventory (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                pillar     TEXT    NOT NULL,
                kind       TEXT    NOT NULL,
                key        TEXT    NOT NULL,
                name       TEXT    NOT NULL,
                ts         TEXT    NOT NULL,
                attributes TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_inv_key ON inventory (pillar, kind, key, id);
            """);
        _logger.LogInformation("SQLite store initialized at {ConnectionString}", _connectionString);
    }

    public async Task SaveHealthAsync(IEnumerable<HealthRecord> records, CancellationToken cancellationToken = default)
    {
        var rows = records as IReadOnlyList<HealthRecord> ?? records.ToList();
        if (rows.Count == 0) return;

        await using var conn = Open();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
        const string sql =
            """
            INSERT INTO health (pillar, target, check_name, status, summary, value, unit, ts, details)
            VALUES (@Pillar, @Target, @CheckName, @Status, @Summary, @Value, @Unit, @Ts, @Details);
            """;
        foreach (var r in rows)
        {
            await conn.ExecuteAsync(sql, new
            {
                r.Pillar,
                r.Target,
                CheckName = r.Check,
                Status = (int)r.Status,
                r.Summary,
                r.Value,
                r.Unit,
                Ts = Iso(r.Timestamp),
                Details = Serialize(r.Details),
            }, tx);
        }
        await tx.CommitAsync(cancellationToken);
    }

    public async Task SaveInventoryAsync(IEnumerable<InventoryRecord> records, CancellationToken cancellationToken = default)
    {
        var rows = records as IReadOnlyList<InventoryRecord> ?? records.ToList();
        if (rows.Count == 0) return;

        await using var conn = Open();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
        const string sql =
            """
            INSERT INTO inventory (pillar, kind, key, name, ts, attributes)
            VALUES (@Pillar, @Kind, @Key, @Name, @Ts, @Attributes);
            """;
        foreach (var r in rows)
        {
            await conn.ExecuteAsync(sql, new
            {
                r.Pillar,
                r.Kind,
                r.Key,
                r.Name,
                Ts = Iso(r.Timestamp),
                Attributes = Serialize(r.Attributes),
            }, tx);
        }
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HealthRecord>> GetLatestHealthAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<HealthRow>(
            """
            SELECT pillar, target, check_name, status, summary, value, unit, ts, details
            FROM health
            WHERE id IN (SELECT MAX(id) FROM health GROUP BY pillar, target, check_name)
            ORDER BY pillar, target, check_name;
            """);
        return rows.Select(MapHealth).ToList();
    }

    public async Task<IReadOnlyList<HealthRecord>> GetHealthHistoryAsync(
        string pillar, string target, string check, DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<HealthRow>(
            """
            SELECT pillar, target, check_name, status, summary, value, unit, ts, details
            FROM health
            WHERE pillar = @pillar AND target = @target AND check_name = @check AND ts >= @since
            ORDER BY ts DESC, id DESC;
            """,
            new { pillar, target, check, since = Iso(since) });
        return rows.Select(MapHealth).ToList();
    }

    public async Task<IReadOnlyList<InventoryRecord>> GetLatestInventoryAsync(
        string pillar, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<InventoryRow>(
            """
            SELECT pillar, kind, key, name, ts, attributes
            FROM inventory
            WHERE pillar = @pillar
              AND id IN (SELECT MAX(id) FROM inventory WHERE pillar = @pillar GROUP BY kind, key)
            ORDER BY kind, key;
            """,
            new { pillar });
        return rows.Select(MapInventory).ToList();
    }

    private static string Iso(DateTimeOffset ts) => ts.ToUniversalTime().ToString("O");

    private static string? Serialize(IReadOnlyDictionary<string, string>? dict) =>
        dict is null || dict.Count == 0 ? null : JsonSerializer.Serialize(dict);

    private static IReadOnlyDictionary<string, string>? Deserialize(string? json) =>
        string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json);

    private static HealthRecord MapHealth(HealthRow r) => new()
    {
        Pillar = r.Pillar,
        Target = r.Target,
        Check = r.CheckName,
        Status = (HealthStatus)r.Status,
        Summary = r.Summary,
        Value = r.Value,
        Unit = r.Unit,
        Timestamp = DateTimeOffset.Parse(r.Ts, null, System.Globalization.DateTimeStyles.RoundtripKind),
        Details = Deserialize(r.Details),
    };

    private static InventoryRecord MapInventory(InventoryRow r) => new()
    {
        Pillar = r.Pillar,
        Kind = r.Kind,
        Key = r.Key,
        Name = r.Name,
        Timestamp = DateTimeOffset.Parse(r.Ts, null, System.Globalization.DateTimeStyles.RoundtripKind),
        Attributes = Deserialize(r.Attributes),
    };

    private sealed class HealthRow
    {
        public string Pillar { get; set; } = "";
        public string Target { get; set; } = "";
        public string CheckName { get; set; } = "";
        public int Status { get; set; }
        public string? Summary { get; set; }
        public double? Value { get; set; }
        public string? Unit { get; set; }
        public string Ts { get; set; } = "";
        public string? Details { get; set; }
    }

    private sealed class InventoryRow
    {
        public string Pillar { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Ts { get; set; } = "";
        public string? Attributes { get; set; }
    }
}
