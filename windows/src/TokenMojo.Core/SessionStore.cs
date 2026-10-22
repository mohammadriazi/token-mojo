using System.Text.Json;

namespace TokenMojo.Core;

public sealed record SessionSnapshot(string Id, BridgeState State, DateTimeOffset Updated)
{
    public bool IsActive(DateTimeOffset now) => now - Updated < TimeSpan.FromMinutes(10);
}

public sealed record LoadedState(BridgeState State, DateTimeOffset Updated, ClaudeSource Source);

public static class SessionStore
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(12);

    /// <summary>The newest state file across sources; rate limits are per account, so the latest write wins.</summary>
    public static LoadedState? LoadCurrent(IEnumerable<ClaudeSource> sources)
    {
        (ClaudeSource source, FileInfo file) newest = sources
            .Select(source => (source, file: new FileInfo(source.StateFile)))
            .Where(candidate => candidate.file.Exists)
            .OrderByDescending(candidate => candidate.file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (newest.file is null) return null;
        return new LoadedState(
            BridgeState.Parse(File.ReadAllText(newest.file.FullName)),
            new DateTimeOffset(newest.file.LastWriteTimeUtc),
            newest.source);
    }

    // Claude Code signals no session end, so "recent" is the best proxy for "open".
    public static IReadOnlyList<SessionSnapshot> LoadSessions(IEnumerable<ClaudeSource> sources, DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - RecentWindow;
        List<SessionSnapshot> sessions = new();
        foreach (ClaudeSource source in sources)
        {
            DirectoryInfo dir = new(source.SessionsDir);
            // The directory appears after the first bridge write.
            if (!dir.Exists) continue;
            foreach (FileInfo file in dir.EnumerateFiles("*.json"))
            {
                DateTimeOffset updated = new(file.LastWriteTimeUtc);
                if (updated <= cutoff) continue;
                if (TryRead(file) is { } state)
                    sessions.Add(new SessionSnapshot(Path.GetFileNameWithoutExtension(file.Name), state, updated));
            }
        }
        return sessions.OrderByDescending(s => s.Updated).ToList();
    }

    private static BridgeState? TryRead(FileInfo file)
    {
        try
        {
            return BridgeState.Parse(File.ReadAllText(file.FullName));
        }
        // Writes are atomic renames, so an undecodable file is foreign, and one that vanished was pruned.
        catch (Exception e) when (e is JsonException or FileNotFoundException)
        {
            return null;
        }
    }
}
