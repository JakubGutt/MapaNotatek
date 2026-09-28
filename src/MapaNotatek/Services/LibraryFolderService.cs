namespace MapaNotatek.Services;

public static class LibraryFolderService
{
    private static readonly HashSet<string> IgnoredEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        "desktop.ini",
        "Thumbs.db"
    };

    private static readonly HashSet<string> RecognizedEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "Projects",
        "Notes",
        "People",
        "Trash",
        "Assets",
        "History",
        "Recovery",
        "app-state.json"
    };

    public static LibraryFolderInspection Inspect(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new LibraryFolderInspection(LibraryFolderKind.Invalid, string.Empty, "Ścieżka jest pusta.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folder);
        }
        catch (Exception ex)
        {
            return new LibraryFolderInspection(LibraryFolderKind.Invalid, folder, ex.Message);
        }

        if (!Directory.Exists(fullPath))
        {
            return new LibraryFolderInspection(LibraryFolderKind.Missing, fullPath, null);
        }

        var entries = Directory.EnumerateFileSystemEntries(fullPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !IgnoredEntries.Contains(name!))
            .ToList();
        if (entries.Count == 0)
        {
            return new LibraryFolderInspection(LibraryFolderKind.Empty, fullPath, null);
        }

        if (entries.Any(name => RecognizedEntries.Contains(name!)))
        {
            return new LibraryFolderInspection(LibraryFolderKind.ExistingLibrary, fullPath, null);
        }

        return new LibraryFolderInspection(
            LibraryFolderKind.UnrelatedContent,
            fullPath,
            "Folder zawiera inne pliki i nie wygląda jak biblioteka MapaNotatek.");
    }

    public static void EnsureCanOpen(string folder, string? currentRoot = null)
    {
        var inspection = Inspect(folder);
        if (inspection.Kind is LibraryFolderKind.Invalid or LibraryFolderKind.UnrelatedContent)
        {
            throw new InvalidOperationException(inspection.Message ?? "Nie można użyć tego folderu.");
        }

        if (currentRoot is null)
        {
            return;
        }

        var current = Path.GetFullPath(currentRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(inspection.FullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = Path.GetRelativePath(current, candidate);
        if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Nowa biblioteka nie może znajdować się wewnątrz bieżącej biblioteki.");
        }
    }
}

public enum LibraryFolderKind
{
    Invalid,
    Missing,
    Empty,
    ExistingLibrary,
    UnrelatedContent
}

public sealed record LibraryFolderInspection(
    LibraryFolderKind Kind,
    string FullPath,
    string? Message);
