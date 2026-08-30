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
        services.AddSingleton<FileLogger>();
        services.AddSingleton<IAppLogger>(services => services.GetRequiredService<FileLogger>());
        services.AddSingleton<ILogViewerService>(services => services.GetRequiredService<FileLogger>());
        services.AddSingleton<IDeviceSnapshotRepository, DeviceSnapshotRepository>();
        services.AddSingleton<IDeviceInspectionService, DeviceInspectionService>();
        services.AddSingleton<IDeviceBackupService, DeviceBackupService>();
        services.AddSingleton<IConfigurationExplorerService, ConfigurationExplorerService>();
        services.AddSingleton<IConfigurationSnapshotStore, ConfigurationSnapshotStore>();
        services.AddSingleton<IPackageInventoryService, PackageInventoryService>();
        services.AddSingleton<IPackageIconService, PackageIconService>();
        services.AddSingleton<IPackageInventoryRepository, PackageInventoryRepository>();
        services.AddSingleton<IPackagePreferenceRepository, PackagePreferenceRepository>();
        services.AddSingleton<IAdbCommandService, AdbCommandService>();
        services.AddSingleton<IPackageClassifier, PackageClassifier>();
        services.AddSingleton<IDebloatPlanner, DebloatPlanner>();
        services.AddSingleton<IDebloatExecutionService, DebloatExecutionService>();
        services.AddSingleton<IDeveloperVerificationPolicyProvider, DeveloperVerificationPolicyProvider>();
        services.AddSingleton<IRootGuidanceProvider, RootGuidanceProvider>();
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
