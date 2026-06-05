using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.HyperV;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Hyper-V collector, binding <see cref="HyperVOptions"/> from
    /// the "HyperV" config section.</summary>
    public static IServiceCollection AddHyperVCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<HyperVOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("HyperV"));

        services.AddSingleton<ICollector, HyperVCollector>();
        return services;
    }
}
