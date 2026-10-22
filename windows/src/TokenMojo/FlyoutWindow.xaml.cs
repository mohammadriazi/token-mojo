using System.Windows;
using System.Windows.Interop;

namespace TokenMojo;

/// <summary>The dashboard that opens from the tray icon, standing in for the Mac app's menu bar dropdown.</summary>
public partial class FlyoutWindow : Window
{
    private const int EdgeMargin = 12;
    private const int OffScreen = -32000;
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);
    private DateTime _hiddenAt = DateTime.MinValue;

    public FlyoutWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this, round: true);
        Deactivated += (_, _) => HideFlyout();
    }

    public event EventHandler? PopOutRequested;

    public DashboardView View => Dashboard;

    public void Toggle()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }
        // Clicking the tray icon while open deactivates (hides) the flyout first; don't reopen it.
        if (DateTime.UtcNow - _hiddenAt < ReopenGuard) return;
        LaunchAtLoginCheck.Visibility = StartupRegistration.IsEnabled ? Visibility.Visible : Visibility.Hidden;
        Show();
        PlaceNearCursor();
        Activate();
    }

    // Clamped into the cursor's work area, so it sits beside the taskbar wherever that is docked. The scale
    // is the cursor's monitor's, which the flyout adopts once it moves there.
    private void PlaceNearCursor()
    {
        UpdateLayout();
        System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
        System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
        double scale = Native.ScaleAt(cursor);
        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);
        int margin = (int)(EdgeMargin * scale);
        int x = Math.Clamp(cursor.X - width / 2, area.Left + margin, Math.Max(area.Left + margin, area.Right - width - margin));
        int y = Math.Clamp(cursor.Y - height - margin, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - height - margin));
        Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Native.NoSizeNoZOrderNoActivate);
    }

    // Parked off-screen while hidden, so the next Show doesn't flash at the old spot before it is placed.
    private void HideFlyout()
    {
        if (!IsVisible) return;
        Hide();
        Native.SetWindowPos(Handle, IntPtr.Zero, OffScreen, OffScreen, 0, 0, Native.NoSizeNoZOrderNoActivate);
        _hiddenAt = DateTime.UtcNow;
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void OnPopOut(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        PopOutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnToggleLaunchAtLogin(object sender, RoutedEventArgs e)
    {
        StartupRegistration.Toggle();
        LaunchAtLoginCheck.Visibility = StartupRegistration.IsEnabled ? Visibility.Visible : Visibility.Hidden;
    }

    private void OnQuit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
