using System.Text.Json;

namespace TokenMojo.Core;

/// <summary>The statusline payload the bridge script writes to cctokmon-state.json.</summary>
public sealed record BridgeState(
    string? SessionId,
    string? SessionName,
    BridgeModel? Model,
    BridgeWorkspace? Workspace,
    BridgeCost? Cost,
    BridgeContextWindow? ContextWindow,
    BridgeRateLimits? RateLimits)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static BridgeState Parse(string json) =>
        JsonSerializer.Deserialize<BridgeState>(json, Options) ?? throw new JsonException("Bridge state is JSON null");
}

public sealed record BridgeModel(string? DisplayName);

public sealed record BridgeWorkspace(string? CurrentDir);

public sealed record BridgeCost(double? TotalCostUsd, long? TotalLinesAdded, long? TotalLinesRemoved);

public sealed record BridgeContextWindow(
    long? TotalInputTokens, long? TotalOutputTokens, long? ContextWindowSize, double? UsedPercentage);

public sealed record BridgeLimit(double? UsedPercentage, double? ResetsAt);

public sealed record BridgeRateLimits(BridgeLimit? FiveHour, BridgeLimit? SevenDay);
