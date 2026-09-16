namespace AndroidTVManager.Infrastructure.Adb;

public sealed class PlatformToolsActivator
{
    public const string PreviousSuffix = ".previous";

    private readonly IDirectoryOperations _directories;

    public PlatformToolsActivator(IDirectoryOperations? directories = null)
    {
        _directories = directories ?? new FileSystemDirectoryOperations();
    }

    public async Task<string> ActivateAsync(
        string packageRoot,
        string activePath,
        Func<string, CancellationToken, Task<string>> verifyActiveDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(activePath);
        ArgumentNullException.ThrowIfNull(verifyActiveDirectory);

        if (!_directories.FileExists(Path.Combine(packageRoot, "adb.exe")))
            throw new InvalidDataException("The Platform-Tools package does not contain adb.exe.");
        if (!_directories.FileExists(Path.Combine(packageRoot, "fastboot.exe")))
            throw new InvalidDataException("The Platform-Tools package does not contain fastboot.exe.");

        var previous = activePath + PreviousSuffix;
        var movedOld = false;
        var movedNew = false;
        try
        {
            if (_directories.DirectoryExists(previous))
                _directories.DeleteDirectory(previous);

            if (_directories.DirectoryExists(activePath))
            {
                _directories.MoveDirectory(activePath, previous);
                movedOld = true;
            }

            _directories.MoveDirectory(packageRoot, activePath);
            movedNew = true;

            var version = await verifyActiveDirectory(activePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException("The activated ADB executable did not return a valid version.");
            if (!_directories.FileExists(Path.Combine(activePath, "fastboot.exe")))
                throw new InvalidDataException("The activated Platform-Tools directory does not contain fastboot.exe.");

            TryDeletePrevious(previous);
            return version;
        }
        catch (Exception exception)
        {
            try
            {
                Rollback(activePath, previous, movedOld, movedNew);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    "Platform-Tools activation failed and the previous installation could not be restored.",
                    new AggregateException(exception, rollbackException));
            }

            throw;
        }
    }

    private void TryDeletePrevious(string previous)
    {
        if (!_directories.DirectoryExists(previous))
            return;

        try
        {
            _directories.DeleteDirectory(previous);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Rollback(string activePath, string previous, bool movedOld, bool movedNew)
    {
        if (movedNew && _directories.DirectoryExists(activePath))
        {
            try
            {
                _directories.DeleteDirectory(activePath);
            }
            catch (Exception)
            {
                var failed = activePath + $".failed-{Guid.NewGuid():N}";
                _directories.MoveDirectory(activePath, failed);
            }
        }

        if (movedOld && _directories.DirectoryExists(previous) && !_directories.DirectoryExists(activePath))
            _directories.MoveDirectory(previous, activePath);
    }
}
