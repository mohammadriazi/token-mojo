using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>Light and dark brushes following the Windows app and taskbar themes, as the Mac app follows its appearance.</summary>
internal static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static readonly SolidColorBrush Teal = Frozen(Color.FromRgb(0x30, 0xB0, 0xC7));

    private static readonly SolidColorBrush Ok = Frozen(Color.FromRgb(0x34, 0xC7, 0x59));
    private static readonly SolidColorBrush Warning = Frozen(Color.FromRgb(0xFF, 0x95, 0x00));
    private static readonly SolidColorBrush Critical = Frozen(Color.FromRgb(0xFF, 0x3B, 0x30));

    public static bool AppsUseLightTheme => ReadPersonalize("AppsUseLightTheme");

    public static bool TaskbarUsesLightTheme => ReadPersonalize("SystemUsesLightTheme");

    public static SolidColorBrush SeverityBrush(Severity severity) => severity switch
    {
        Severity.Critical => Critical,
        Severity.Warning => Warning,
        _ => Ok,
    };

    public static void Apply(ResourceDictionary resources)
    {
        bool light = AppsUseLightTheme;
        Color ink = light ? Colors.Black : Colors.White;
        resources["WindowBrush"] = Frozen(light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20));
        resources["WindowBorderBrush"] = Frozen(WithAlpha(ink, 0x24));
        resources["CardBrush"] = Frozen(WithAlpha(ink, 0x0D));
        resources["HoverBrush"] = Frozen(WithAlpha(ink, 0x14));
        resources["TrackBrush"] = Frozen(WithAlpha(ink, 0x1A));
        resources["PrimaryTextBrush"] = Frozen(light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White);
        resources["SecondaryTextBrush"] = Frozen(WithAlpha(ink, 0x99));
        resources["IdleDotBrush"] = Frozen(WithAlpha(ink, 0x66));
        resources["CurrentRowBrush"] = Frozen(WithAlpha(SystemParameters.WindowGlassColor, 0x26));
        resources["OkBrush"] = Ok;
    }

    private static bool ReadPersonalize(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        // Absent on Windows builds before the dark theme existed, which were light.
        return key?.GetValue(valueName) is not int value || value != 0;
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static SolidColorBrush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
