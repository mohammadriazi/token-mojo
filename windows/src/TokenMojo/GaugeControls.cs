using System.Windows;
using System.Windows.Media;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>The circular gauge from LimitRing in the Mac app; also draws the tray icon.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(Severity), typeof(RingGauge), new FrameworkPropertyMetadata(Severity.Ok, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }

    public Severity Severity { get => (Severity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public static void Draw(DrawingContext context, Point center, double radius, double thickness, double percent, Brush track, Brush arc)
    {
        context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        if (percent <= 0) return;
        Pen pen = new(arc, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (percent >= 100)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        // Clockwise from twelve o'clock, like the Mac ring.
        double angle = 2 * Math.PI * percent / 100;
        Point start = new(center.X, center.Y - radius);
        Point end = new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        StreamGeometry geometry = new();
        using (StreamGeometryContext figure = geometry.Open())
        {
            figure.BeginFigure(start, isFilled: false, isClosed: false);
            figure.ArcTo(end, new Size(radius, radius), 0, isLargeArc: percent > 50, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        Color color = Theme.SeverityBrush(Severity).Color;
        LinearGradientBrush arc = new(Color.FromArgb(0xB3, color.R, color.G, color.B), color, 90);
        Draw(drawingContext, new Point(ActualWidth / 2, ActualHeight / 2), (size - Thickness) / 2, Thickness,
            Math.Min(Percent, 100), TrackBrush, arc);
    }
}

/// <summary>The rounded progress bar from CapsuleBar in the Mac app.</summary>
public sealed class CapsuleBar : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(CapsuleBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(Severity), typeof(CapsuleBar), new FrameworkPropertyMetadata(Severity.Ok, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(CapsuleBar), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }

    public Severity Severity { get => (Severity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double radius = ActualHeight / 2;
        drawingContext.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
        double width = ActualWidth * Math.Clamp(Percent, 0, 100) / 100;
        if (width <= 0) return;
        LinearGradientBrush fill = new(Theme.Teal.Color, Theme.SeverityBrush(Severity).Color, 0);
        drawingContext.DrawRoundedRectangle(fill, null, new Rect(0, 0, width, ActualHeight), radius, radius);
    }
}
