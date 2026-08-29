using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Infrastructure.Database;
using AndroidTVManager.Infrastructure.Logging;
using AndroidTVManager.Infrastructure.Scripts;
using AndroidTVManager.Core.Scripts;
using AndroidTVManager.Infrastructure.Packages;
using AndroidTVManager.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTVManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAndroidTVManagerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ILocalAppDataPaths, LocalAppDataPaths>();
        services.AddSingleton<IAppLogger, FileLogger>();
        services.AddSingleton<IDeviceSnapshotRepository, DeviceSnapshotRepository>();
        services.AddSingleton<IDeviceInspectionService, DeviceInspectionService>();
        services.AddSingleton<IPackageInventoryService, PackageInventoryService>();
        services.AddSingleton<IAdbCommandService, AdbCommandService>();
        services.AddSingleton<IPackageClassifier, PackageClassifier>();
        services.AddSingleton<IDebloatPlanner, DebloatPlanner>();
        services.AddSingleton<IDebloatExecutionService, DebloatExecutionService>();
        services.AddSingleton<IScriptExecutionStore, ScriptExecutionStore>();
        services.AddSingleton<IScriptExecutionService, ScriptExecutionService>();
        services.AddSingleton<IAdbToolsManager, AdbToolsManager>();
        services.AddSingleton<IAdbProcessRunner, AdbProcessRunner>();
        services.AddSingleton<IAdbDeviceTracker, AdbDeviceTracker>();
        services.AddSingleton<IAdbConnectionService, AdbConnectionService>();
        services.AddSingleton<IApkInstaller, ApkInstaller>();
        services.AddSingleton<IPackageManager, PackageManager>();
        services.AddSingleton<IDeviceToolsService, DeviceToolsService>();
        services.AddSingleton<SqliteDatabase>();
        services.AddSingleton<IDeviceRepository, DeviceRepository>();
        services.AddSingleton<IConnectionHistoryRepository, ConnectionHistoryRepository>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        return services;
    }
}
