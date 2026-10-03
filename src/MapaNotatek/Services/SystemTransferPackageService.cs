using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class SystemTransferPackageService
{
    public const string Extension = ".mapanotatki";
    private const string ManifestName = "manifest.json";
    private const string SnapshotName = "snapshot.json";
    private const long MaxSnapshotBytes = 64L * 1024 * 1024;
    private const long MaxAssetBytes = 20L * 1024 * 1024;
    private const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxEntries = 50_000;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static SystemTransferManifest Export(
        string libraryRoot,
        string systemId,
        string destinationPath,
        AppState? state = null)
    {
        var root = SafeFileStorage.NormalizeDirectory(libraryRoot);
        var manifestPath = Path.Combine(root, LibrarySchemaService.ManifestFileName);
        var library = LibrarySchemaService.ReadManifest(manifestPath);
        var store = new MarkdownStore(root);
        var projects = store.LoadProjects();
        var notes = store.LoadNotes();
        var people = store.LoadPeople();
        var system = projects.FirstOrDefault(project =>
            string.Equals(project.Id, systemId, StringComparison.OrdinalIgnoreCase) &&
            project.ItemType == ProjectItemType.System)
            ?? throw new InvalidOperationException("Eksportować można wyłącznie istniejący system.");

        var includedProjects = SelectProjects(system, projects);
        var includedProjectIds = includedProjects.Select(project => project.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedSlugs = includedProjects.Select(project => project.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedNotes = notes.Where(note => note.Tags.Any(includedSlugs.Contains)).ToList();
        var includedNoteIds = includedNotes.Select(note => note.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedPersonSlugs = includedProjects.SelectMany(project => project.People)
            .Concat(includedNotes.SelectMany(note => note.People))
            .Concat(includedProjects.SelectMany(project => project.Checklist).SelectMany(task => task.People))
            .Concat(includedNotes.SelectMany(note => note.Checklist).SelectMany(task => task.People))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedPeople = people.Where(person => usedPersonSlugs.Contains(person.Slug)).ToList();
        var knownPersonSlugs = includedPeople.Select(person => person.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalSystemLinks = includedProjects.Sum(project =>
            project.SystemIds.Count(id => !includedProjectIds.Contains(id)));
        var externalNoteLinks = includedNotes.Sum(note =>
            note.RelatedNoteIds.Count(id => !includedNoteIds.Contains(id)));
        var missingPeople = usedPersonSlugs.Count(slug => !knownPersonSlugs.Contains(slug));

        var snapshot = new SystemTransferSnapshot
        {
            Projects = includedProjects.Select(project => FromProject(project, includedProjectIds)).ToList(),
            Notes = includedNotes.Select(note => FromNote(note, includedNoteIds)).ToList(),
            People = includedPeople.Select(FromPerson).ToList()
        };
        if (state is not null)
        {
            foreach (var id in includedProjectIds.Concat(includedNoteIds))
            {
                if (state.NodePositions.TryGetValue(id, out var position))
                {
                    snapshot.Positions[id] = new TransferGraphPosition { X = position.X, Y = position.Y };
                }
            }
        }

        var destination = Path.GetFullPath(destinationPath);
        if (!destination.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            destination += Extension;
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Nie można ustalić folderu docelowego.");
        Directory.CreateDirectory(parent);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("Plik pakietu już istnieje.");
        }

        var temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
        var manifest = new SystemTransferManifest
        {
            PackageId = Guid.NewGuid(),
            SourceLibraryId = library.LibraryId,
            SourceSystemId = system.Id,
            SourceSystemName = system.Name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Warnings = BuildExportWarnings(externalSystemLinks, externalNoteLinks, missingPeople)
        };

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
                AddBytes(archive, SnapshotName, snapshotBytes, manifest.Files);

                var assetIds = includedNoteIds
                    .Concat(includedPeople.Select(person => person.Id))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var assetId in assetIds)
                {
                    SafeFileStorage.ValidateFileToken(assetId, "identyfikator zasobu");
                    var folder = Path.GetFullPath(Path.Combine(root, "Assets", assetId));
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    foreach (var file in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        var info = new FileInfo(file);
                        if (info.Length <= 0 || info.Length > MaxAssetBytes)
                        {
                            throw new InvalidDataException($"Załącznik ma niedozwolony rozmiar: {Path.GetFileName(file)}");
                        }

                        AddFile(archive, $"assets/{assetId}/{Path.GetFileName(file)}", file, manifest.Files);
                    }
                }

                var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                var manifestEntry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using var output = manifestEntry.Open();
                output.Write(manifestBytes);
            }

            using (var durable = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                durable.Flush(flushToDisk: true);
            }

            _ = Open(destinationPath: temporary);
            File.Move(temporary, destination, overwrite: false);
            return manifest;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static LoadedSystemTransferPackage Open(string destinationPath)
    {
        var path = Path.GetFullPath(destinationPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Nie znaleziono pakietu zmian.", path);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException("Pakiet zawiera zbyt wiele plików.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = ValidateEntryPath(entry.FullName);
            if (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!entries.TryAdd(normalized, entry))
            {
                throw new InvalidDataException($"Pakiet zawiera powtórzoną ścieżkę: {normalized}");
            }

            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaxPackageBytes)
            {
                throw new InvalidDataException("Rozpakowana zawartość pakietu przekracza 2 GB.");
            }
        }

        if (!entries.TryGetValue(ManifestName, out var manifestEntry) || manifestEntry.Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("Pakiet nie zawiera poprawnego manifestu.");
        }

        var manifest = ReadJson<SystemTransferManifest>(manifestEntry, 2 * 1024 * 1024);
        if (!string.Equals(manifest.Format, "MapaNotatek-system-transfer", StringComparison.Ordinal) ||
            manifest.Version != 1 || manifest.PackageId == Guid.Empty || manifest.SourceLibraryId == Guid.Empty ||
            string.IsNullOrWhiteSpace(manifest.SourceSystemId))
        {
            throw new InvalidDataException("Pakiet ma nieobsługiwany albo uszkodzony format.");
        }

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestName };
        foreach (var file in manifest.Files)
        {
            var relative = ValidateEntryPath(file.Path);
            if (!declared.Add(relative) || !entries.TryGetValue(relative, out var entry))
            {
                throw new InvalidDataException($"Brakuje zadeklarowanego pliku albo ścieżka się powtarza: {relative}");
            }

            var max = string.Equals(relative, SnapshotName, StringComparison.OrdinalIgnoreCase)
                ? MaxSnapshotBytes
                : MaxAssetBytes;
            if (entry.Length != file.Bytes || entry.Length < 0 || entry.Length > max)
            {
                throw new InvalidDataException($"Nieprawidłowy rozmiar pliku w pakiecie: {relative}");
            }

            using var input = entry.Open();
            var hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Nieprawidłowa suma kontrolna: {relative}");
            }
        }

        foreach (var pathEntry in entries.Keys)
        {
            if (!declared.Contains(pathEntry))
            {
                throw new InvalidDataException($"Pakiet zawiera plik spoza manifestu: {pathEntry}");
            }
        }

        if (!entries.TryGetValue(SnapshotName, out var snapshotEntry))
        {
            throw new InvalidDataException("Pakiet nie zawiera snapshotu systemu.");
        }

        var snapshot = ReadJson<SystemTransferSnapshot>(snapshotEntry, MaxSnapshotBytes);
        ValidateSnapshot(manifest, snapshot);
        return new LoadedSystemTransferPackage(path, manifest, snapshot);
    }

    public static void ExtractAssets(
        LoadedSystemTransferPackage package,
        string sourceObjectId,
        string destinationFolder)
    {
        var prefix = $"assets/{sourceObjectId}/";
        var destination = Path.GetFullPath(destinationFolder);
        Directory.CreateDirectory(destination);
        using var stream = new FileStream(package.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                     !entry.FullName.EndsWith("/", StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(ValidateEntryPath(entry.FullName));
            var outputPath = SafeFileStorage.GetContainedFilePath(destination, name);
            using var input = entry.Open();
            using var output = new FileStream(outputPath + ".partial", FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
            output.Close();
            File.Move(outputPath + ".partial", outputPath, overwrite: true);
        }
    }

    private static List<Project> SelectProjects(Project system, IReadOnlyCollection<Project> projects)
    {
        var selected = new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase)
        {
            [system.Id] = system
        };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var project in projects)
            {
                if (selected.ContainsKey(project.Id))
                {
                    continue;
                }

                if ((!string.IsNullOrWhiteSpace(project.ParentId) && selected.ContainsKey(project.ParentId)) ||
                    project.SystemIds.Contains(system.Id, StringComparer.OrdinalIgnoreCase))
                {
                    selected[project.Id] = project;
                    changed = true;
                }
            }
        }

        return selected.Values.OrderBy(project => project.Created).ToList();
    }

    private static List<string> BuildExportWarnings(
        int externalSystemLinks,
        int externalNoteLinks,
        int missingPeople)
    {
        var warnings = new List<string>();
        if (externalSystemLinks > 0)
        {
            warnings.Add($"Pominięto {externalSystemLinks} powiązań z systemami spoza eksportowanego systemu.");
        }

        if (externalNoteLinks > 0)
        {
            warnings.Add($"Pominięto {externalNoteLinks} relacji do notatek spoza eksportowanego systemu.");
        }

        if (missingPeople > 0)
        {
            warnings.Add($"Nie znaleziono {missingPeople} osób wskazanych w elementach lub taskach.");
        }

        return warnings;
    }

    private static TransferProject FromProject(Project project, ISet<string> includedIds) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Slug = project.Slug,
        Description = project.Description,
        People = project.People.ToList(),
        SystemIds = project.SystemIds.Where(includedIds.Contains).ToList(),
        Checklist = project.Checklist.Select(TransferModelCloner.From).ToList(),
        IsArchived = project.IsArchived,
        ParentId = project.ParentId is not null && includedIds.Contains(project.ParentId) ? project.ParentId : null,
        ItemType = project.ItemType,
        Created = project.Created,
        Modified = project.Modified
    };

    private static TransferNote FromNote(Note note, ISet<string> includedIds) => new()
    {
        Id = note.Id,
        Title = note.Title,
        Body = note.Body,
        Tags = note.Tags.ToList(),
        People = note.People.ToList(),
        RelatedNoteIds = note.RelatedNoteIds.Where(includedIds.Contains).ToList(),
        Checklist = note.Checklist.Select(TransferModelCloner.From).ToList(),
        Created = note.Created,
        Modified = note.Modified
    };

    private static TransferPerson FromPerson(Person person) => new()
    {
        Id = person.Id,
        Name = person.Name,
        Slug = person.Slug,
        Role = person.Role,
        Description = person.Description,
        AvatarPath = person.AvatarPath,
        Created = person.Created,
        Modified = person.Modified
    };

    private static void AddBytes(
        ZipArchive archive,
        string path,
        byte[] bytes,
        ICollection<SystemTransferFileEntry> files)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using (var output = entry.Open())
        {
            output.Write(bytes);
        }

        files.Add(new SystemTransferFileEntry
        {
            Path = path,
            Bytes = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        });
    }

    private static void AddFile(
        ZipArchive archive,
        string path,
        string sourcePath,
        ICollection<SystemTransferFileEntry> files)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using (var input = File.OpenRead(sourcePath))
        using (var output = entry.Open())
        {
            input.CopyTo(output);
        }

        using var hashInput = File.OpenRead(sourcePath);
        files.Add(new SystemTransferFileEntry
        {
            Path = path,
            Bytes = new FileInfo(sourcePath).Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(hashInput)).ToLowerInvariant()
        });
    }

    private static T ReadJson<T>(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length < 0 || entry.Length > maxBytes)
        {
            throw new InvalidDataException($"Plik {entry.FullName} ma niedozwolony rozmiar.");
        }

        using var input = entry.Open();
        return JsonSerializer.Deserialize<T>(input, JsonOptions)
            ?? throw new InvalidDataException($"Plik {entry.FullName} jest pusty.");
    }

    private static string ValidateEntryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\'))
        {
            throw new InvalidDataException("Pakiet zawiera nieprawidłową ścieżkę.");
        }

        var normalized = value.Replace("//", "/", StringComparison.Ordinal);
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Pakiet zawiera niedozwoloną ścieżkę: {value}");
        }

        return normalized;
    }

    private static void ValidateSnapshot(SystemTransferManifest manifest, SystemTransferSnapshot snapshot)
    {
        if (snapshot.Projects is null || snapshot.Notes is null || snapshot.People is null || snapshot.Positions is null)
        {
            throw new InvalidDataException("Snapshot ma niekompletną strukturę.");
        }

        var projects = snapshot.Projects.ToDictionary(project => project.Id, StringComparer.OrdinalIgnoreCase);
        if (!projects.TryGetValue(manifest.SourceSystemId, out var root) || root.ItemType != ProjectItemType.System)
        {
            throw new InvalidDataException("Snapshot nie zawiera deklarowanego systemu głównego.");
        }

        var allIds = snapshot.Projects.Select(project => project.Id)
            .Concat(snapshot.Notes.Select(note => note.Id))
            .Concat(snapshot.People.Select(person => person.Id))
            .ToList();
        if (allIds.Any(string.IsNullOrWhiteSpace) ||
            allIds.Count != allIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidDataException("Snapshot zawiera puste albo powtórzone identyfikatory.");
        }

        foreach (var project in snapshot.Projects)
        {
            SafeFileStorage.ValidateFileToken(project.Id, "identyfikator projektu");
            SafeFileStorage.ValidateFileToken(project.Slug, "slug projektu");
            if (project.ParentId is not null && !projects.ContainsKey(project.ParentId))
            {
                throw new InvalidDataException($"Projekt {project.Name} wskazuje rodzica spoza pakietu.");
            }
            if (project.SystemIds.Any(id => !projects.TryGetValue(id, out var linked) || linked.ItemType != ProjectItemType.System))
            {
                throw new InvalidDataException($"Projekt {project.Name} wskazuje nieistniejący system.");
            }
        }

        var noteIds = snapshot.Notes.Select(note => note.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var note in snapshot.Notes)
        {
            SafeFileStorage.ValidateFileToken(note.Id, "identyfikator notatki");
            if (note.RelatedNoteIds.Any(id => !noteIds.Contains(id)))
            {
                throw new InvalidDataException($"Notatka {note.Title} wskazuje relację spoza pakietu.");
            }
        }

        foreach (var person in snapshot.People)
        {
            SafeFileStorage.ValidateFileToken(person.Id, "identyfikator osoby");
            SafeFileStorage.ValidateFileToken(person.Slug, "slug osoby");
        }
        if (snapshot.People.Select(person => person.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.People.Count)
        {
            throw new InvalidDataException("Snapshot zawiera powtórzone identyfikatory osób.");
        }

        foreach (var position in snapshot.Positions)
        {
            if (!allIds.Contains(position.Key, StringComparer.OrdinalIgnoreCase) ||
                !double.IsFinite(position.Value.X) || !double.IsFinite(position.Value.Y))
            {
                throw new InvalidDataException("Snapshot zawiera nieprawidłową pozycję elementu grafu.");
            }
        }

        foreach (var project in snapshot.Projects)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.Id };
            var parentId = project.ParentId;
            while (parentId is not null)
            {
                if (!visited.Add(parentId))
                {
                    throw new InvalidDataException("Snapshot zawiera cykl w hierarchii projektów.");
                }
                parentId = projects[parentId].ParentId;
            }
        }
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
            // The partial file remains clearly marked and is never accepted as a package.
        }
    }
}

public sealed record LoadedSystemTransferPackage(
    string Path,
    SystemTransferManifest Manifest,
    SystemTransferSnapshot Snapshot);
