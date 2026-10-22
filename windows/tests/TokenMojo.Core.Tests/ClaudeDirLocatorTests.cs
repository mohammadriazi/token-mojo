using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class ClaudeDirLocatorTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Locate_NoWslRunning_ReturnsOnlyWindowsHome()
    {
        // Arrange
        string home = _temp.MakeDir("winhome");
        ClaudeDirLocator locator = new(home, new FakeWsl(), NoEnv);

        // Act / Assert
        Assert.Equal(
            [new ClaudeSource("Windows",
                Path.Combine(home, ".claude", "cctokmon-state.json"),
                Path.Combine(home, ".claude", "cctokmon-sessions"))],
            locator.Locate());
    }

    [Fact]
    public void Locate_EnvOverrides_ReplaceWindowsDefaults()
    {
        // Arrange
        Dictionary<string, string> env = new()
        {
            ["CCTOKMON_STATE_FILE"] = "/custom/state.json",
            ["CCTOKMON_SESSIONS_DIR"] = "/custom/sessions",
        };
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl(), env.GetValueOrDefault);

        // Act / Assert
        Assert.Equal([new ClaudeSource("Windows", "/custom/state.json", "/custom/sessions")], locator.Locate());
    }

    [Fact]
    public void Locate_RunningDistro_AddsEveryHomeThatHasAClaudeDir()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        string alice = _temp.MakeDir("share/Ubuntu/home/alice/.claude");
        _temp.MakeDir("share/Ubuntu/home/bob");
        string root = _temp.MakeDir("share/Ubuntu/root/.claude");
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), NoEnv, share);

        // Act
        List<ClaudeSource> wslSources = locator.Locate().Skip(1).ToList();

        // Assert
        Assert.Equal(
            [
                new ClaudeSource("Ubuntu:alice", Path.Combine(alice, "cctokmon-state.json"), Path.Combine(alice, "cctokmon-sessions")),
                new ClaudeSource("Ubuntu:root", Path.Combine(root, "cctokmon-state.json"), Path.Combine(root, "cctokmon-sessions")),
            ],
            wslSources);
    }

    [Fact]
    public void Locate_DistroShareUnreachable_IsSkipped()
    {
        // Arrange
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Gone"), NoEnv, _temp.MakeDir("share"));

        // Act / Assert
        Assert.Single(locator.Locate());
    }

    [Fact]
    public void Locate_UnreadableDistroHome_SkipsThatDistroOnly()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        string blocked = _temp.MakeDir("share/Broken/home");
        _temp.MakeDir("share/Ubuntu/home/alice/.claude");
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Broken", "Ubuntu"), NoEnv, share);
        using IDisposable denied = TempDir.DenyRead(blocked);

        // Act
        IReadOnlyList<ClaudeSource> sources = locator.Locate();

        // Assert
        Assert.Equal(["Windows", "Ubuntu:alice"], sources.Select(s => s.Name));
    }

    private static string? NoEnv(string name) => null;

    private sealed class FakeWsl(params string[] running) : IWslDistros
    {
        public IReadOnlyList<string> Running() => running;
    }
}
