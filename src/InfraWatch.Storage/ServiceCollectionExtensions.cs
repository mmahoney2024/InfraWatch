using InfraWatch.Core;
using Microsoft.Extensions.DependencyInjection;

namespace InfraWatch.Storage;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the SQLite-backed <see cref="IStore"/> as a singleton.</summary>
    public static IServiceCollection AddSqliteStore(
        this IServiceCollection services, Action<StoreOptions>? configure = null)
    {
        var options = services.AddOptions<StoreOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.AddSingleton<IStore, SqliteStore>();
        return services;
    }
}
