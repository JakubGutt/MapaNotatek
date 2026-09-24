using Avalonia;
using System;
using Avalonia.Threading;

namespace MapaNotatek;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Startup.ConfigureExceptionLogging();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal static class Startup
{
    private static readonly object LogGate = new();

    public static void ConfigureExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Fail(args.ExceptionObject?.ToString() ?? "Nieznany wyjątek procesu.");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Fail(args.Exception.ToString());
            args.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Fail(args.Exception.ToString());
            // Keep the process alive so pending editor changes can still be flushed or
            // preserved through the close-safety dialog.
            args.Handled = true;
        };
    }

    public static void Log(string message)
    {
        Console.WriteLine("MapaNotatek: " + message);
        Console.Out.Flush();
        try
        {
            lock (LogGate)
            {
                var path = Path.Combine(Path.GetTempPath(), "MapaNotatek-startup.log");
                RotateIfNeeded(path);
                File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
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
        }
        catch
        {
            // Last-resort: ignore logging failures.
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 512 * 1024)
        {
            return;
        }

        File.Move(path, path + ".1", overwrite: true);
    }
}
