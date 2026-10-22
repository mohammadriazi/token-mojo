using Microsoft.Win32;

namespace TokenMojo;

/// <summary>Launch at Login, through the per-user Run key (the Windows counterpart of SMAppService).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TokenMojo";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            // A stale path from a moved build counts as off, so toggling repairs it.
            return key?.GetValue(ValueName) as string == Command;
        }
    }

    public static void Toggle()
    {
        bool enable = !IsEnabled;
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
