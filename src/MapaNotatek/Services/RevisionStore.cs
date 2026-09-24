using System.Security.Cryptography;

namespace MapaNotatek.Services;

/// <summary>
/// Keeps local, bounded snapshots of files before they are replaced.
/// </summary>
public sealed class RevisionStore
{
    private const int DefaultRetention = 30;

    public RevisionStore(string dataRoot)
    {
        Root = Path.GetFullPath(dataRoot);
        HistoryFolder = Path.Combine(Root, "History");
    }

    public string Root { get; }
    public string HistoryFolder { get; }

    public string? CaptureExisting(string kind, string itemId, string? existingPath, int retention = DefaultRetention)
    {
        if (string.IsNullOrWhiteSpace(existingPath) || !File.Exists(existingPath))
        {
            return null;
        }

        var safeKind = ValidateSegment(kind);
        var safeId = ValidateSegment(itemId);
        var bytes = File.ReadAllBytes(existingPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var folder = Path.Combine(HistoryFolder, safeKind, safeId);
        Directory.CreateDirectory(folder);

        var newest = Directory.EnumerateFiles(folder, "*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is not null && Path.GetFileNameWithoutExtension(newest).EndsWith(hash[..12], StringComparison.Ordinal))
        {
            return newest;
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        var destination = Path.Combine(folder, $"{timestamp}-{hash[..12]}.md");
        var temporary = destination + ".partial";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, destination);
        Prune(folder, Math.Max(2, retention));
        return destination;
    }

    public IReadOnlyList<RevisionInfo> List(string kind, string itemId)
    {
        var folder = Path.Combine(HistoryFolder, ValidateSegment(kind), ValidateSegment(itemId));
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path => new RevisionInfo(path, File.GetLastWriteTimeUtc(path), new FileInfo(path).Length))
            .ToList();
    }

    public string Read(RevisionInfo revision)
    {
        var path = Path.GetFullPath(revision.Path);
        var historyRoot = Path.GetFullPath(HistoryFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(historyRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Rewizja znajduje się poza lokalną historią.");
        }

        return File.ReadAllText(path);
    }

    private static string ValidateSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar) ||
            value is "." or "..")
        {
            throw new InvalidDataException("Nieprawidłowy identyfikator historii.");
        }

        return value;
    }

    private static void Prune(string folder, int retention)
    {
        foreach (var path in Directory.EnumerateFiles(folder, "*.md")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(retention))
        {
            File.Delete(path);
        }
    }
}

public sealed record RevisionInfo(string Path, DateTime TimestampUtc, long Bytes)
{
    public override string ToString()
    {
        var size = Bytes < 1024 ? $"{Bytes} B" : $"{Bytes / 1024d:0.#} KB";
        return $"{TimestampUtc.ToLocalTime():g}  •  {size}";
    }
}
