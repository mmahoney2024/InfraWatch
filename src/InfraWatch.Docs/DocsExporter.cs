using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Docs;

/// <summary>
/// Periodically renders the "State of the Network" report and publishes it: to a file
/// (a share / wiki-watched folder) and/or a Confluence page. No-op when nothing is enabled.
/// </summary>
public sealed class DocsExporter : BackgroundService
{
    private readonly NetworkReport _report;
    private readonly DocsExportOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DocsExporter> _logger;

    public DocsExporter(
        NetworkReport report, IOptions<DocsExportOptions> options,
        IHttpClientFactory httpFactory, ILogger<DocsExporter> logger)
    {
        _report = report;
        _options = options.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AnyEnabled)
            return;

        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await ExportOnceAsync(stoppingToken);
            try { await Task.Delay(_options.Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ExportOnceAsync(CancellationToken ct)
    {
        try
        {
            if (_options.FileEnabled)
            {
                var md = await _report.GenerateMarkdownAsync(ct);
                var path = Path.GetFullPath(_options.FilePath);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(path, md, ct);
                _logger.LogInformation("Exported report to {Path}", path);
            }

            if (_options.Confluence.IsConfigured)
            {
                var html = await _report.GenerateHtmlBodyAsync(ct);
                await PublishConfluenceAsync(html, _options.Confluence, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Docs export failed");
        }
    }

    private async Task PublishConfluenceAsync(string htmlBody, DocsExportOptions.ConfluenceOptions c, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.Email}:{c.ApiToken}")));
        var baseUrl = c.BaseUrl.TrimEnd('/');

        using var getResp = await http.GetAsync($"{baseUrl}/rest/api/content/{c.PageId}?expand=version", ct);
        getResp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync(ct));
        var version = doc.RootElement.GetProperty("version").GetProperty("number").GetInt32();

        object payload = string.IsNullOrWhiteSpace(c.ParentPageId)
            ? new
            {
                id = c.PageId,
                type = "page",
                title = c.Title,
                version = new { number = version + 1, message = "Updated by InfraWatch" },
                body = new { storage = new { value = htmlBody, representation = "storage" } },
            }
            : new
            {
                id = c.PageId,
                type = "page",
                title = c.Title,
                version = new { number = version + 1, message = "Updated by InfraWatch" },
                ancestors = new[] { new { id = c.ParentPageId } },
                body = new { storage = new { value = htmlBody, representation = "storage" } },
            };
        using var putResp = await http.PutAsJsonAsync($"{baseUrl}/rest/api/content/{c.PageId}", payload, ct);
        putResp.EnsureSuccessStatusCode();
        _logger.LogInformation("Published report to Confluence page {PageId}", c.PageId);
    }
}
