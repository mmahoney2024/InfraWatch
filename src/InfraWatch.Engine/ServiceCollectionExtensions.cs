using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InfraWatch.Engine;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the collector scheduler as a hosted background service. Collectors are
    /// registered separately (each pillar/integration provides its own Add… method).
    /// </summary>
    public static IServiceCollection AddEngine(this IServiceCollection services)
    {
        services.AddHostedService<CollectorScheduler>();
        return services;
    }
}
