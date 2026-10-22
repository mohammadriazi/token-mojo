using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>Draws the worst gauge as a ring with its percentage inside, sized for the notification area.</summary>
internal static class TrayIcon
{
    public static System.Drawing.Icon Render(GlanceGauge gauge, bool lightTaskbar)
    {
        int size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
        Color ink = lightTaskbar ? Colors.Black : Colors.White;
        double percent = gauge.Percent ?? 0;
        double thickness = Math.Max(2, size / 8.0);
        Point center = new(size / 2.0, size / 2.0);

        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            RingGauge.Draw(context, center, (size - thickness) / 2, thickness, Math.Min(percent, 100),
                new SolidColorBrush(Color.FromArgb(0x59, ink.R, ink.G, ink.B)), Theme.SeverityBrush(Gauges.SeverityOf(percent)));
            FormattedText text = new(Gauges.IconText(gauge.Percent), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                size * 0.45, new SolidColorBrush(ink), pixelsPerDip: 1);
            context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return FromPng(Encode(bitmap), size);
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream png = new();
        encoder.Save(png);
        return png.ToArray();
    }

    // A one-image .ico wrapping the PNG, so the Icon owns its memory and needs no DestroyIcon.
    private static System.Drawing.Icon FromPng(byte[] png, int size)
    {
        MemoryStream ico = new();
        using (BinaryWriter writer = new(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(png.Length);
            writer.Write(6 + 16);
            writer.Write(png);
        }
        ico.Position = 0;
        return new System.Drawing.Icon(ico);
    }
}
