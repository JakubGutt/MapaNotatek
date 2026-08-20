using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace MapaNotatek;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            ComWrappersSupport.InitializeComWrappers();
            try
            {
                Bootstrap.TryInitialize(0x00020004, out _);
            }
            catch
            {
                // Self-contained builds already include the runtime.
            }

            Application.Start(OnAppStart);
        }
        catch (Exception ex)
        {
            Startup.Fail(ex.ToString());
        }
    }

    private static void OnAppStart(ApplicationInitializationCallbackParams args)
    {
        var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
        SynchronizationContext.SetSynchronizationContext(context);
        _ = new App();
    }
}

internal static class Startup
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public static void Fail(string message)
    {
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
