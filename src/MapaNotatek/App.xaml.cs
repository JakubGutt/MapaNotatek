using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace MapaNotatek;

public partial class App : Application
{
    private AppWindow? _appWindow;
    private DesktopWindowXamlSource? _xamlSource;

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
        Startup.Log("OnLaunched");
        try
        {
            _appWindow = AppWindow.Create();
            _appWindow.Title = "MapaNotatek test";
            _appWindow.MoveAndResize(new RectInt32(120, 120, 720, 420));
            Startup.Log("AppWindow created");

            _xamlSource = new DesktopWindowXamlSource();
            Startup.Log("Island constructed");
            _xamlSource.Initialize(_appWindow.Id);
            Startup.Log("Island initialized");

            var root = new Grid { Background = new SolidColorBrush(Colors.WhiteSmoke) };
            root.Resources.MergedDictionaries.Add(new XamlControlsResources());
            root.Children.Add(new TextBlock
            {
                Text = "To jest okno testowe. WinUI działa.",
                Margin = new Thickness(32),
                FontSize = 22
            });
            _xamlSource.Content = root;
            Startup.Log("Island content set");

            _appWindow.Closing += (_, _) =>
            {
                Startup.Log("AppWindow closing");
                Exit();
            };

            _appWindow.Show();
            var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(_appWindow.Id);
            Startup.Log("HWND=" + hwnd);
            Startup.ShowHwnd(hwnd);

            if (Startup.IsSmoke)
            {
                var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(8);
                timer.IsRepeating = false;
                timer.Tick += (_, _) =>
                {
                    Startup.Log("smoke: still alive, exiting");
                    Exit();
                };
                timer.Start();
            }

            Startup.Log("OnLaunched done");
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }
}
