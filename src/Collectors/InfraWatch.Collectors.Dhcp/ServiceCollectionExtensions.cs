using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.Dhcp;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the DHCP collector, binding <see cref="DhcpOptions"/> from the
    /// "Dhcp" config section.</summary>
    public static IServiceCollection AddDhcpCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<DhcpOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Dhcp"));

        services.AddSingleton<ICollector, DhcpCollector>();
        services.AddSingleton<DhcpTester>();
        return services;
    }
}
