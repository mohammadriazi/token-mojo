using System.IO;
using System.Text.Json;

namespace TokenMojo;

/// <summary>Remembers where the pop-out was left, as setFrameAutosaveName does on the Mac.</summary>
internal static class PopOutPlacement
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenMojo", "popout.json");

    // Physical pixels, so the check below matches the monitors' own coordinates at any scale factor.
    public static System.Drawing.Point? Load()
    {
        try
        {
            Saved? saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
            if (saved is null) return null;
            System.Drawing.Point position = new(saved.Left, saved.Top);
            // A monitor may have been unplugged or moved since.
            return System.Windows.Forms.Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(position)) ? position : null;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    public static void Save(System.Drawing.Point position)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(position.X, position.Y)));
    }

    private sealed record Saved(int Left, int Top);
}
