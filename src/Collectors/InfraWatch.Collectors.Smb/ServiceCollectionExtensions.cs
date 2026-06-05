using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.Smb;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the SMB collector, binding <see cref="SmbOptions"/> from the
    /// "Smb" config section.</summary>
    public static IServiceCollection AddSmbCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<SmbOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Smb"));

        services.AddSingleton<ICollector, SmbCollector>();
        return services;
    }
}
