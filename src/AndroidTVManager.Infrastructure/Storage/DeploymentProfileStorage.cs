using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Infrastructure.Storage;

public sealed class DeploymentProfileStorage : IDeploymentProfileStorage
{
    private readonly ILocalAppDataPaths _paths;

    public DeploymentProfileStorage(ILocalAppDataPaths paths)
    {
        _paths = paths;
    }

    public string GetProfileDirectory(long profileId)
    {
        if (profileId <= 0)
            throw new ArgumentOutOfRangeException(nameof(profileId));
        return Path.Combine(_paths.Root, "Profiles", profileId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public string GetPackagePath(long profileId, string relativePath)
    {
        var root = Path.GetFullPath(GetProfileDirectory(profileId)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The profile package path is outside the profile directory.");
        return path;
    }

    public async Task<string> CopyPackageAsync(
        long profileId,
        string sourcePath,
        string? storedFileName = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The selected package does not exist.", sourcePath);
        var directory = GetProfileDirectory(profileId);
        Directory.CreateDirectory(directory);
        var fileName = string.IsNullOrWhiteSpace(storedFileName)
            ? Path.GetFileName(sourcePath)
            : Path.GetFileName(storedFileName);
        var destination = GetPackagePath(profileId, fileName);
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);
        await using var target = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
        return fileName;
    }

    public Task DeleteProfileFilesAsync(long profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = GetProfileDirectory(profileId);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }
}
