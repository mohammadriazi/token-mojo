using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TokenMojo;

/// <summary>The dashboard that opens from the tray icon, standing in for the Mac app's menu bar dropdown.</summary>
public partial class FlyoutWindow : Window
{
    private const int EdgeMargin = 12;
    private const uint NoSizeNoZOrderNoActivate = 0x0001 | 0x0004 | 0x0010;
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

    // Clamped into the cursor's work area, so it sits beside the taskbar wherever that is docked.
    private void PlaceNearCursor()
    {
        UpdateLayout();
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
        System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
        int margin = (int)(EdgeMargin * dpi.DpiScaleX);
        int x = Math.Clamp(cursor.X - width / 2, area.Left + margin, Math.Max(area.Left + margin, area.Right - width - margin));
        int y = Math.Clamp(cursor.Y - height - margin, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - height - margin));
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0, NoSizeNoZOrderNoActivate);
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        Hide();
        _hiddenAt = DateTime.UtcNow;
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
