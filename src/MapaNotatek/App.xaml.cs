using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using WinRT.Interop;

namespace MapaNotatek;

public partial class App : Application
{
    private Window? _window;
    private AppWindow? _nativeWindow;
    private DesktopWindowXamlSource? _xamlSource;

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
        ShowNativeProbe();
        ShowXamlProbe();
        Startup.Log("OnLaunched done");
    }

    private static UIElement CreateProbeContent()
    {
        return new TextBlock
        {
            Text = "To jest okno testowe. WinUI działa.",
            Margin = new Thickness(32),
            FontSize = 22
        };
    }

    private void ShowNativeProbe()
    {
        try
        {
            Startup.Log("Creating standalone AppWindow");
            _nativeWindow = AppWindow.Create();
            _nativeWindow.Title = "MapaNotatek AppWindow";
            _nativeWindow.MoveAndResize(new RectInt32(80, 80, 720, 420));
            _nativeWindow.Destroying += (_, _) => Startup.Log("AppWindow destroying");
            _nativeWindow.Show(true);
            Startup.Log("Standalone AppWindow.Show OK");

            _xamlSource = new DesktopWindowXamlSource();
            _xamlSource.Initialize(_nativeWindow.Id);
            _xamlSource.Content = CreateProbeContent();
            Startup.Log("Xaml island OK");
        }
        catch (Exception ex)
        {
            Startup.Log("Native probe FAIL: " + ex);
        }
    }

    private void ShowXamlProbe()
    {
        try
        {
            Startup.Log("Creating probe Window");
            var probe = new Window { Title = "MapaNotatek test" };
            Startup.Log("Window constructed");
            probe.Content = CreateProbeContent();
            probe.Closed += (_, _) => Startup.Log("Probe window closed");
            _window = probe;
            Startup.Log("Calling Activate");
            probe.Activate();
            Startup.Log("Activate returned Visible=" + probe.Visible);

            var hwnd = WindowNative.GetWindowHandle(probe);
            Startup.Log("HWND=" + hwnd);
            probe.AppWindow.MoveAndResize(new RectInt32(200, 200, 720, 420));
            probe.AppWindow.Show(true);
            Startup.Log("XAML AppWindow.Show OK IsVisible=" + probe.AppWindow.IsVisible);
            Startup.ShowHwnd(hwnd);
            Startup.Log("Win32 ShowWindow done");
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }
}
