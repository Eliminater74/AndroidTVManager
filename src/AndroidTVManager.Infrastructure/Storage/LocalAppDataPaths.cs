using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Infrastructure.Storage;

public sealed class LocalAppDataPaths : ILocalAppDataPaths
{
    public LocalAppDataPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidTVManager");
        DatabasePath = Path.Combine(Root, "Data", "androidtvmanager.db");
        ToolsPath = Path.Combine(Root, "Tools", "PlatformTools");
        LogsPath = Path.Combine(Root, "Logs");
        ScriptsPath = Path.Combine(Root, "Scripts");
        SnapshotsPath = Path.Combine(Root, "Snapshots");
        ScreenshotsPath = Path.Combine(Root, "Screenshots");
        RecordingsPath = Path.Combine(Root, "Recordings");
        BackupsPath = Path.Combine(Root, "Backups");
        TempPath = Path.Combine(Root, "Temp");
    }

    public string Root { get; }
    public string DatabasePath { get; }
    public string ToolsPath { get; }
    public string LogsPath { get; }
    public string ScriptsPath { get; }
    public string SnapshotsPath { get; }
    public string ScreenshotsPath { get; }
    public string RecordingsPath { get; }
    public string BackupsPath { get; }
    public string TempPath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        foreach (var path in new[] { ToolsPath, LogsPath, ScriptsPath, SnapshotsPath, ScreenshotsPath, RecordingsPath, BackupsPath, TempPath })
            Directory.CreateDirectory(path);
    }
}
