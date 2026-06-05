using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.Dns;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the DNS collector, binding <see cref="DnsOptions"/> from the
    /// "Dns" config section when <paramref name="config"/> is supplied.</summary>
    public static IServiceCollection AddDnsCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<DnsOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Dns"));

        services.AddSingleton<ICollector, DnsCollector>();
        return services;
    }
}
