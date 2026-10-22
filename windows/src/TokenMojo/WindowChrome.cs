using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TokenMojo;

/// <summary>Windows 11 dark title bars and rounded corners; older builds ignore the attributes.</summary>
internal static partial class WindowChrome
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int RoundCorners = 2;

    public static void Apply(Window window, bool round)
    {
        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        int dark = Theme.AppsUseLightTheme ? 0 : 1;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int));
        if (round)
        {
            int corners = RoundCorners;
            _ = DwmSetWindowAttribute(handle, WindowCornerPreference, ref corners, sizeof(int));
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
