using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.HostNet;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Host/Net collector. Binds <see cref="HostNetOptions"/> from
    /// the "HostNet" config section when <paramref name="config"/> is supplied.</summary>
    public static IServiceCollection AddHostNetCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<HostNetOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("HostNet"));

        services.AddSingleton<ICollector, HostNetCollector>();
        return services;
    }
}
