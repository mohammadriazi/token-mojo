using System.ComponentModel;
using System.Windows;

namespace TokenMojo;

/// <summary>The always-on-top dashboard window, the Mac app's floating panel.</summary>
public partial class PopOutWindow : Window
{
    public PopOutWindow()
    {
        InitializeComponent();
        if (PopOutPlacement.Load() is { } position)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = position.X;
            Top = position.Y;
        }
        WindowChrome.Apply(this, round: false);
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
        PopOutPlacement.Save(new Point(Left, Top));
        Hide();
    }
}
