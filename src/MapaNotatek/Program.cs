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
        XamlCheckProcessRequirements();
        ComWrappersSupport.InitializeComWrappers();
        Application.Start((ApplicationInitializationCallbackParams p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}

internal static class Startup
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public static void Log(string message)
    {
        Console.WriteLine("MapaNotatek: " + message);
        Console.Out.Flush();
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "MapaNotatek-startup.log"),
                $"{DateTime.Now:O} {message}{Environment.NewLine}");
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
            MessageBox(IntPtr.Zero, message + "\n\nZapisano: " + path, "MapaNotatek", 0x00000010);
        }
        catch
        {
            // Last-resort: ignore logging failures.
        }
    }
}
