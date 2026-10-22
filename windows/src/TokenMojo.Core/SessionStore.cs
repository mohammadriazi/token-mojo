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
            BridgeState.Parse(ReadShared(newest.file.FullName)),
            new DateTimeOffset(newest.file.LastWriteTimeUtc),
            newest.source);
    }

    // Claude Code signals no session end, so "recent" is the best proxy for "open".
    public static IReadOnlyList<SessionSnapshot> LoadSessions(IEnumerable<ClaudeSource> sources, DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - RecentWindow;
        List<SessionSnapshot> sessions = new();
        foreach (ClaudeSource source in sources)
            sessions.AddRange(LoadSource(source, cutoff));
        return sessions.OrderByDescending(s => s.Updated).ToList();
    }

    private static List<SessionSnapshot> LoadSource(ClaudeSource source, DateTimeOffset cutoff)
    {
        List<SessionSnapshot> sessions = new();
        DirectoryInfo dir = new(source.SessionsDir);
        // The directory appears after the first bridge write.
        if (!dir.Exists) return sessions;
        try
        {
            foreach (FileInfo file in dir.EnumerateFiles("*.json"))
            {
                DateTimeOffset updated = new(file.LastWriteTimeUtc);
                if (updated <= cutoff) continue;
                if (TryRead(file) is { } state)
                    sessions.Add(new SessionSnapshot(Path.GetFileNameWithoutExtension(file.Name), state, updated));
            }
        }
        // One unreadable source (a WSL distro that stopped mid-read) must not hide the others.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return sessions;
    }

    private static BridgeState? TryRead(FileInfo file)
    {
        try
        {
            return BridgeState.Parse(ReadShared(file.FullName));
        }
        // Writes are atomic renames, so an undecodable file is foreign; one that vanished or is locked
        // is caught mid-replace and is read again on the next refresh.
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // FileShare.Delete lets the bridge rename a new file over this one while it is being read on Windows.
    private static string ReadShared(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
