using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace MapaNotatek;

public partial class App : Application
{
    private Window? _window;
    private DispatcherQueueTimer? _startTimer;

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
        var queue = DispatcherQueue.GetForCurrentThread();
        _startTimer = queue.CreateTimer();
        _startTimer.Interval = TimeSpan.FromMilliseconds(50);
        _startTimer.IsRepeating = false;
        _startTimer.Tick += OnStartTimerTick;
        _startTimer.Start();
        Startup.Log("Timer started");
    }

    private void OnStartTimerTick(DispatcherQueueTimer timer, object args)
    {
        timer.Stop();
        timer.Tick -= OnStartTimerTick;
        Startup.Log("Timer fired");
        ShowProbeWindow();
    }

    private void ShowProbeWindow()
    {
        try
        {
            Startup.Log("Creating probe Window");
            var probe = new Window { Title = "MapaNotatek test" };
            Startup.Log("Window constructed");
            probe.Content = new TextBlock
            {
                Text = "To jest okno testowe. WinUI działa.",
                Margin = new Thickness(32),
                FontSize = 22
            };
            probe.Closed += (_, _) => Startup.Log("Probe window closed");
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
