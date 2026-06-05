using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Alerting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Teams and email alert channels (bound from the "Alerting" config
    /// section). Each channel no-ops until enabled/configured, so this is always safe to add.
    /// </summary>
    public static IServiceCollection AddAlerting(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<AlertingOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Alerting"));

        // Teams needs an HttpClient (typed client), then exposed as an IAlertChannel.
        services.AddHttpClient<TeamsAlertChannel>();
        services.AddSingleton<IAlertChannel>(sp => sp.GetRequiredService<TeamsAlertChannel>());

        services.AddSingleton<IAlertChannel, EmailAlertChannel>();
        return services;
    }
}
