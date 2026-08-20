using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace MapaNotatek;

public static class Program
{
    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

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
            Application.Start((ApplicationInitializationCallbackParams p) =>
            {
                Startup.Log("Application.Start");
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            Startup.Log("Application.Start ended");
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
