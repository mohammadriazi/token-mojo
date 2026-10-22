using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace TokenMojo;

/// <summary>The always-on-top dashboard window, the Mac app's floating panel.</summary>
public partial class PopOutWindow : Window
{
    public PopOutWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this, round: false);
        if (PopOutPlacement.Load() is { } position)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, position.X, position.Y, 0, 0,
                Native.NoSizeNoZOrderNoActivate);
        }
    }

    public DashboardView View => Dashboard;

    public void ShowFront()
    {
        Show();
        Activate();
    }

    // Closing only hides it, so the next Pop Out reopens it where it was.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        PopOutPlacement.Save(Native.TopLeft(new WindowInteropHelper(this).Handle));
        Hide();
    }
}
