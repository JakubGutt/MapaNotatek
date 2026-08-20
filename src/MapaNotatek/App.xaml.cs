using Microsoft.UI.Xaml;

namespace MapaNotatek;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        Startup.Log("App ctor");
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
        Startup.Log("OnLaunched");
        try
        {
            MainAppWindow = new MainWindow();
            _window = MainAppWindow;
            Startup.Log("Window created, activating");
            MainAppWindow.Activate();
            try
            {
                MainAppWindow.AppWindow.Show();
            }
            catch (Exception ex)
            {
                Startup.Log("AppWindow.Show: " + ex.Message);
            }

            MainAppWindow.LoadWorkspace();
            Startup.Log("Workspace loaded");
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }
}
