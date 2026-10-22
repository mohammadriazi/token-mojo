using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void LoadSessions_SeveralRecentSessions_ReturnsNewestFirst()
    {
        // Arrange
        ClaudeSource source = _temp.Source("win");
        _temp.Write("win/cctokmon-sessions/older.json", Payloads.ForSession("older"), Now.AddHours(-2));
        _temp.Write("win/cctokmon-sessions/newer.json", Payloads.ForSession("newer"), Now.AddMinutes(-1));

        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([source], Now);

        // Assert
        Assert.Equal(["newer", "older"], sessions.Select(s => s.Id));
    }

    [Fact]
    public void LoadSessions_SessionOlderThanTwelveHours_IsLeftOut()
    {
        // Arrange
        ClaudeSource source = _temp.Source("win");
        _temp.Write("win/cctokmon-sessions/stale.json", Payloads.ForSession("stale"), Now.AddHours(-13));

        // Act / Assert
        Assert.Empty(SessionStore.LoadSessions([source], Now));
    }

    [Fact]
    public void LoadSessions_UndecodableAndNonJsonFiles_AreSkipped()
    {
        // Arrange
        ClaudeSource source = _temp.Source("win");
        _temp.Write("win/cctokmon-sessions/garbage.json", "not json", Now);
        _temp.Write("win/cctokmon-sessions/inflight.json.tmp", Payloads.ForSession("inflight"), Now);
        _temp.Write("win/cctokmon-sessions/good.json", Payloads.ForSession("good"), Now);

        // Act / Assert
        Assert.Equal(["good"], SessionStore.LoadSessions([source], Now).Select(s => s.Id));
    }

    [Fact]
    public void LoadSessions_SessionsDirNotCreatedYet_ReturnsEmpty()
    {
        Assert.Empty(SessionStore.LoadSessions([_temp.Source("missing")], Now));
    }

    [Fact]
    public void LoadSessions_SessionsFromTwoSources_AreMerged()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now.AddMinutes(-5));
        _temp.Write("wsl/cctokmon-sessions/b.json", Payloads.ForSession("b"), Now.AddMinutes(-1));

        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([_temp.Source("win"), _temp.Source("wsl")], Now);

        // Assert
        Assert.Equal(["b", "a"], sessions.Select(s => s.Id));
    }

    [Fact]
    public void LoadCurrent_StateFilesInTwoSources_PicksMostRecentlyWritten()
    {
        // Arrange
        _temp.Write("win/cctokmon-state.json", Payloads.ForSession("from-windows"), Now.AddMinutes(-30));
        _temp.Write("wsl/cctokmon-state.json", Payloads.ForSession("from-wsl"), Now.AddMinutes(-1));

        // Act
        LoadedState? current = SessionStore.LoadCurrent([_temp.Source("win"), _temp.Source("wsl")]);

        // Assert
        Assert.Equal("from-wsl", current?.State.SessionId);
        Assert.Equal("wsl", current?.Source.Name);
        Assert.Equal(Now.AddMinutes(-1), current?.Updated);
    }

    [Fact]
    public void LoadCurrent_NoStateFileAnywhere_ReturnsNull()
    {
        Assert.Null(SessionStore.LoadCurrent([_temp.Source("win"), _temp.Source("wsl")]));
    }

    [Theory]
    [InlineData(-9, true)]
    [InlineData(-11, false)]
    public void IsActive_UpdatedWithinTenMinutes_IsActive(int minutesAgo, bool expected)
    {
        // Arrange
        SessionSnapshot session = new("s", BridgeState.Parse("{}"), Now.AddMinutes(minutesAgo));

        // Act / Assert
        Assert.Equal(expected, session.IsActive(Now));
    }
}
