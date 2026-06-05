using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Veeam;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Veeam collector: options (from the "Veeam" config section), an
    /// authenticated typed <see cref="VeeamClient"/> (base URL + self-signed cert handling),
    /// and the collector.</summary>
    public static IServiceCollection AddVeeamCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<VeeamOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Veeam"));

        services.AddHttpClient<VeeamClient>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<VeeamOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(o.BaseUrl))
                http.BaseAddress = new Uri(o.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(60);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var o = sp.GetRequiredService<IOptions<VeeamOptions>>().Value;
            var handler = new HttpClientHandler();
            if (o.IgnoreCertErrors)
                handler.ServerCertificateCustomValidationCallback = static (_, _, _, _) => true;
            return handler;
        });

        services.AddSingleton<ICollector, VeeamCollector>();
        return services;
    }
}
