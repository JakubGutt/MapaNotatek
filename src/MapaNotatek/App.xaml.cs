using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

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
            Startup.Log("Creating probe Window");
            var probe = new Window { Title = "MapaNotatek test" };
            Startup.Log("Window constructed");

            var root = new Grid { Background = new SolidColorBrush(Colors.WhiteSmoke) };
            root.Children.Add(new TextBlock
            {
                Text = "To jest okno testowe. WinUI działa.",
                Margin = new Thickness(32),
                FontSize = 22
            });
            probe.Content = root;
            probe.Activated += (_, e) => Startup.Log("Activated event " + e.WindowActivationState);
            probe.Closed += (_, _) => Startup.Log("Probe window closed");
            probe.VisibilityChanged += (_, _) => Startup.Log("VisibilityChanged Visible=" + probe.Visible);
            _window = probe;

            Startup.Log("Calling Activate");
            probe.Activate();
            Startup.Log("Activate returned Visible=" + probe.Visible);

            var hwnd = WindowNative.GetWindowHandle(probe);
            Startup.Log("HWND=" + hwnd);
            probe.AppWindow.MoveAndResize(new RectInt32(120, 120, 720, 420));
            Startup.ShowHwnd(hwnd);
            Startup.Log("OnLaunched done");
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }
}
