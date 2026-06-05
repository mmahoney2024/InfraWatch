namespace InfraWatch.Alerting;

public sealed class AlertingOptions
{
    public TeamsOptions Teams { get; set; } = new();
    public EmailOptions Email { get; set; } = new();

    public sealed class TeamsOptions
    {
        public bool Enabled { get; set; }

        /// <summary>Incoming-webhook / workflow URL (a secret — inject outside source).</summary>
        public string WebhookUrl { get; set; } = "";

        public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(WebhookUrl);
    }

    public sealed class EmailOptions
    {
        public bool Enabled { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public bool UseSsl { get; set; } = true;
        public string Username { get; set; } = "";

        /// <summary>SMTP password (a secret — inject outside source). Empty = anonymous relay.</summary>
        public string Password { get; set; } = "";

        public string From { get; set; } = "";
        public List<string> To { get; set; } = [];

        public bool IsConfigured =>
            Enabled && !string.IsNullOrWhiteSpace(Host)
            && !string.IsNullOrWhiteSpace(From) && To.Count > 0;
    }
}
