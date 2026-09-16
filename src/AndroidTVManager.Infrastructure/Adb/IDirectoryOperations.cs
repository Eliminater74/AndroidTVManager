namespace AndroidTVManager.Infrastructure.Adb;

public interface IDirectoryOperations
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void DeleteDirectory(string path);
    void MoveDirectory(string source, string destination);
}

public sealed class FileSystemDirectoryOperations : IDirectoryOperations
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);
    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
}
