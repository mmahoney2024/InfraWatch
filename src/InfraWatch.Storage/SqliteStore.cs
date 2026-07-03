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

    /// <summary>Pillars whose inventory churns by design and must not feed the drift/change log.</summary>
    private static readonly HashSet<string> NonDriftPillars = new(StringComparer.OrdinalIgnoreCase) { "Jira" };

    /// <summary>Inventory attributes that are *measured* (re-sampled every poll) rather than
    /// *configuration*. Comparing them for drift would re-flood the change log with poll noise,
    /// so they're excluded from config-change detection. Everything else (os, roles, FSMO holder,
    /// share path, VM host, cert issuer, scope state, …) is treated as configuration.</summary>
    private static readonly HashSet<string> VolatileAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "latencyMs", "lastLatencyMs", "latency", "accessible",
        "free", "inUse", "reserved", "percentInUse",
        "daysToExpiry", "vmCount", "running",
        "lastBackup", "ageDays", "restorePoints", "points", "lastRun", "lastResult", "status",
        "freeGB", "usedGB", "freePct", "answers",
    };

    private static readonly IReadOnlyDictionary<string, string> EmptyAttrs =
        new Dictionary<string, string>();

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

            CREATE TABLE IF NOT EXISTS change_log (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                pillar      TEXT NOT NULL,
                kind        TEXT NOT NULL,
                key         TEXT NOT NULL,
                name        TEXT NOT NULL,
                change_type TEXT NOT NULL,
                detail      TEXT,
                ts          TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_change_ts ON change_log (ts);

            CREATE TABLE IF NOT EXISTS pending_removal (
                pillar          TEXT NOT NULL,
                kind            TEXT NOT NULL,
                key             TEXT NOT NULL,
                first_missed_ts TEXT NOT NULL,
                PRIMARY KEY (pillar, kind, key)
            );
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
        var changes = await DetectChangesAsync(conn, rows);

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

        if (changes.Count > 0)
        {
            const string changeSql =
                """
                INSERT INTO change_log (pillar, kind, key, name, change_type, detail, ts)
                VALUES (@Pillar, @Kind, @Key, @Name, @ChangeType, @Detail, @Ts);
                """;
            var now = Iso(DateTimeOffset.UtcNow);
            foreach (var c in changes)
                await conn.ExecuteAsync(changeSql,
                    new { c.Pillar, c.Kind, c.Key, c.Name, c.ChangeType, c.Detail, Ts = now }, tx);
        }

        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>Diff this batch's inventory keys against the latest stored set per pillar to
    /// find added/removed items. Skips a pillar with no prior data (initial baseline).</summary>
    private static async Task<List<ChangeRecord>> DetectChangesAsync(SqliteConnection conn, IReadOnlyList<InventoryRecord> rows)
    {
        var changes = new List<ChangeRecord>();
        foreach (var grp in rows.GroupBy(r => r.Pillar))
        {
            // Integration pillars (e.g. Jira) inventory live tickets that legitimately open and
            // close every cycle — that is workflow, not infrastructure drift, so don't log it.
            if (NonDriftPillars.Contains(grp.Key))
                continue;

            var existing = (await conn.QueryAsync<KeyRow>(
                """
                SELECT kind, key, name, attributes FROM inventory
                WHERE pillar = @pillar
                  AND id IN (SELECT MAX(id) FROM inventory WHERE pillar = @pillar GROUP BY kind, key)
                """, new { pillar = grp.Key })).ToList();

            if (existing.Count == 0)
                continue; // first time we've seen this pillar — baseline, not drift

            var existingByKey = existing.ToDictionary(e => (e.Kind, e.Key));
            var incomingKeys = grp.Select(r => (r.Kind, r.Key)).ToHashSet();

            foreach (var r in grp)
            {
                if (!existingByKey.TryGetValue((r.Kind, r.Key), out var prior))
                {
                    changes.Add(new ChangeRecord { Pillar = grp.Key, Kind = r.Kind, Key = r.Key, Name = r.Name, ChangeType = "added" });
                }
                else
                {
                    // Same item seen before — did its *configuration* change?
                    var detail = DiffConfig(prior.Attributes, r.Attributes);
                    if (detail is not null)
                        changes.Add(new ChangeRecord
                        {
                            Pillar = grp.Key, Kind = r.Kind, Key = r.Key, Name = r.Name,
                            ChangeType = "changed", Detail = detail,
                        });
                }
            }

            // Removals are debounced: a single transient collection miss (e.g. one WMI/LDAP poll
            // failing to enumerate a host) must NOT be reported as "removed". We only confirm a
            // removal once the item has been absent across two consecutive collections, tracked
            // in pending_removal. Any item present this cycle clears its pending flag.
            var priorPending = (await conn.QueryAsync<KeyRow>(
                "SELECT kind, key FROM pending_removal WHERE pillar = @pillar", new { pillar = grp.Key }))
                .Select(r => (r.Kind, r.Key)).ToHashSet();
            await conn.ExecuteAsync("DELETE FROM pending_removal WHERE pillar = @pillar", new { pillar = grp.Key });

            var nowIso = Iso(DateTimeOffset.UtcNow);
            foreach (var e in existing)
            {
                if (incomingKeys.Contains((e.Kind, e.Key)))
                    continue; // still present — nothing to do (pending already cleared above)

                if (priorPending.Contains((e.Kind, e.Key)))
                {
                    // Missing again this cycle → confirmed removal.
                    changes.Add(new ChangeRecord { Pillar = grp.Key, Kind = e.Kind, Key = e.Key, Name = e.Name, ChangeType = "removed" });
                }
                else
                {
                    // First time we've missed it — tentatively flag, don't report yet.
                    await conn.ExecuteAsync(
                        "INSERT INTO pending_removal (pillar, kind, key, first_missed_ts) VALUES (@Pillar, @Kind, @Key, @Ts)",
                        new { Pillar = grp.Key, e.Kind, e.Key, Ts = nowIso });
                }
            }
        }
        return changes;
    }

    /// <summary>Compares the configuration (non-volatile) attributes of a prior vs. current
    /// inventory record. Returns a human-readable "key: old → new" summary of what changed, or
    /// null if no configuration attribute differs.</summary>
    private static string? DiffConfig(string? priorJson, IReadOnlyDictionary<string, string>? incoming)
    {
        var prior = Deserialize(priorJson) ?? EmptyAttrs;
        var current = incoming ?? EmptyAttrs;

        var keys = prior.Keys.Concat(current.Keys)
            .Where(k => !VolatileAttributes.Contains(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        var diffs = new List<string>();
        foreach (var k in keys)
        {
            var ov = prior.TryGetValue(k, out var a) ? a : "";
            var nv = current.TryGetValue(k, out var b) ? b : "";
            if (!string.Equals(ov, nv, StringComparison.Ordinal))
                diffs.Add($"{k}: {Trunc(ov)} → {Trunc(nv)}");
        }

        if (diffs.Count == 0) return null;
        const int max = 5;
        if (diffs.Count > max)
        {
            var shown = diffs.Take(max).ToList();
            shown.Add($"+{diffs.Count - max} more");
            return string.Join("; ", shown);
        }
        return string.Join("; ", diffs);
    }

    private static string Trunc(string s) =>
        string.IsNullOrEmpty(s) ? "(none)" : s.Length <= 60 ? s : s[..57] + "…";

    public async Task<IReadOnlyList<ChangeRecord>> GetRecentChangesAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<ChangeRow>(
            """
            SELECT pillar, kind, key, name, change_type, detail, ts
            FROM change_log ORDER BY id DESC LIMIT @limit;
            """, new { limit });
        return rows.Select(r => new ChangeRecord
        {
            Pillar = r.Pillar, Kind = r.Kind, Key = r.Key, Name = r.Name,
            ChangeType = r.ChangeType, Detail = r.Detail,
            Timestamp = DateTimeOffset.Parse(r.Ts, null, System.Globalization.DateTimeStyles.RoundtripKind),
        }).ToList();
    }

    private sealed class KeyRow
    {
        public string Kind { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Attributes { get; set; }
    }

    private sealed class ChangeRow
    {
        public string Pillar { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string ChangeType { get; set; } = "";
        public string? Detail { get; set; }
        public string Ts { get; set; } = "";
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

    /// <summary>
    /// Derives downtime windows from the append-only health history. A window function pulls
    /// only the rows where a check's status <em>changed</em> (cheap even with months of
    /// per-minute samples); a linear walk then pairs each entry into Critical with the first
    /// return to Healthy. Mirrors <c>AlertEvaluator</c> semantics exactly: Warning/Unknown
    /// samples neither open nor close an incident, and repeated Criticals don't re-open.
    /// </summary>
    public async Task<IReadOnlyList<IncidentRecord>> GetIncidentsAsync(
        DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<TransitionRow>(
            """
            SELECT pillar, target, check_name, status, summary, ts FROM (
                SELECT id, pillar, target, check_name, status, summary, ts,
                       LAG(status) OVER (PARTITION BY pillar, target, check_name ORDER BY id) AS prev
                FROM health
                WHERE ts >= @since
            )
            WHERE prev IS NULL OR status <> prev
            ORDER BY id;
            """, new { since = Iso(since) });

        var open = new Dictionary<(string Pillar, string Target, string Check), (DateTimeOffset Start, string? Error)>();
        var incidents = new List<IncidentRecord>();

        foreach (var r in rows)
        {
            var key = (r.Pillar, r.Target, r.CheckName);
            var status = (HealthStatus)r.Status;
            var ts = DateTimeOffset.Parse(r.Ts, null, System.Globalization.DateTimeStyles.RoundtripKind);

            if (status == HealthStatus.Critical)
            {
                if (!open.ContainsKey(key))
                    open[key] = (ts, r.Summary);
            }
            else if (status == HealthStatus.Healthy && open.Remove(key, out var o))
            {
                incidents.Add(new IncidentRecord
                {
                    Pillar = r.Pillar, Target = r.Target, Check = r.CheckName,
                    Error = o.Error, Start = o.Start, End = ts,
                });
            }
        }

        // Anything still open is an ongoing (or never-recovered) incident.
        foreach (var (key, o) in open)
            incidents.Add(new IncidentRecord
            {
                Pillar = key.Pillar, Target = key.Target, Check = key.Check,
                Error = o.Error, Start = o.Start, End = null,
            });

        return incidents.OrderByDescending(i => i.Start).ToList();
    }

    private sealed class TransitionRow
    {
        public string Pillar { get; set; } = "";
        public string Target { get; set; } = "";
        public string CheckName { get; set; } = "";
        public int Status { get; set; }
        public string? Summary { get; set; }
        public string Ts { get; set; } = "";
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
