using InfraWatch.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Collectors.Print;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Print Server collector. Binds <see cref="PrintOptions"/> from the
    /// "Print" config section when <paramref name="config"/> is supplied.</summary>
    public static IServiceCollection AddPrintCollector(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<PrintOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("Print"));

        services.AddSingleton<ICollector, PrintCollector>();
        return services;
    }
}
