using Microsoft.UI.Xaml;

namespace MapaNotatek;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    public static MainWindow? MainAppWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        _window = MainAppWindow;
        _window.Activate();
    }
}
