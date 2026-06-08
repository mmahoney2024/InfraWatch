namespace InfraWatch.Docs;

public sealed class DocsExportOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Write the report Markdown to a file (e.g. a share or wiki-watched folder).</summary>
    public bool FileEnabled { get; set; }

    public string FilePath { get; set; } = "docs/state-of-the-network.md";

    public ConfluenceOptions Confluence { get; set; } = new();

    public bool AnyEnabled => FileEnabled || Confluence.IsConfigured;

    public sealed class ConfluenceOptions
    {
        public bool Enabled { get; set; }

        /// <summary>Confluence base, e.g. https://sscserv.atlassian.net/wiki</summary>
        public string BaseUrl { get; set; } = "";

        public string Email { get; set; } = "";

        /// <summary>API token (a secret).</summary>
        public string ApiToken { get; set; } = "";

        /// <summary>The page to update (it must already exist).</summary>
        public string PageId { get; set; } = "";

        /// <summary>Optional parent page; when set, every publish re-asserts this page's parent
        /// so it stays filed under (e.g.) "Infrastructure Documentation".</summary>
        public string ParentPageId { get; set; } = "";

        public string Title { get; set; } = "InfraWatch — State of the Network";

        public bool IsConfigured =>
            Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Email)
            && !string.IsNullOrWhiteSpace(ApiToken) && !string.IsNullOrWhiteSpace(PageId);
    }
}
