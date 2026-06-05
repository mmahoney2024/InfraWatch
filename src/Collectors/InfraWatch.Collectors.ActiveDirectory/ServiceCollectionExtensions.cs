using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.ActiveDirectory;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Active Directory collector, binding
    /// <see cref="ActiveDirectoryOptions"/> from the "ActiveDirectory" config section.</summary>
    public static IServiceCollection AddActiveDirectoryCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<ActiveDirectoryOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("ActiveDirectory"));

        services.AddSingleton<ICollector, ActiveDirectoryCollector>();
        return services;
    }
}
