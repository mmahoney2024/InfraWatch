using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Docs;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the documentation generators.</summary>
    public static IServiceCollection AddDocs(this IServiceCollection services)
    {
        services.AddSingleton<NetworkReport>();
        return services;
    }
}
