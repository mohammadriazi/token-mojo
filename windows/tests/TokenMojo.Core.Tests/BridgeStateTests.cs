using System.Text.Json;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public class BridgeStateTests
{
    [Fact]
    public void Parse_FullStatuslinePayload_ReadsEveryField()
    {
        // Act
        BridgeState state = BridgeState.Parse(Payloads.Full);

        // Assert
        Assert.Equal("abc-123", state.SessionId);
        Assert.Equal("my-session", state.SessionName);
        Assert.Equal("Opus", state.Model?.DisplayName);
        Assert.Equal("/home/alice/proj", state.Workspace?.CurrentDir);
        Assert.Equal(new BridgeCost(1.2345, 10, 2), state.Cost);
        Assert.Equal(new BridgeContextWindow(1000, 200, 200000, 8), state.ContextWindow);
        Assert.Equal(new BridgeLimit(23.5, 1738425600), state.RateLimits?.FiveHour);
        Assert.Equal(new BridgeLimit(41.2, 1738857600), state.RateLimits?.SevenDay);
    }

    [Fact]
    public void Parse_ApiKeyPayloadWithoutRateLimits_LeavesRateLimitsNull()
    {
        // Act
        BridgeState state = BridgeState.Parse("""{"session_id":"s1","cost":{"total_cost_usd":0.5}}""");

        // Assert
        Assert.Null(state.RateLimits);
        Assert.Equal(0.5, state.Cost?.TotalCostUsd);
    }

    [Fact]
    public void Parse_NullContextPercentageEarlyInSession_LeavesItNull()
    {
        // Act
        BridgeState state = BridgeState.Parse("""{"context_window":{"used_percentage":null,"context_window_size":200000}}""");

        // Assert
        Assert.Null(state.ContextWindow?.UsedPercentage);
        Assert.Equal(200000, state.ContextWindow?.ContextWindowSize);
    }

    [Fact]
    public void Parse_GarbageText_ThrowsJsonException()
    {
        // Act / Assert
        Assert.ThrowsAny<JsonException>(() => BridgeState.Parse("not json"));
    }
}
