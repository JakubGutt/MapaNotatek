using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MapaNotatek.Services;

public static class LocalDiagnosticsService
{
    public static string BuildReport(
        string dataFolder,
        int noteCount,
        int projectCount,
        int pendingSaveCount,
        IEnumerable<StorageReadIssue> storageIssues,
        string? lastSaveError)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MapaNotatek — lokalny raport diagnostyczny");
        builder.AppendLine($"Utworzono: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Wersja: {Assembly.GetExecutingAssembly().GetName().Version}");
        builder.AppendLine($"System: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Architektura: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine("Tryb sieciowy: brak — aplikacja nie zawiera klienta sieciowego");
        builder.AppendLine($"Biblioteka: {RedactHome(dataFolder)}");
        builder.AppendLine($"Notatki: {noteCount}");
        builder.AppendLine($"Projekty/foldery: {projectCount}");
        builder.AppendLine($"Oczekujące zapisy: {pendingSaveCount}");

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dataFolder));
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                builder.AppendLine($"Wolne miejsce: {drive.AvailableFreeSpace / (1024 * 1024):N0} MB");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine("Wolne miejsce: nie można odczytać — " + ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(lastSaveError))
        {
            builder.AppendLine();
            builder.AppendLine("Ostatni błąd zapisu:");
            builder.AppendLine(lastSaveError);
        }

        var issues = storageIssues.ToList();
        builder.AppendLine();
        builder.AppendLine($"Ostrzeżenia magazynu: {issues.Count}");
        foreach (var issue in issues)
        {
            builder.AppendLine(
                $"- [{issue.Area}] {issue.Message} | {RedactHome(issue.Path)} | backup={issue.RecoveredFromBackup}");
            builder.AppendLine("  " + issue.Exception.GetType().Name + ": " + issue.Exception.Message);
        }

        AppendLocalLog(builder, "Log startowy", Path.Combine(Path.GetTempPath(), "MapaNotatek-startup.log"));
        AppendLocalLog(builder, "Ostatnia awaria", Path.Combine(Path.GetTempPath(), "MapaNotatek-crash.log"));
        builder.AppendLine();
        builder.AppendLine("Raport nie zawiera treści notatek. Jest zapisywany lokalnie i nigdy nie jest wysyłany automatycznie.");
        return builder.ToString();
    }

    public static void WriteReport(string path, string report)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Nie można ustalić folderu raportu.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.partial");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(report);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void AppendLocalLog(StringBuilder builder, string title, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(title + ":");
        try
        {
            foreach (var line in File.ReadLines(path).TakeLast(200))
            {
                builder.AppendLine(RedactHome(line));
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine("Nie można odczytać logu: " + ex.Message);
        }
    }

    private static string RedactHome(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(home)
            ? text
            : text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
    }
}
