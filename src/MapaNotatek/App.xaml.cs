using Microsoft.UI.Xaml;

namespace MapaNotatek;

public partial class App : Application
{
    public App()
    {
        Startup.Log("App ctor");
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            Startup.Fail(args.Exception?.ToString() ?? args.Message);
            args.Handled = true;
        };
    }

    public static MainWindow? MainAppWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Startup.Log("OnLaunched skipped: Window ctor crashes, hosting via AppWindow island");
    }
}
