using System.Globalization;

namespace TokenMojo.Core;

public enum Severity { Ok, Warning, Critical }

public sealed record GlanceGauge(string Label, double? Percent);

/// <summary>Display rules shared with the macOS app (menubar/TokenMojo.swift); keep the two in step.</summary>
public static class Gauges
{
    // Keeps ToLocalTime in range for any time zone offset.
    private static readonly DateTimeOffset LatestReset = DateTimeOffset.MaxValue.AddDays(-2);
    private static readonly DateTimeOffset EarliestReset = DateTimeOffset.MinValue.AddDays(2);

    public static IReadOnlyList<GlanceGauge> Glance(BridgeState state, DateTimeOffset now)
    {
        List<GlanceGauge> gauges = new();
        // API-key users get no rate limits, so only the context gauge shows for them.
        if (state.RateLimits is { } limits)
        {
            gauges.Add(new GlanceGauge("5h", LivePercent(limits.FiveHour, now)));
            gauges.Add(new GlanceGauge("7d", LivePercent(limits.SevenDay, now)));
        }
        gauges.Add(new GlanceGauge("ctx", state.ContextWindow?.UsedPercentage));
        return gauges;
    }

    public static GlanceGauge Worst(IReadOnlyList<GlanceGauge> gauges) => gauges.MaxBy(g => g.Percent ?? -1)!;

    public static string Summary(IReadOnlyList<GlanceGauge> gauges, BridgeState state) =>
        string.Join(" · ", gauges.Select(g => $"{g.Label} {PercentText(g.Percent)}").Append(CostText(state.Cost?.TotalCostUsd, 2)));

    // Null once the window has reset: the bridge only refreshes on the next Claude Code interaction.
    public static double? LivePercent(BridgeLimit? limit, DateTimeOffset now)
    {
        if (limit?.UsedPercentage is not { } percent) return null;
        if (limit.ResetsAt is { } resetsAt && ResetTime(resetsAt) < now) return null;
        return percent;
    }

    public static Severity SeverityOf(double percent) =>
        percent >= 80 ? Severity.Critical : percent >= 50 ? Severity.Warning : Severity.Ok;

    public static string PercentText(double? percent) => percent is { } p ? $"{(int)p}%" : "–";

    // The tray icon has room for two characters inside its ring.
    public static string IconText(double? percent) => percent switch
    {
        null => "–",
        >= 100 => "!",
        { } p => ((int)p).ToString(CultureInfo.InvariantCulture),
    };

    public static string CostText(double? usd, int decimals) =>
        "$" + (usd ?? 0).ToString("F" + decimals, CultureInfo.InvariantCulture);

    public static string ResetText(BridgeLimit? limit, DateTimeOffset now)
    {
        if (limit?.ResetsAt is not { } resetsAt) return limit?.UsedPercentage is null ? "n/a" : " ";
        DateTimeOffset reset = ResetTime(resetsAt);
        if (reset < now) return "Reset — awaiting update";
        DateTimeOffset local = reset.ToLocalTime();
        return reset - now > TimeSpan.FromDays(1) ? $"Resets {local:ddd} {local:t}" : $"Resets {local:t}";
    }

    // Sessions come from Windows and WSL, so split on both separators.
    public static string FolderName(string directory) =>
        directory.TrimEnd('/', '\\').Split('/', '\\')[^1];

    // Never throws, like Swift's Date(timeIntervalSince1970:): a value outside the calendar clamps to its ends.
    private static DateTimeOffset ResetTime(double unixSeconds) =>
        unixSeconds >= LatestReset.ToUnixTimeSeconds() ? LatestReset
        : unixSeconds <= EarliestReset.ToUnixTimeSeconds() ? EarliestReset
        : DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds);
}
