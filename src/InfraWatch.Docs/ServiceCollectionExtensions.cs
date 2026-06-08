using Microsoft.Extensions.Configuration;
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

    /// <summary>Registers the scheduled report exporter (file / Confluence), bound from the
    /// "DocsExport" config section. No-op until something is enabled.</summary>
    public static IServiceCollection AddDocsExport(
        this IServiceCollection services, IConfiguration? config = null)
    {
        var builder = services.AddOptions<DocsExportOptions>();
        if (config is not null)
            builder.Bind(config.GetSection("DocsExport"));

        services.AddHttpClient();
        services.AddHostedService<DocsExporter>();
        return services;
    }
}
