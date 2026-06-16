using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.Web;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Web Servers collector. Binds <see cref="WebOptions"/> from the
    /// "Web" config section when <paramref name="config"/> is supplied.</summary>
    public static IServiceCollection AddWebCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<WebOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Web"));

        services.AddSingleton<ICollector, WebCollector>();
        return services;
    }
}
