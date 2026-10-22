using System.IO;
using System.Text.Json;
using System.Windows;

namespace TokenMojo;

/// <summary>Remembers where the pop-out was left, as setFrameAutosaveName does on the Mac.</summary>
internal static class PopOutPlacement
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenMojo", "popout.json");

    public static Point? Load()
    {
        try
        {
            Saved? saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
            if (saved is null) return null;
            Rect desktop = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            // A monitor may have been unplugged since.
            return desktop.Contains(new Point(saved.Left, saved.Top)) ? new Point(saved.Left, saved.Top) : null;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    public static void Save(Point position)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(position.X, position.Y)));
    }

    private sealed record Saved(double Left, double Top);
}
