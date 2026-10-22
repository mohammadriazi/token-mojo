using System.Runtime.InteropServices;

namespace TokenMojo;

/// <summary>The few Win32 calls WPF has no API for: exact window placement, per-monitor DPI and DWM chrome.</summary>
internal static partial class Native
{
    public const uint NoSizeNoZOrderNoActivate = 0x0001 | 0x0004 | 0x0010;
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    public static double ScaleAt(System.Drawing.Point point)
    {
        IntPtr monitor = MonitorFromPoint(new PointStruct(point.X, point.Y), MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
    }

    public static System.Drawing.Point TopLeft(IntPtr window)
    {
        GetWindowRect(window, out RectStruct rect);
        return new System.Drawing.Point(rect.Left, rect.Top);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr window, out RectStruct rect);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(PointStruct point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PointStruct(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RectStruct(int Left, int Top, int Right, int Bottom);
}
