using System.Text.RegularExpressions;

namespace MapaNotatek.Services;

/// <summary>
/// Imports images into the local library. No remote URI is ever resolved or downloaded.
/// </summary>
public sealed class AttachmentStore
{
    public const long MaxImageBytes = 20 * 1024 * 1024;
    private static readonly Regex SafeIdentifier = new("^[a-zA-Z0-9_-]{1,128}$", RegexOptions.Compiled);

    public AttachmentStore(string dataRoot)
    {
        Root = Path.GetFullPath(dataRoot);
        AssetsFolder = Path.Combine(Root, "Assets");
        Directory.CreateDirectory(AssetsFolder);
    }

    public string Root { get; }
    public string AssetsFolder { get; }

    public AttachmentImportResult ImportImage(string noteId, string sourcePath)
    {
        ValidateNoteId(noteId);
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Nie znaleziono obrazu.", source);
        }

        var info = new FileInfo(source);
        if (info.Length <= 0 || info.Length > MaxImageBytes)
        {
            throw new InvalidDataException("Obraz musi mieć od 1 B do 20 MB.");
        }

        var format = DetectImageFormat(source);
        var noteFolder = SafeCombine(AssetsFolder, noteId);
        Directory.CreateDirectory(noteFolder);

        var baseName = SlugHelper.FromName(Path.GetFileNameWithoutExtension(source));
        if (string.IsNullOrWhiteSpace(baseName) || baseName == "item")
        {
            baseName = "obraz";
        }

        var fileName = $"{baseName}-{Guid.NewGuid():N}.{format.Extension}";
        var destination = SafeCombine(noteFolder, fileName);
        var temporary = destination + ".partial";
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            if (new FileInfo(temporary).Length != info.Length)
            {
                throw new IOException("Nie udało się skopiować całego obrazu.");
            }

            File.Move(temporary, destination);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        var relativeFromRoot = Path.GetRelativePath(Root, destination)
            .Replace(Path.DirectorySeparatorChar, '/');
        var relativeFromNotes = "../" + relativeFromRoot;
        return new AttachmentImportResult(
            destination,
            relativeFromRoot,
            relativeFromNotes,
            format.MimeType,
            info.Length);
    }

    public IReadOnlyList<string> ListNoteAssets(string noteId)
    {
        ValidateNoteId(noteId);
        var folder = SafeCombine(AssetsFolder, noteId);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder)
            .Where(path => !path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string BuildMarkdownImage(string altText, string relativePath)
    {
        var safeAlt = (altText ?? string.Empty).Replace("[", "\\[").Replace("]", "\\]");
        var safePath = relativePath.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");
        return $"![{safeAlt}]({safePath})";
    }

    private static ImageFormat DetectImageFormat(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        if (read >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return new ImageFormat("png", "image/png");
        }

        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return new ImageFormat("jpg", "image/jpeg");
        }

        if (read >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
        {
            return new ImageFormat("gif", "image/gif");
        }

        if (read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return new ImageFormat("webp", "image/webp");
        }

        throw new InvalidDataException("Obsługiwane obrazy to PNG, JPG, GIF i WebP.");
    }

    private static void ValidateNoteId(string noteId)
    {
        if (!SafeIdentifier.IsMatch(noteId))
        {
            throw new InvalidDataException("Nieprawidłowy identyfikator notatki.");
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Ścieżka załącznika wychodzi poza bibliotekę.");
        }

        return fullPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A .partial file remains visible and is never treated as an attachment.
        }
    }

    private sealed record ImageFormat(string Extension, string MimeType);
}

public sealed record AttachmentImportResult(
    string FullPath,
    string RelativeFromLibrary,
    string MarkdownPath,
    string MimeType,
    long Bytes);
