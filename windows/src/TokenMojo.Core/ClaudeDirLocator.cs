namespace TokenMojo.Core;

public sealed record ClaudeSource(string Name, string StateFile, string SessionsDir);

public interface IWslDistros
{
    IReadOnlyList<string> Running();
}

/// <summary>Finds every ~/.claude the bridge may write to: the Windows profile and each running WSL distro.</summary>
public sealed class ClaudeDirLocator(
    string windowsHome, IWslDistros wsl, Func<string, string?> getEnv, string wslShareRoot = @"\\wsl.localhost")
{
    private const string StateFileName = "cctokmon-state.json";
    private const string SessionsDirName = "cctokmon-sessions";

    public IReadOnlyList<ClaudeSource> Locate()
    {
        string windowsClaude = Path.Combine(windowsHome, ".claude");
        List<ClaudeSource> sources = new()
        {
            new("Windows",
                getEnv("CCTOKMON_STATE_FILE") ?? Path.Combine(windowsClaude, StateFileName),
                getEnv("CCTOKMON_SESSIONS_DIR") ?? Path.Combine(windowsClaude, SessionsDirName)),
        };
        // Only running distros: reading a stopped distro's share would boot it.
        foreach (string distro in wsl.Running())
            sources.AddRange(DistroSources(distro));
        return sources;
    }

    private List<ClaudeSource> DistroSources(string distro)
    {
        string distroRoot = Path.Combine(wslShareRoot, distro);
        List<(string User, string Home)> homes = new();
        string homeDir = Path.Combine(distroRoot, "home");
        try
        {
            if (Directory.Exists(homeDir))
                homes.AddRange(Directory.EnumerateDirectories(homeDir).Order().Select(home => (Path.GetFileName(home), home)));
        }
        // The distro may stop between listing and reading, or deny access to /home.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        homes.Add(("root", Path.Combine(distroRoot, "root")));
        return homes
            .Select(entry => (entry.User, Claude: Path.Combine(entry.Home, ".claude")))
            .Where(entry => Directory.Exists(entry.Claude))
            .Select(entry => new ClaudeSource($"{distro}:{entry.User}",
                Path.Combine(entry.Claude, StateFileName), Path.Combine(entry.Claude, SessionsDirName)))
            .ToList();
    }
}
