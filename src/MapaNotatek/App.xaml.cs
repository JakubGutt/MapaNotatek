using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace MapaNotatek;

public partial class App : Application
{
    private Window? _window;
    private AppWindow? _nativeWindow;

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
        ShowProbeWindow();
    }

    private void ShowProbeWindow()
    {
        try
        {
            Startup.Log("Creating standalone AppWindow");
            _nativeWindow = AppWindow.Create();
            _nativeWindow.Title = "MapaNotatek AppWindow";
            _nativeWindow.MoveAndResize(new RectInt32(80, 80, 640, 360));
            _nativeWindow.Show(true);
            Startup.Log("Standalone AppWindow.Show OK");

            Startup.Log("Creating probe Window");
            var probe = new Window { Title = "MapaNotatek test" };
            Startup.Log("Window constructed");
            probe.Content = new TextBlock
            {
                Text = "To jest okno testowe. WinUI działa.",
                Margin = new Thickness(32),
                FontSize = 22
            };
            _window = probe;
            Startup.Log("Calling Activate");
            probe.Activate();
            Startup.Log("Activate returned");

            var hwnd = WindowNative.GetWindowHandle(probe);
            Startup.Log("HWND=" + hwnd);
            probe.AppWindow.MoveAndResize(new RectInt32(120, 120, 720, 420));
            probe.AppWindow.Show(true);
            Startup.Log("XAML AppWindow.Show OK");
            Startup.ShowHwnd(hwnd);
            Startup.Log("Win32 ShowWindow done");
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }
}
