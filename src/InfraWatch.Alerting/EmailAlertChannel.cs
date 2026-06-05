using System.Net;
using System.Net.Mail;
using InfraWatch.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraWatch.Alerting;

/// <summary>
/// Sends an alert by SMTP email. No-ops when not configured.
/// </summary>
public sealed class EmailAlertChannel : IAlertChannel
{
    private readonly AlertingOptions.EmailOptions _options;
    private readonly ILogger<EmailAlertChannel> _logger;

    public EmailAlertChannel(IOptions<AlertingOptions> options, ILogger<EmailAlertChannel> logger)
    {
        _options = options.Value.Email;
        _logger = logger;
    }

    public string Name => "Email";

    public async Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            return;

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.From),
                Subject = $"[InfraWatch] {alert.Title}",
                Body = string.IsNullOrWhiteSpace(alert.Url)
                    ? alert.Message
                    : $"{alert.Message}\n\n{alert.Url}",
            };
            foreach (var to in _options.To)
                message.To.Add(to);

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.UseSsl,
                Credentials = string.IsNullOrWhiteSpace(_options.Username)
                    ? CredentialCache.DefaultNetworkCredentials
                    : new NetworkCredential(_options.Username, _options.Password),
            };

            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Email alert sent to {Count} recipient(s): {Title}",
                _options.To.Count, alert.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email alert: {Title}", alert.Title);
        }
    }
}
