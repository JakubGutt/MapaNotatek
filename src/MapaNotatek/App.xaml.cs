using Microsoft.UI.Xaml;

namespace MapaNotatek;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
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
        try
        {
            MainAppWindow = new MainWindow();
            _window = MainAppWindow;
            MainAppWindow.Activate();
            MainAppWindow.AppWindow.Show();
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }
}
