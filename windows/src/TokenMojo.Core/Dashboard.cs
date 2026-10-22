using System.Text.RegularExpressions;

namespace TokenMojo.Core;

public sealed record DashboardModel(
    string ModelName,
    string? SessionName,
    string? Directory,
    IReadOnlyList<LimitRingModel> Limits,
    ContextModel? Context,
    string CostText,
    string LinesText,
    string InputTokensText,
    string OutputTokensText,
    SessionListModel? Sessions,
    string UpdatedText);

public sealed record LimitRingModel(string Title, double RingPercent, string PercentText, Severity Severity, string ResetText);

public sealed record ContextModel(double BarPercent, string Text, Severity Severity);

public sealed record SessionListModel(string Summary, IReadOnlyList<SessionRowModel> Rows);

public sealed record SessionRowModel(
    string Title, string Detail, string CostText, double ContextBarPercent, string ContextText,
    Severity ContextSeverity, bool IsActive, bool IsCurrent);

/// <summary>Everything the dashboard shows, formatted as DashboardPanel in menubar/TokenMojo.swift formats it.</summary>
public static class Dashboard
{
    private const int MaxSessionRows = 10;
    private static readonly Regex UnixHome = new("^(/home/[^/]+|/root)(?=/|$)");

    public static DashboardModel Build(
        LoadedState current, IReadOnlyList<SessionSnapshot> sessions, DateTimeOffset now, string windowsHome)
    {
        BridgeState state = current.State;
        LimitRingModel[] limits = state.RateLimits is { } rateLimits
            ? new[] { LimitRing("5-hour limit", rateLimits.FiveHour, now), LimitRing("7-day limit", rateLimits.SevenDay, now) }
            : [];
        return new DashboardModel(
            state.Model?.DisplayName ?? "Claude Code",
            state.SessionName,
            state.Workspace?.CurrentDir is { } dir ? AbbreviateHome(dir, windowsHome) : null,
            limits,
            state.ContextWindow is { } context ? Context(context) : null,
            Gauges.CostText(state.Cost?.TotalCostUsd, 4),
            $"+{state.Cost?.TotalLinesAdded ?? 0:N0} / −{state.Cost?.TotalLinesRemoved ?? 0:N0}",
            $"{state.ContextWindow?.TotalInputTokens ?? 0:N0}",
            $"{state.ContextWindow?.TotalOutputTokens ?? 0:N0}",
            sessions.Count > 0 ? SessionList(sessions, state.SessionId, now) : null,
            $"Updated {RelativeAge(now - current.Updated, abbreviated: false)}");
    }

    // Mirrors RelativeDateTimeFormatter's English wording, full and abbreviated.
    public static string RelativeAge(TimeSpan age, bool abbreviated)
    {
        (int count, string full, string shortUnit) = age.TotalSeconds switch
        {
            < 1 => (0, "", ""),
            < 60 => ((int)age.TotalSeconds, "second", "sec."),
            < 3600 => ((int)age.TotalMinutes, "minute", "min."),
            < 86400 => ((int)age.TotalHours, "hour", "hr."),
            _ => ((int)age.TotalDays, "day", "day"),
        };
        if (count == 0) return "now";
        string unit = abbreviated && shortUnit != "day" ? shortUnit : count == 1 ? full : full + "s";
        return $"{count} {unit} ago";
    }

    private static LimitRingModel LimitRing(string title, BridgeLimit? limit, DateTimeOffset now)
    {
        double? percent = Gauges.LivePercent(limit, now);
        return new LimitRingModel(title, Math.Min(percent ?? 0, 100), Gauges.PercentText(percent),
            Gauges.SeverityOf(percent ?? 0), Gauges.ResetText(limit, now));
    }

    private static ContextModel Context(BridgeContextWindow context)
    {
        double percent = context.UsedPercentage ?? 0;
        string size = context.ContextWindowSize switch
        {
            null => "?",
            >= 1_000_000 and long tokens => $"{tokens / 1_000_000}M",
            long tokens => $"{tokens / 1000}k",
        };
        return new ContextModel(Math.Min(percent, 100), $"{Gauges.PercentText(percent)} of {size}", Gauges.SeverityOf(percent));
    }

    private static SessionListModel SessionList(IReadOnlyList<SessionSnapshot> sessions, string? currentId, DateTimeOffset now)
    {
        double totalCost = sessions.Sum(s => s.State.Cost?.TotalCostUsd ?? 0);
        List<SessionRowModel> rows = sessions.Take(MaxSessionRows).Select(session =>
        {
            BridgeState state = session.State;
            string? folder = state.Workspace?.CurrentDir is { } dir ? Gauges.FolderName(dir) : null;
            double context = state.ContextWindow?.UsedPercentage ?? 0;
            string?[] details = [folder, state.Model?.DisplayName, RelativeAge(now - session.Updated, abbreviated: true)];
            return new SessionRowModel(
                state.SessionName ?? folder ?? "Session",
                string.Join(" · ", details.OfType<string>()),
                Gauges.CostText(state.Cost?.TotalCostUsd, 2),
                Math.Min(context, 100),
                Gauges.PercentText(context),
                Gauges.SeverityOf(context),
                session.IsActive(now),
                session.Id == currentId);
        }).ToList();
        return new SessionListModel($"{sessions.Count} · {Gauges.CostText(totalCost, 2)} total", rows);
    }

    private static string AbbreviateHome(string directory, string windowsHome)
    {
        string? rest = directory.StartsWith(windowsHome, StringComparison.OrdinalIgnoreCase) ? directory[windowsHome.Length..] : null;
        if (rest is "" || rest?[0] is '\\' or '/')
            return "~" + rest;
        return UnixHome.Replace(directory, "~");
    }
}
