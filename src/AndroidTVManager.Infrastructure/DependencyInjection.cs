using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTVManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAndroidTVManagerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ILocalAppDataPaths, LocalAppDataPaths>();
        return services;
    }
}
