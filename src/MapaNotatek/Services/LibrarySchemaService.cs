using System.Text.Json;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class LibrarySchemaService
{
    public const int CurrentSchemaVersion = 3;
    public const string ManifestFileName = "library.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static LibraryOpenResult Prepare(string root)
    {
        var source = SafeFileStorage.NormalizeDirectory(root);
        Directory.CreateDirectory(source);
        var manifestPath = Path.Combine(source, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            var manifest = ReadManifest(manifestPath);
            if (manifest.SchemaVersion > CurrentSchemaVersion)
            {
                return new LibraryOpenResult(source, true, false, source, manifest,
                    $"Biblioteka używa nowszego schematu {manifest.SchemaVersion}. Otworzono ją tylko do odczytu.");
            }
            if (manifest.SchemaVersion < CurrentSchemaVersion)
            {
                return MigrateLegacyCopy(source);
            }
            return new LibraryOpenResult(source, false, false, source, manifest, null);
        }

        if (!ContainsUserDocuments(source))
        {
            var manifest = NewManifest();
            WriteManifest(source, manifest);
            return new LibraryOpenResult(source, false, false, source, manifest, null);
        }

        return MigrateLegacyCopy(source);
    }

    public static LibraryManifest ReadManifest(string path)
    {
        var manifest = JsonSerializer.Deserialize<LibraryManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Manifest biblioteki jest pusty.");
        if (!string.Equals(manifest.Format, "MapaNotatek-library", StringComparison.Ordinal) ||
            manifest.SchemaVersion < 1 || manifest.LibraryId == Guid.Empty)
        {
            throw new InvalidDataException("Manifest biblioteki ma nieprawidłowy format.");
        }

        return manifest;
    }

    private static LibraryOpenResult MigrateLegacyCopy(string source)
    {
        var parent = Path.GetDirectoryName(source)
            ?? throw new InvalidOperationException("Nie można ustalić katalogu nadrzędnego biblioteki.");
        var sourceName = Path.GetFileName(source);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = UniqueDirectory(parent, $"{sourceName}-schema-{CurrentSchemaVersion}-{stamp}");
        var transfer = UniqueDirectory(parent, $".{sourceName}-migration-{stamp}");

        EnsureMigrationSpace(source, parent);
        try
        {
            BackupService.ExportCopy(source, transfer);
            BackupService.RestoreCopy(transfer, destination);
            var store = new MarkdownStore(destination);
            var projects = store.LoadProjects();
            var notes = store.LoadNotes();
            var duplicateIds = projects.Select(project => project.Id)
                .Concat(notes.Select(note => note.Id))
                .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateIds is not null)
            {
                throw new InvalidDataException($"Migracja wykryła powtórzony identyfikator: {duplicateIds.Key}");
            }
            foreach (var project in projects.Where(SupportsSystemMembership))
            {
                var systems = FindAncestorSystems(project, projects);
                if (systems.Count == 0 || systems.All(id => project.SystemIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }
                project.SystemIds = project.SystemIds.Concat(systems).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            NormalizeTaskIdentityAndOrder(projects, notes);
            foreach (var project in projects)
            {
                project.Description = FrontMatter.ExtractProjectContext(project.Description);
                store.SaveProject(project);
            }
            foreach (var note in notes)
            {
                store.SaveNote(note);
            }

            ValidateMigratedLibrary(store);
            var manifest = NewManifest();
            WriteManifest(destination, manifest);
            return new LibraryOpenResult(destination, false, true, source, manifest,
                $"Utworzono bezpiecznie zmigrowaną bibliotekę: {destination}. Oryginał pozostał bez zmian.");
        }
        catch
        {
            // Never touch the source. Partial destinations are intentionally preserved for diagnostics.
            throw;
        }
        finally
        {
            if (Directory.Exists(transfer))
            {
                Directory.Delete(transfer, recursive: true);
            }
        }
    }

    private static bool SupportsSystemMembership(Project project) =>
        project.ItemType is ProjectItemType.Product or ProjectItemType.Subsystem or ProjectItemType.Component;

    private static void NormalizeTaskIdentityAndOrder(
        IEnumerable<Project> projects,
        IEnumerable<Note> notes)
    {
        var tasks = projects.SelectMany(project => project.Checklist)
            .Concat(notes.SelectMany(note => note.Checklist))
            .OrderBy(task => task.Priority.HasValue ? 0 : 1)
            .ThenBy(task => task.Priority)
            .ToList();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (string.IsNullOrWhiteSpace(task.Id) || !usedIds.Add(task.Id))
            {
                do
                {
                    task.Id = Guid.NewGuid().ToString("N");
                } while (!usedIds.Add(task.Id));
            }

            task.Priority = index * 100;
        }
    }

    private static List<string> FindAncestorSystems(Project project, IReadOnlyCollection<Project> projects)
    {
        var result = new List<string>();
        var parentId = project.ParentId;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.Id };
        while (!string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId))
        {
            var parent = projects.FirstOrDefault(candidate => string.Equals(candidate.Id, parentId, StringComparison.OrdinalIgnoreCase));
            if (parent is null)
            {
                break;
            }

            if (parent.ItemType == ProjectItemType.System)
            {
                result.Add(parent.Id);
            }
            parentId = parent.ParentId;
        }
        return result;
    }

    private static void ValidateMigratedLibrary(MarkdownStore store)
    {
        var projects = store.LoadProjects();
        var notes = store.LoadNotes();
        var people = store.LoadPeople();
        var ids = projects.Select(item => item.Id).Concat(notes.Select(item => item.Id)).Concat(people.Select(item => item.Id)).ToList();
        if (ids.Count != ids.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidDataException("Migracja wykryła powtórzone identyfikatory.");
        }

        var systems = projects.Where(item => item.ItemType == ProjectItemType.System)
            .Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (projects.SelectMany(item => item.SystemIds).Any(id => !systems.Contains(id)))
        {
            throw new InvalidDataException("Migracja wykryła przypisanie do nieistniejącego systemu.");
        }

        var tasks = projects.SelectMany(project => project.Checklist)
            .Concat(notes.SelectMany(note => note.Checklist))
            .ToList();
        if (tasks.Any(task => string.IsNullOrWhiteSpace(task.Id) || !task.Priority.HasValue) ||
            tasks.Select(task => task.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tasks.Count)
        {
            throw new InvalidDataException("Migracja nie utworzyła stabilnej kolejności zadań.");
        }
    }

    private static void EnsureMigrationSpace(string source, string destinationParent)
    {
        var bytes = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .Sum(path => new FileInfo(path).Length);
        var root = Path.GetPathRoot(destinationParent);
        if (!string.IsNullOrWhiteSpace(root))
        {
            var available = new DriveInfo(root).AvailableFreeSpace;
            var required = Math.Max(64L * 1024 * 1024, (long)(bytes * 2.2));
            if (available < required)
            {
                throw new IOException($"Za mało miejsca na bezpieczną migrację. Wymagane: około {required / 1024 / 1024} MB.");
            }
        }
    }

    private static bool ContainsUserDocuments(string root) =>
        new[] { "Projects", "Notes", "People" }
            .Select(folder => Path.Combine(root, folder))
            .Any(folder => Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.md").Any());

    private static LibraryManifest NewManifest() => new()
    {
        Format = "MapaNotatek-library",
        SchemaVersion = CurrentSchemaVersion,
        LibraryId = Guid.NewGuid(),
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow
    };

    private static void WriteManifest(string root, LibraryManifest manifest)
    {
        manifest.UpdatedUtc = DateTimeOffset.UtcNow;
        SafeFileStorage.AtomicWriteAllText(
            SafeFileStorage.GetContainedFilePath(root, ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static string UniqueDirectory(string parent, string name)
    {
        var candidate = Path.Combine(parent, name);
        var index = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(parent, name + "-" + index++);
        }
        return candidate;
    }
}

public sealed class LibraryManifest
{
    public string Format { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public Guid LibraryId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed record LibraryOpenResult(
    string Root,
    bool IsReadOnly,
    bool WasMigrated,
    string OriginalRoot,
    LibraryManifest Manifest,
    string? Message);
