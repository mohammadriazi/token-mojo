using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TokenMojo.Core.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("tokenmojo-tests-").FullName;

    public string Write(string relativePath, string contents, DateTimeOffset modified)
    {
        string file = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, contents);
        File.SetLastWriteTimeUtc(file, modified.UtcDateTime);
        return file;
    }

    public string MakeDir(string relativePath) => Directory.CreateDirectory(System.IO.Path.Combine(Path, relativePath)).FullName;

    public ClaudeSource Source(string name) => new(
        name,
        System.IO.Path.Combine(Path, name, "cctokmon-state.json"),
        System.IO.Path.Combine(Path, name, "cctokmon-sessions"));

    /// <summary>Makes a file or directory unreadable for the current user until the result is disposed.</summary>
    public static IDisposable DenyRead(string path)
    {
        return OperatingSystem.IsWindows() ? DenyReadOnWindows(path) : DenyReadOnUnix(path);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);

    [UnsupportedOSPlatform("windows")]
    private static IDisposable DenyReadOnUnix(string path)
    {
        UnixFileMode previous = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        return new Restore(() => File.SetUnixFileMode(path, previous));
    }

    [SupportedOSPlatform("windows")]
    private static IDisposable DenyReadOnWindows(string path)
    {
        FileSystemAccessRule deny = new(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ReadData | FileSystemRights.ListDirectory, AccessControlType.Deny);
        if (Directory.Exists(path))
        {
            DirectoryInfo directory = new(path);
            DirectorySecurity security = directory.GetAccessControl();
            security.AddAccessRule(deny);
            directory.SetAccessControl(security);
            return new Restore(() =>
            {
                security.RemoveAccessRule(deny);
                directory.SetAccessControl(security);
            });
        }
        FileInfo file = new(path);
        FileSecurity fileSecurity = file.GetAccessControl();
        fileSecurity.AddAccessRule(deny);
        file.SetAccessControl(fileSecurity);
        return new Restore(() =>
        {
            fileSecurity.RemoveAccessRule(deny);
            file.SetAccessControl(fileSecurity);
        });
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
