using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;

namespace MapaNotatek;

public static class Program
{
    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    private static DispatcherQueueController? _queueController;
    private static WindowsXamlManager? _xamlManager;
    private static AppWindow? _appWindow;
    private static DesktopWindowXamlSource? _xamlSource;

    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Startup.Log("Unhandled: " + e.ExceptionObject);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            Startup.Log("ProcessExit");
        try
        {
            Startup.Log("start");
            XamlCheckProcessRequirements();
            Startup.Log("XamlCheck OK");
            ComWrappersSupport.InitializeComWrappers();
            Startup.Log("COM OK");

            _queueController = DispatcherQueueController.CreateOnCurrentThread();
            var queue = _queueController.DispatcherQueue;
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            Startup.Log("Dispatcher OK");

            _xamlManager = WindowsXamlManager.InitializeForCurrentThread();
            Startup.Log("XamlManager OK");

            _appWindow = AppWindow.Create();
            _appWindow.Title = "MapaNotatek test";
            _appWindow.MoveAndResize(new RectInt32(120, 120, 720, 420));
            Startup.Log("AppWindow created");

            _xamlSource = new DesktopWindowXamlSource();
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
                queue.EnqueueEventLoopExit();
            };

            _appWindow.Show();
            var hwnd = Win32Interop.GetWindowFromWindowId(_appWindow.Id);
            Startup.Log("HWND=" + hwnd);
            Startup.ShowHwnd(hwnd);
            if (Startup.IsSmoke)
            {
                var timer = queue.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(8);
                timer.IsRepeating = false;
                timer.Tick += (_, _) =>
                {
                    Startup.Log("smoke: still alive, exiting");
                    queue.EnqueueEventLoopExit();
                };
                timer.Start();
            }

            Startup.Log("running event loop");
            queue.RunEventLoop();
            Startup.Log("event loop ended");
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }
}

internal static class Startup
{
    private const int SwShownormal = 1;

    public static bool IsSmoke =>
        Environment.GetEnvironmentVariable("MAPANOTATEK_SMOKE") == "1" ||
        Environment.GetEnvironmentVariable("CI") == "true";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    public static void ShowHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            Log("ShowHwnd skipped: HWND=0");
            return;
        }

        ShowWindow(hwnd, SwShownormal);
        UpdateWindow(hwnd);
        SetForegroundWindow(hwnd);
    }

    public static void Log(string message)
    {
        Console.WriteLine("MapaNotatek: " + message);
        Console.Out.Flush();
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "MapaNotatek-startup.log");
            File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging failures.
        }
    }

    public static void Fail(string message)
    {
        Log("FAIL: " + message);
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "MapaNotatek-crash.log");
            File.WriteAllText(path, $"{DateTime.Now:O}\n{message}");
            if (!IsSmoke)
            {
                MessageBox(IntPtr.Zero, message + "\n\nZapisano: " + path, "MapaNotatek", 0x00000010);
            }
        }
        catch
        {
            // Last-resort: ignore logging failures.
        }
    }
}
