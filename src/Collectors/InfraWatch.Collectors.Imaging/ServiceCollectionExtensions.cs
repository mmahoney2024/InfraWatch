using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.Imaging;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Imaging (WDS/MDT) collector, binding
    /// <see cref="ImagingOptions"/> from the "Imaging" config section.</summary>
    public static IServiceCollection AddImagingCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<ImagingOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Imaging"));

        services.AddSingleton<ICollector, ImagingCollector>();
        return services;
    }
}
