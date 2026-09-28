using System.Security.Cryptography;
using System.Text.Json;

namespace MapaNotatek.Services;

public static class BackupService
{
    private const string ManifestFileName = "backup-manifest.json";
    private static readonly string[] DataDirectories =
    [
        "Projects",
        "Notes",
        "People",
        "Trash",
        "Assets",
        "History",
        "Recovery"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a verified snapshot of application-owned data. The destination only appears
    /// after every file and the manifest have been written successfully.
    /// </summary>
    public static void ExportCopy(string sourceFolder, string destinationFolder)
    {
        var source = NormalizeExistingDirectory(sourceFolder);
        var destination = Path.GetFullPath(destinationFolder);
        EnsureDestinationIsOutsideSource(source, destination);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Miejsce kopii już istnieje: {destination}");
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Nie można ustalić folderu nadrzędnego kopii.");
        Directory.CreateDirectory(parent);

        var staging = Path.Combine(parent, $".{Path.GetFileName(destination)}.partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var entries = new List<BackupFileEntry>();
            foreach (var relativePath in EnumerateApplicationFiles(source))
            {
                var sourcePath = SafeCombine(source, relativePath);
                var destinationPath = SafeCombine(staging, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: false);

                var info = new FileInfo(destinationPath);
                entries.Add(new BackupFileEntry
                {
                    Path = NormalizeManifestPath(relativePath),
                    Bytes = info.Length,
                    Sha256 = ComputeSha256(destinationPath),
                    LastWriteUtc = info.LastWriteTimeUtc
                });
            }

            var manifest = new BackupManifest
            {
                Format = "MapaNotatek-backup",
                Version = 1,
                CreatedUtc = DateTimeOffset.UtcNow,
                Files = entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToList()
            };
            var manifestPath = Path.Combine(staging, ManifestFileName);
            WriteTextDurably(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

            var validation = Validate(staging);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    "Weryfikacja kopii nie powiodła się: " + string.Join("; ", validation.Errors));
            }

            Directory.Move(staging, destination);
        }
        catch
        {
            TryDeleteStaging(staging);
            throw;
        }
    }

    public static BackupValidationResult Validate(string backupFolder)
    {
        var errors = new List<string>();
        string root;
        try
        {
            root = NormalizeExistingDirectory(backupFolder);
        }
        catch (Exception ex)
        {
            return new BackupValidationResult(false, [ex.Message], null);
        }

        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return new BackupValidationResult(false, ["Brak manifestu kopii."], null);
        }

        if (IsReparsePoint(manifestPath))
        {
            return new BackupValidationResult(false, ["Manifest kopii jest dowiązaniem symbolicznym."], null);
        }

        BackupManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (Exception ex)
        {
            return new BackupValidationResult(false, [$"Nie można odczytać manifestu: {ex.Message}"], null);
        }

        if (manifest is null || manifest.Format != "MapaNotatek-backup" || manifest.Version != 1)
        {
            return new BackupValidationResult(false, ["Nieobsługiwany format kopii."], manifest);
        }

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Files)
        {
            var relative = entry.Path.Replace('/', Path.DirectorySeparatorChar);
            string path;
            try
            {
                path = SafeCombine(root, relative);
            }
            catch (Exception ex)
            {
                errors.Add($"Niebezpieczna ścieżka „{entry.Path}”: {ex.Message}");
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            if (!declared.Add(fullPath))
            {
                errors.Add($"Powtórzony plik w manifeście: {entry.Path}");
                continue;
            }

            if (ContainsReparsePoint(root, fullPath))
            {
                errors.Add($"Dowiązanie symboliczne nie jest dozwolone: {entry.Path}");
                continue;
            }

            if (!File.Exists(path))
            {
                errors.Add($"Brak pliku: {entry.Path}");
                continue;
            }

            var info = new FileInfo(path);
            if (info.Length != entry.Bytes)
            {
                errors.Add($"Nieprawidłowy rozmiar: {entry.Path}");
                continue;
            }

            if (!string.Equals(ComputeSha256(path), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Nieprawidłowa suma kontrolna: {entry.Path}");
            }
        }

        foreach (var entry in EnumerateTreeWithoutFollowingLinks(root, errors))
        {
            if (entry.IsLink)
            {
                continue;
            }

            var file = entry.Path;
            if (string.Equals(file, manifestPath, SafeFileStorage.PathComparison))
            {
                continue;
            }

            if (!declared.Contains(Path.GetFullPath(file)))
            {
                errors.Add($"Plik spoza manifestu: {Path.GetRelativePath(root, file)}");
            }
        }

        return new BackupValidationResult(errors.Count == 0, errors, manifest);
    }

    /// <summary>
    /// Restores a validated backup into a new, empty folder. Existing data is never overwritten.
    /// </summary>
    public static void RestoreCopy(string backupFolder, string destinationFolder)
    {
        var backup = NormalizeExistingDirectory(backupFolder);
        var validation = Validate(backup);
        if (!validation.IsValid || validation.Manifest is null)
        {
            throw new InvalidDataException(
                "Kopia jest uszkodzona: " + string.Join("; ", validation.Errors));
        }

        var destination = Path.GetFullPath(destinationFolder);
        EnsureDestinationIsOutsideSource(backup, destination);
        if (Path.TrimEndingDirectorySeparator(destination) ==
            Path.TrimEndingDirectorySeparator(Path.GetPathRoot(destination) ?? string.Empty))
        {
            throw new IOException("Nie można przywracać kopii bezpośrednio do katalogu głównego systemu.");
        }

        if (Directory.Exists(destination) && IsReparsePoint(destination))
        {
            throw new IOException("Folder docelowy nie może być dowiązaniem symbolicznym.");
        }

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new IOException("Przywracanie wymaga nowego albo pustego folderu.");
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Nie można ustalić folderu nadrzędnego przywracanej biblioteki.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(destination)}.restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var entry in validation.Manifest.Files)
            {
                var relative = entry.Path.Replace('/', Path.DirectorySeparatorChar);
                var sourcePath = SafeCombine(backup, relative);
                if (ContainsReparsePoint(backup, sourcePath))
                {
                    throw new InvalidDataException($"Kopia zawiera niedozwolone dowiązanie: {entry.Path}");
                }

                var destinationPath = SafeCombine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: false);
                var copiedInfo = new FileInfo(destinationPath);
                if (copiedInfo.Length != entry.Bytes ||
                    !string.Equals(ComputeSha256(destinationPath), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Plik zmienił się podczas przywracania: {entry.Path}");
                }
            }

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: false);
            }

            Directory.Move(staging, destination);
        }
        catch
        {
            TryDeleteStaging(staging);
            if (!Directory.Exists(destination))
            {
                try
                {
                    Directory.CreateDirectory(destination);
                }
                catch
                {
                    // The original target was absent; the caller still gets the root error.
                }
            }

            throw;
        }
    }

    private static IEnumerable<string> EnumerateApplicationFiles(string source)
    {
        var statePath = Path.Combine(source, "app-state.json");
        if (File.Exists(statePath))
        {
            yield return "app-state.json";
        }

        foreach (var directoryName in DataDirectories)
        {
            var directory = Path.Combine(source, directoryName);
            if (!Directory.Exists(directory) || IsReparsePoint(directory))
            {
                continue;
            }

            foreach (var file in EnumerateFilesWithoutLinks(directory))
            {
                yield return Path.GetRelativePath(source, file);
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!IsReparsePoint(file))
            {
                yield return file;
            }
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (IsReparsePoint(child))
            {
                continue;
            }

            foreach (var file in EnumerateFilesWithoutLinks(child))
            {
                yield return file;
            }
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool ContainsReparsePoint(string root, string path)
    {
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path);
        while (!string.Equals(current, rootPath, SafeFileStorage.PathComparison))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
            {
                return true;
            }

            current = Path.GetDirectoryName(current) ?? rootPath;
            if (!current.StartsWith(rootPath + Path.DirectorySeparatorChar, SafeFileStorage.PathComparison) &&
                !string.Equals(current, rootPath, SafeFileStorage.PathComparison))
            {
                return true;
            }
        }

        return IsReparsePoint(rootPath);
    }

    private static IEnumerable<(string Path, bool IsLink)> EnumerateTreeWithoutFollowingLinks(
        string directory,
        ICollection<string> errors)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var isLink = IsReparsePoint(file);
            if (isLink)
            {
                errors.Add($"Dowiązanie symboliczne nie jest dozwolone: {Path.GetRelativePath(directory, file)}");
            }

            yield return (Path.GetFullPath(file), isLink);
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (IsReparsePoint(child))
            {
                errors.Add($"Dowiązanie symboliczne nie jest dozwolone: {Path.GetRelativePath(directory, child)}");
                yield return (Path.GetFullPath(child), true);
                continue;
            }

            foreach (var entry in EnumerateTreeWithoutFollowingLinks(child, errors))
            {
                yield return entry;
            }
        }
    }

    private static string NormalizeExistingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Ścieżka folderu jest pusta.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        if (IsReparsePoint(fullPath))
        {
            throw new IOException("Folder kopii nie może być dowiązaniem symbolicznym.");
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void EnsureDestinationIsOutsideSource(string source, string destination)
    {
        var relative = Path.GetRelativePath(source, destination);
        if (relative == "." ||
            (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && relative != ".."))
        {
            throw new IOException("Kopia nie może znajdować się wewnątrz folderu danych.");
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("Ścieżka bezwzględna nie jest dozwolona.");
        }

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, SafeFileStorage.PathComparison))
        {
            throw new InvalidDataException("Ścieżka wychodzi poza katalog kopii.");
        }

        return fullPath;
    }

    private static string NormalizeManifestPath(string relativePath) =>
        relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteTextDurably(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void TryDeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch
        {
            // A .partial folder is intentionally left behind for manual recovery.
        }
    }
}

public sealed class BackupManifest
{
    public string Format { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public List<BackupFileEntry> Files { get; set; } = [];
}

public sealed class BackupFileEntry
{
    public string Path { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime LastWriteUtc { get; set; }
}

public sealed record BackupValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    BackupManifest? Manifest);
