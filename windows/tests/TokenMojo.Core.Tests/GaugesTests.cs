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
        // Act
        double? percent = Gauges.LivePercent(new BridgeLimit(23.5, Future), Now);

        // Assert
        Assert.Equal(23.5, percent);
    }

    [Fact]
    public void LivePercent_WindowAlreadyReset_ReturnsNull()
    {
        // The bridge only refreshes on the next interaction, so the stored value is stale.
        // Act
        double? percent = Gauges.LivePercent(new BridgeLimit(23.5, Past), Now);

        // Assert
        Assert.Null(percent);
    }

    [Fact]
    public void LivePercent_NoResetTime_ReturnsUsedPercentage()
    {
        // Act
        double? percent = Gauges.LivePercent(new BridgeLimit(23.5, null), Now);

        // Assert
        Assert.Equal(23.5, percent);
    }

    [Fact]
    public void LivePercent_NoLimit_ReturnsNull()
    {
        // Act
        double? percent = Gauges.LivePercent(null, Now);

        // Assert
        Assert.Null(percent);
    }

    [Fact]
    public void LivePercent_ResetTimeBeyondDateRange_TreatsWindowAsOpen()
    {
        // Arrange
        BridgeLimit limit = new(23.5, 1.79e15);

        // Act
        double? percent = Gauges.LivePercent(limit, Now);

        // Assert
        Assert.Equal(23.5, percent);
    }

    [Fact]
    public void ResetText_ResetTimeBeyondDateRange_DoesNotThrow()
    {
        // Arrange
        BridgeLimit limit = new(23.5, 1.79e15);

        // Act
        string text = Gauges.ResetText(limit, Now);

        // Assert
        Assert.StartsWith("Resets ", text);
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
        // Act
        Severity severity = Gauges.SeverityOf(percent);

        // Assert
        Assert.Equal(expected, severity);
    }

    [Theory]
    [InlineData(63.9, "63%")]
    [InlineData(0.0, "0%")]
    [InlineData(null, "–")]
    public void PercentText_TruncatesToWholePercent(double? percent, string expected)
    {
        // Act
        string text = Gauges.PercentText(percent);

        // Assert
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(63.9, "63")]
    [InlineData(99.9, "99")]
    [InlineData(100.0, "!")]
    [InlineData(null, "–")]
    public void IconText_FitsTwoCharactersInTheTrayIcon(double? percent, string expected)
    {
        // Act
        string text = Gauges.IconText(percent);

        // Assert
        Assert.Equal(expected, text);
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
        // Act
        string text = Gauges.CostText(null, 2);

        // Assert
        Assert.Equal("$0.00", text);
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
        // Act
        string text = Gauges.ResetText(null, Now);

        // Assert
        Assert.Equal("n/a", text);
    }

    [Fact]
    public void ResetText_PercentWithoutResetTime_ShowsBlank()
    {
        // Act
        string text = Gauges.ResetText(new BridgeLimit(10, null), Now);

        // Assert
        Assert.Equal(" ", text);
    }

    [Fact]
    public void ResetText_WindowAlreadyReset_SaysAwaitingUpdate()
    {
        // Act
        string text = Gauges.ResetText(new BridgeLimit(10, Past), Now);

        // Assert
        Assert.Equal("Reset — awaiting update", text);
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
        // Act
        string folder = Gauges.FolderName(directory);

        // Assert
        Assert.Equal(expected, folder);
    }
}
