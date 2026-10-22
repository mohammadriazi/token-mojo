using System.Globalization;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class DashboardTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly double Future = Now.AddHours(2).ToUnixTimeSeconds();
    private static readonly ClaudeSource Source = new("Windows", "state.json", "sessions");
    private const string WindowsHome = @"C:\Users\alice";
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public DashboardTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    [Fact]
    public void Build_SubscriberState_ShowsFiveHourAndSevenDayRings()
    {
        // Arrange
        BridgeState state = Subscriber(fiveHour: 23.5, sevenDay: 41.2);

        // Act
        IReadOnlyList<LimitRingModel> limits = Build(state).Limits;

        // Assert
        Assert.Equal(["5-hour limit", "7-day limit"], limits.Select(l => l.Title));
        Assert.Equal(["23%", "41%"], limits.Select(l => l.PercentText));
    }

    [Fact]
    public void Build_ApiKeyState_ShowsNoRings()
    {
        Assert.Empty(Build(BridgeState.Parse(Payloads.Full) with { RateLimits = null }).Limits);
    }

    [Fact]
    public void Build_LimitOverOneHundred_CapsRingButShowsRealPercent()
    {
        // Act
        LimitRingModel ring = Build(Subscriber(fiveHour: 120, sevenDay: 10)).Limits[0];

        // Assert
        Assert.Equal(100, ring.RingPercent);
        Assert.Equal("120%", ring.PercentText);
        Assert.Equal(Severity.Critical, ring.Severity);
    }

    [Fact]
    public void Build_ExpiredLimit_ShowsEmptyRingAwaitingUpdate()
    {
        // Arrange
        BridgeState state = Subscriber(fiveHour: 90, sevenDay: 10) with
        {
            RateLimits = new BridgeRateLimits(new BridgeLimit(90, Now.AddMinutes(-1).ToUnixTimeSeconds()), null),
        };

        // Act
        LimitRingModel ring = Build(state).Limits[0];

        // Assert
        Assert.Equal(new LimitRingModel("5-hour limit", 0, "–", Severity.Ok, "Reset — awaiting update"), ring);
    }

    [Theory]
    [InlineData(200000L, "8% of 200k")]
    [InlineData(1000000L, "8% of 1M")]
    [InlineData(null, "8% of ?")]
    public void Build_ContextWindow_DescribesUsageAndSize(long? size, string expected)
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with { ContextWindow = new BridgeContextWindow(0, 0, size, 8) };

        // Act / Assert
        Assert.Equal(expected, Build(state).Context?.Text);
    }

    [Fact]
    public void Build_NoContextWindow_HidesContextCard()
    {
        Assert.Null(Build(BridgeState.Parse("{}")).Context);
    }

    [Fact]
    public void Build_FullPayload_FormatsStatTiles()
    {
        // Act
        DashboardModel model = Build(BridgeState.Parse(Payloads.Full));

        // Assert
        Assert.Equal("$1.2345", model.CostText);
        Assert.Equal("+10 / −2", model.LinesText);
        Assert.Equal("1,000", model.InputTokensText);
        Assert.Equal("200", model.OutputTokensText);
    }

    [Fact]
    public void Build_EmptyPayload_FallsBackToClaudeCodeAndZeros()
    {
        // Act
        DashboardModel model = Build(BridgeState.Parse("{}"));

        // Assert
        Assert.Equal("Claude Code", model.ModelName);
        Assert.Null(model.SessionName);
        Assert.Null(model.Directory);
        Assert.Equal("$0.0000", model.CostText);
        Assert.Equal("+0 / −0", model.LinesText);
    }

    [Theory]
    [InlineData(@"C:\Users\alice\proj", @"~\proj")]
    [InlineData(@"C:\Users\alicesmith\proj", @"C:\Users\alicesmith\proj")]
    [InlineData("/home/bob/proj", "~/proj")]
    [InlineData("/root/proj", "~/proj")]
    [InlineData("/srv/proj", "/srv/proj")]
    public void Build_Directory_AbbreviatesHomeWithTilde(string directory, string expected)
    {
        // Arrange
        BridgeState state = BridgeState.Parse("{}") with { Workspace = new BridgeWorkspace(directory) };

        // Act / Assert
        Assert.Equal(expected, Build(state).Directory);
    }

    [Fact]
    public void Build_StateWrittenSecondsAgo_SaysWhenUpdated()
    {
        // Act
        DashboardModel model = Dashboard.Build(new LoadedState(BridgeState.Parse("{}"), Now.AddSeconds(-3), Source), [], Now, WindowsHome);

        // Assert
        Assert.Equal("Updated 3 seconds ago", model.UpdatedText);
    }

    [Fact]
    public void Build_NoRecentSessions_HidesSessionList()
    {
        Assert.Null(Build(BridgeState.Parse(Payloads.Full)).Sessions);
    }

    [Fact]
    public void Build_RecentSessions_SummarisesCountAndTotalCost()
    {
        // Arrange
        SessionSnapshot[] sessions = [Session("a", 1.25, minutesAgo: 1), Session("b", 2.25, minutesAgo: 30)];

        // Act / Assert
        Assert.Equal("2 · $3.50 total", Build(BridgeState.Parse(Payloads.Full), sessions).Sessions?.Summary);
    }

    [Fact]
    public void Build_MoreThanTenSessions_ListsOnlyTheFirstTen()
    {
        // Arrange
        List<SessionSnapshot> sessions = Enumerable.Range(0, 12).Select(i => Session($"s{i}", 1, minutesAgo: i)).ToList();

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions).Sessions!;

        // Assert
        Assert.Equal(10, list.Rows.Count);
        Assert.Equal("12 · $12.00 total", list.Summary);
    }

    [Fact]
    public void Build_SessionRow_DescribesFolderModelAgeAndContext()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with { SessionName = null };
        SessionSnapshot session = new("abc-123", state, Now.AddMinutes(-5));

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal(
            new SessionRowModel("proj", "proj · Opus · 5 min. ago", "$1.23", 8, "8%", Severity.Ok,
                IsActive: true, IsCurrent: true),
            row);
    }

    [Fact]
    public void Build_SessionWithoutNameOrFolder_IsTitledSession()
    {
        // Arrange
        SessionSnapshot session = new("x", BridgeState.Parse("{}"), Now.AddMinutes(-20));

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal("Session", row.Title);
        Assert.Equal("20 min. ago", row.Detail);
        Assert.False(row.IsActive);
        Assert.False(row.IsCurrent);
    }

    [Theory]
    [InlineData(0, false, "now")]
    [InlineData(1, false, "1 second ago")]
    [InlineData(45, false, "45 seconds ago")]
    [InlineData(60, false, "1 minute ago")]
    [InlineData(7200, false, "2 hours ago")]
    [InlineData(172800, false, "2 days ago")]
    [InlineData(45, true, "45 sec. ago")]
    [InlineData(300, true, "5 min. ago")]
    [InlineData(3600, true, "1 hr. ago")]
    [InlineData(86400, true, "1 day ago")]
    public void RelativeAge_MatchesMacFormatterWording(int seconds, bool abbreviated, string expected)
    {
        Assert.Equal(expected, Dashboard.RelativeAge(TimeSpan.FromSeconds(seconds), abbreviated));
    }

    private static DashboardModel Build(BridgeState state, IReadOnlyList<SessionSnapshot>? sessions = null) =>
        Dashboard.Build(new LoadedState(state, Now, Source), sessions ?? [], Now, WindowsHome);

    private static BridgeState Subscriber(double fiveHour, double sevenDay) =>
        BridgeState.Parse(Payloads.Full) with
        {
            RateLimits = new BridgeRateLimits(new BridgeLimit(fiveHour, Future), new BridgeLimit(sevenDay, Future)),
        };

    private static SessionSnapshot Session(string id, double cost, int minutesAgo) =>
        new(id, BridgeState.Parse(Payloads.ForSession(id, cost)), Now.AddMinutes(-minutesAgo));
}
