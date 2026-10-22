using System.Globalization;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public class GaugesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly double Future = Now.AddHours(2).ToUnixTimeSeconds();
    private static readonly double Past = Now.AddMinutes(-1).ToUnixTimeSeconds();

    [Fact]
    public void LivePercent_WindowNotYetReset_ReturnsUsedPercentage()
    {
        Assert.Equal(23.5, Gauges.LivePercent(new BridgeLimit(23.5, Future), Now));
    }

    [Fact]
    public void LivePercent_WindowAlreadyReset_ReturnsNull()
    {
        // The bridge only refreshes on the next interaction, so the stored value is stale.
        Assert.Null(Gauges.LivePercent(new BridgeLimit(23.5, Past), Now));
    }

    [Fact]
    public void LivePercent_NoResetTime_ReturnsUsedPercentage()
    {
        Assert.Equal(23.5, Gauges.LivePercent(new BridgeLimit(23.5, null), Now));
    }

    [Fact]
    public void LivePercent_NoLimit_ReturnsNull()
    {
        Assert.Null(Gauges.LivePercent(null, Now));
    }

    [Theory]
    [InlineData(0, Severity.Ok)]
    [InlineData(49.9, Severity.Ok)]
    [InlineData(50, Severity.Warning)]
    [InlineData(79.9, Severity.Warning)]
    [InlineData(80, Severity.Critical)]
    [InlineData(120, Severity.Critical)]
    public void SeverityOf_Percent_MatchesMacThresholds(double percent, Severity expected)
    {
        Assert.Equal(expected, Gauges.SeverityOf(percent));
    }

    [Theory]
    [InlineData(63.9, "63%")]
    [InlineData(0.0, "0%")]
    [InlineData(null, "–")]
    public void PercentText_TruncatesToWholePercent(double? percent, string expected)
    {
        Assert.Equal(expected, Gauges.PercentText(percent));
    }

    [Theory]
    [InlineData(63.9, "63")]
    [InlineData(99.9, "99")]
    [InlineData(100.0, "!")]
    [InlineData(null, "–")]
    public void IconText_FitsTwoCharactersInTheTrayIcon(double? percent, string expected)
    {
        Assert.Equal(expected, Gauges.IconText(percent));
    }

    [Fact]
    public void CostText_CommaDecimalCulture_StillUsesDot()
    {
        // Arrange
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            // Act / Assert
            Assert.Equal("$1.23", Gauges.CostText(1.23456, 2));
            Assert.Equal("$1.2346", Gauges.CostText(1.23456, 4));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void CostText_MissingCost_ShowsZero()
    {
        Assert.Equal("$0.00", Gauges.CostText(null, 2));
    }

    [Fact]
    public void Glance_SubscriberPayload_ListsFiveHourSevenDayThenContext()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with
        {
            RateLimits = new BridgeRateLimits(new BridgeLimit(23.5, Future), new BridgeLimit(41.2, Future)),
        };

        // Act
        IReadOnlyList<GlanceGauge> glance = Gauges.Glance(state, Now);

        // Assert
        Assert.Equal(
            [new GlanceGauge("5h", 23.5), new GlanceGauge("7d", 41.2), new GlanceGauge("ctx", 8)],
            glance);
    }

    [Fact]
    public void Glance_ApiKeyPayload_ListsOnlyContext()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with { RateLimits = null };

        // Act / Assert
        Assert.Equal([new GlanceGauge("ctx", 8)], Gauges.Glance(state, Now));
    }

    [Fact]
    public void Glance_ExpiredWindow_ReportsUnknownPercent()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with
        {
            RateLimits = new BridgeRateLimits(new BridgeLimit(90, Past), new BridgeLimit(41.2, Future)),
        };

        // Act / Assert
        Assert.Null(Gauges.Glance(state, Now)[0].Percent);
    }

    [Fact]
    public void Worst_MixedGauges_PicksHighestPercent()
    {
        // Arrange
        GlanceGauge[] gauges = [new("5h", 23), new("7d", null), new("ctx", 63)];

        // Act / Assert
        Assert.Equal(new GlanceGauge("ctx", 63), Gauges.Worst(gauges));
    }

    [Fact]
    public void Worst_AllUnknown_ReturnsFirstGauge()
    {
        // Arrange
        GlanceGauge[] gauges = [new("5h", null), new("ctx", null)];

        // Act / Assert
        Assert.Equal(new GlanceGauge("5h", null), Gauges.Worst(gauges));
    }

    [Fact]
    public void Summary_SubscriberGauges_JoinsGaugesAndCost()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full);
        GlanceGauge[] gauges = [new("5h", 23.5), new("7d", null), new("ctx", 8)];

        // Act / Assert
        Assert.Equal("5h 23% · 7d – · ctx 8% · $1.23", Gauges.Summary(gauges, state));
    }

    [Fact]
    public void ResetText_NoLimit_ShowsNotApplicable()
    {
        Assert.Equal("n/a", Gauges.ResetText(null, Now));
    }

    [Fact]
    public void ResetText_PercentWithoutResetTime_ShowsBlank()
    {
        Assert.Equal(" ", Gauges.ResetText(new BridgeLimit(10, null), Now));
    }

    [Fact]
    public void ResetText_WindowAlreadyReset_SaysAwaitingUpdate()
    {
        Assert.Equal("Reset — awaiting update", Gauges.ResetText(new BridgeLimit(10, Past), Now));
    }

    [Fact]
    public void ResetText_ResetWithinADay_ShowsTimeOnly()
    {
        // Arrange
        DateTimeOffset reset = DateTimeOffset.FromUnixTimeSeconds((long)Future).ToLocalTime();

        // Act / Assert
        Assert.Equal($"Resets {reset:t}", Gauges.ResetText(new BridgeLimit(10, Future), Now));
    }

    [Fact]
    public void ResetText_ResetDaysAway_ShowsWeekdayAndTime()
    {
        // Arrange
        long resetsAt = Now.AddDays(3).ToUnixTimeSeconds();
        DateTimeOffset reset = DateTimeOffset.FromUnixTimeSeconds(resetsAt).ToLocalTime();

        // Act / Assert
        Assert.Equal($"Resets {reset:ddd} {reset:t}", Gauges.ResetText(new BridgeLimit(10, resetsAt), Now));
    }

    [Theory]
    [InlineData("/home/alice/proj", "proj")]
    [InlineData("/home/alice/proj/", "proj")]
    [InlineData(@"C:\Users\alice\proj", "proj")]
    public void FolderName_WslOrWindowsPath_ReturnsLastSegment(string directory, string expected)
    {
        Assert.Equal(expected, Gauges.FolderName(directory));
    }
}
