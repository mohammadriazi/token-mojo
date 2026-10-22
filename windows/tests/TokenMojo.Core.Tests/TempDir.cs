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

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
