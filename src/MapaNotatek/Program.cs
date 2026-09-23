using Avalonia;
using System;

namespace MapaNotatek;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal static class Startup
{
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
        }
        catch
        {
            // Last-resort: ignore logging failures.
        }
    }
}
