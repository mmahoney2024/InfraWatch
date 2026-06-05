using System.Net.Http.Headers;
using System.Text;
using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InfraWatch.Integrations.Jira;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Jira integration: options (from the "Jira" config section), the snapshot
    /// cache, an authenticated typed <see cref="JiraClient"/>, and the collector.
    /// </summary>
    public static IServiceCollection AddJiraIntegration(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<JiraOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Jira"));

        services.AddSingleton<JiraSnapshotCache>();

        services.AddHttpClient<JiraClient>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<JiraOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(30);
            if (string.IsNullOrWhiteSpace(o.BaseUrl))
                return;

            http.BaseAddress = new Uri(o.BaseUrl);
            if (o.IsConfigured)
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.Email}:{o.ApiToken}"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
        });

        services.AddSingleton<ICollector, JiraCollector>();
        return services;
    }
}
