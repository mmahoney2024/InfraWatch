using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Alerting;

/// <summary>
/// Posts an alert to a Microsoft Teams channel via an incoming webhook (MessageCard
/// payload). No-ops when not configured.
/// </summary>
public sealed class TeamsAlertChannel : IAlertChannel
{
    private readonly HttpClient _http;
    private readonly AlertingOptions.TeamsOptions _options;
    private readonly ILogger<TeamsAlertChannel> _logger;

    public TeamsAlertChannel(HttpClient http, IOptions<AlertingOptions> options, ILogger<TeamsAlertChannel> logger)
    {
        _http = http;
        _options = options.Value.Teams;
        _logger = logger;
    }

    public string Name => "Teams";

    public async Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            return;

        var card = new
        {
            type = "MessageCard",
            context = "https://schema.org/extensions",
            themeColor = ThemeColor(alert.Severity),
            summary = alert.Title,
            title = alert.Title,
            text = string.IsNullOrWhiteSpace(alert.Url)
                ? alert.Message
                : $"{alert.Message}\n\n[Open]({alert.Url})",
        };

        // MessageCard uses the literal keys "@type"/"@context"; serialize then patch.
        var payload = System.Text.Json.JsonSerializer.Serialize(card)
            .Replace("\"type\":", "\"@type\":")
            .Replace("\"context\":", "\"@context\":");

        try
        {
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(_options.WebhookUrl, content, cancellationToken);
            resp.EnsureSuccessStatusCode();
            _logger.LogInformation("Teams alert sent: {Title}", alert.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Teams alert: {Title}", alert.Title);
        }
    }

    private static string ThemeColor(HealthStatus s) => s switch
    {
        HealthStatus.Critical => "d23b3b",
        HealthStatus.Warning => "d9a200",
        _ => "1f9d55",
    };
}
