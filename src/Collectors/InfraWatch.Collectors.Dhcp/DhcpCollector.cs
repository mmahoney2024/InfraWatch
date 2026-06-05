using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Dhcp;

/// <summary>
/// DHCP health + inventory. Per server, queries the DhcpServer PowerShell module for scope
/// statistics (free/in-use addresses, % in use) and reports each scope's pool pressure.
/// Uses the service account's own Windows credentials. Read-only.
/// </summary>
public sealed partial class DhcpCollector : ICollector
{
    public const string Pillar = "Dhcp";

    private readonly DhcpOptions _options;
    private readonly ILogger<DhcpCollector> _logger;

    public DhcpCollector(IOptions<DhcpOptions> options, ILogger<DhcpCollector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => Pillar;
    public TimeSpan Interval => _options.Interval;

    public async Task<CollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        var health = new List<HealthRecord>();
        var inventory = new List<InventoryRecord>();

        foreach (var server in _options.Servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(server))
                continue;
            if (!HostName().IsMatch(server))
            {
                _logger.LogWarning("Skipping invalid DHCP server name '{Server}'", server);
                continue;
            }
            await QueryServerAsync(server, health, inventory, cancellationToken);
        }

        return new CollectionResult(health, inventory);
    }

    private async Task QueryServerAsync(
        string server, List<HealthRecord> health, List<InventoryRecord> inventory, CancellationToken ct)
    {
        string stdout, stderr;
        int exitCode;
        try
        {
            (exitCode, stdout, stderr) = await RunPowerShellAsync(ScriptFor(server), ct);
        }
        catch (Exception ex)
        {
            health.Add(H(server, "service", HealthStatus.Critical, null, null, $"query error: {ex.Message}"));
            return;
        }

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            health.Add(H(server, "service", HealthStatus.Critical, null, null,
                $"unreachable / query failed: {FirstLine(stderr)}"));
            return;
        }

        var scopes = 0;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line[0] is not ('{' or '['))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        AddScope(server, el, health, inventory);
                        scopes++;
                    }
                }
                else
                {
                    AddScope(server, doc.RootElement, health, inventory);
                    scopes++;
                }
            }
            catch (JsonException) { /* skip non-JSON noise */ }
        }

        health.Insert(0, H(server, "service", HealthStatus.Healthy, scopes, "scopes",
            $"DHCP responding, {scopes} scope(s)"));
    }

    private void AddScope(string server, JsonElement el, List<HealthRecord> health, List<InventoryRecord> inventory)
    {
        var scopeId = Str(el, "ScopeId");
        var name = Str(el, "Name");
        var state = Str(el, "State");
        var free = Int(el, "Free");
        var inUse = Int(el, "InUse");
        var reserved = Int(el, "Reserved");
        var pct = Dbl(el, "PercentInUse");

        var active = string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase);
        var status = !active
            ? HealthStatus.Healthy
            : pct >= _options.PercentInUseCrit ? HealthStatus.Critical
            : pct >= _options.PercentInUseWarn ? HealthStatus.Warning
            : HealthStatus.Healthy;

        health.Add(H(server, $"scope {scopeId}", status, pct, "%",
            active ? $"{name}: {pct}% in use, {free} free" : $"{name}: {state}"));

        inventory.Add(new InventoryRecord
        {
            Pillar = Pillar, Kind = "scope", Key = $"{server}/{scopeId}", Name = $"{scopeId} ({name})",
            Attributes = new Dictionary<string, string>
            {
                ["server"] = server, ["scopeId"] = scopeId, ["name"] = name, ["state"] = state,
                ["free"] = free.ToString(), ["inUse"] = inUse.ToString(),
                ["reserved"] = reserved.ToString(), ["percentInUse"] = pct.ToString(),
            },
        });
    }

    private static string ScriptFor(string server) =>
        "$ErrorActionPreference='Stop';" +
        "Import-Module DhcpServer;" +
        $"$srv='{server}';" +
        "$scopes=Get-DhcpServerv4Scope -ComputerName $srv;" +
        "$stats=Get-DhcpServerv4ScopeStatistics -ComputerName $srv;" +
        "foreach($sc in $scopes){" +
        "$id=$sc.ScopeId.IPAddressToString;" +
        "$st=$stats|Where-Object{$_.ScopeId.IPAddressToString -eq $id}|Select-Object -First 1;" +
        "[pscustomobject]@{ScopeId=$id;Name=$sc.Name;State=\"$($sc.State)\";Free=[int]$st.Free;InUse=[int]$st.InUse;Reserved=[int]$st.Reserved;PercentInUse=[math]::Round([double]$st.PercentageInUse,1)}|ConvertTo-Json -Compress -Depth 4}";

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunPowerShellAsync(string script, CancellationToken ct)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("could not start powershell.exe");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return (-1, "", $"timed out after {_options.TimeoutSeconds}s");
        }

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string Str(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
    private static int Int(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.TryGetInt32(out var i) ? i : 0;
    private static double Dbl(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.TryGetDouble(out var d) ? d : 0;

    private static string FirstLine(string s) =>
        string.IsNullOrWhiteSpace(s) ? "no detail"
        : s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "no detail";

    private static HealthRecord H(string target, string check, HealthStatus status, double? value, string? unit, string summary) => new()
    {
        Pillar = Pillar, Target = target, Check = check, Status = status,
        Value = value, Unit = unit, Summary = summary,
    };

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex HostName();
}
