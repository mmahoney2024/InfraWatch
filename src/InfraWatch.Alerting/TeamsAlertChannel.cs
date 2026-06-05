using System.Text;
using System.Text.Json;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Alerting;

/// <summary>
/// Posts an alert to a Microsoft Teams channel via webhook. Supports both webhook flavors,
/// auto-detected from the URL:
///   • classic "Incoming Webhook" connector  -> MessageCard payload
///   • newer "Workflows" (Power Automate)     -> Adaptive Card payload
/// No-ops when not configured.
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

        var payload = IsWorkflowUrl(_options.WebhookUrl)
            ? AdaptiveCardEnvelope(alert)
            : MessageCard(alert);

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(_options.WebhookUrl, content, cancellationToken);
            resp.EnsureSuccessStatusCode();
            _logger.LogInformation("Teams alert sent: {Title}", alert.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Teams alert: {Title}", alert.Title);
        }
    }

    // Workflows / Power Automate webhooks are Logic Apps URLs; classic connectors are *.office.com.
    private static bool IsWorkflowUrl(string url) =>
        url.Contains("logic.azure.com", StringComparison.OrdinalIgnoreCase)
        || url.Contains("/workflows/", StringComparison.OrdinalIgnoreCase)
        || url.Contains("powerplatform", StringComparison.OrdinalIgnoreCase);

    private static string MessageCard(Alert alert)
    {
        var card = new Dictionary<string, object?>
        {
            ["@type"] = "MessageCard",
            ["@context"] = "https://schema.org/extensions",
            ["themeColor"] = ThemeColor(alert.Severity),
            ["summary"] = alert.Title,
            ["title"] = alert.Title,
            ["text"] = string.IsNullOrWhiteSpace(alert.Url)
                ? alert.Message
                : $"{alert.Message}\n\n[Open]({alert.Url})",
        };
        return JsonSerializer.Serialize(card);
    }

    private static string AdaptiveCardEnvelope(Alert alert)
    {
        var body = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["type"] = "TextBlock", ["size"] = "Medium", ["weight"] = "Bolder",
                ["text"] = alert.Title, ["color"] = AdaptiveColor(alert.Severity), ["wrap"] = true,
            },
            new Dictionary<string, object?>
            {
                ["type"] = "TextBlock", ["text"] = alert.Message, ["wrap"] = true,
            },
        };

        var card = new Dictionary<string, object?>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body,
        };
        if (!string.IsNullOrWhiteSpace(alert.Url))
        {
            card["actions"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Action.OpenUrl", ["title"] = "Open", ["url"] = alert.Url,
                },
            };
        }

        var envelope = new
        {
            type = "message",
            attachments = new[]
            {
                new { contentType = "application/vnd.microsoft.card.adaptive", content = card },
            },
        };
        return JsonSerializer.Serialize(envelope);
    }

    private static string ThemeColor(HealthStatus s) => s switch
    {
        HealthStatus.Critical => "d23b3b",
        HealthStatus.Warning => "d9a200",
        _ => "1f9d55",
    };

    private static string AdaptiveColor(HealthStatus s) => s switch
    {
        HealthStatus.Critical => "Attention",
        HealthStatus.Warning => "Warning",
        _ => "Good",
    };
}
