using System.Windows;

namespace TokenMojo;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\TokenMojo", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        Theme.Apply(Resources);
        _tray = new TrayController();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
