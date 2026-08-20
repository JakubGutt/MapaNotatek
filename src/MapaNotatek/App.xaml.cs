using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        var queue = DispatcherQueue.GetForCurrentThread();
        var queued = queue.TryEnqueue(() =>
        {
            Startup.Log("StartWindow queued");
            ShowProbeWindow();
        });
        Startup.Log(queued ? "TryEnqueue OK" : "TryEnqueue failed");
        if (!queued)
        {
            ShowProbeWindow();
        }
    }

    private void ShowProbeWindow()
    {
        try
        {
            Startup.Log("Creating probe Window");
            var probe = new Window { Title = "MapaNotatek test" };
            probe.Content = new TextBlock
            {
                Text = "To jest okno testowe. WinUI działa.",
                Margin = new Thickness(32),
                FontSize = 22
            };
            _window = probe;
            probe.Activate();
            Startup.Log("Probe Activate OK");
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }
}
