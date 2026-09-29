using System.Security.Cryptography;
using System.Text;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public enum StorageArea
{
    Projects,
    Notes,
    People,
    Trash,
    AppState
}

public sealed record StorageReadIssue(
    StorageArea Area,
    string Path,
    string Message,
    Exception Exception,
    bool RecoveredFromBackup = false);

public sealed class StorageReadException : IOException
{
    public StorageReadException(StorageReadIssue issue)
        : base(issue.Message, issue.Exception)
    {
        Issue = issue;
    }

    public StorageReadIssue Issue { get; }
}

public sealed class StorageConflictException : IOException
{
    public StorageConflictException(string path)
        : base($"Plik został zmieniony poza aplikacją i nie został nadpisany: {path}")
    {
        Path = path;
    }

    public string Path { get; }
}

public sealed class MarkdownStore
{
    private readonly Dictionary<(StorageArea Area, string Path), StorageReadIssue> _readIssues = new();

    public MarkdownStore(string root)
    {
        Root = SafeFileStorage.NormalizeDirectory(root);
        EnsureFolders();
    }

    public string Root { get; private set; }
    public string ProjectsFolder => Path.Combine(Root, "Projects");
    public string NotesFolder => Path.Combine(Root, "Notes");
    public string PeopleFolder => Path.Combine(Root, "People");
    public string TrashFolder => Path.Combine(Root, "Trash");
    public string TrashedProjectsFolder => Path.Combine(TrashFolder, "Projects");
    public string TrashedPeopleFolder => Path.Combine(TrashFolder, "People");

    public IReadOnlyList<StorageReadIssue> ReadIssues => _readIssues.Values
        .OrderBy(issue => issue.Area)
        .ThenBy(issue => issue.Path, SafeFileStorage.PathComparer)
        .ToList();

    public event Action<StorageReadIssue>? ReadIssueDetected;

    public void SetRoot(string root)
    {
        var normalized = SafeFileStorage.NormalizeDirectory(root);
        EnsureFolders(normalized);
        Root = normalized;
        _readIssues.Clear();
    }

    public void EnsureFolders() => EnsureFolders(Root);

    public List<Project> LoadProjects()
    {
        ClearIssues(StorageArea.Projects);
        var projects = new List<Project>();
        foreach (var file in EnumerateMarkdownFiles(ProjectsFolder, StorageArea.Projects))
        {
            try
            {
                SafeFileStorage.ValidateContainedFilePath(ProjectsFolder, file);
                projects.Add(ReadProject(file));
            }
            catch (Exception primaryError)
            {
                if (TryReadProjectBackup(
                        file,
                        ProjectsFolder,
                        StorageArea.Projects,
                        primaryError,
                        out var recovered))
                {
                    projects.Add(recovered!);
                }
                else if (!HasIssue(StorageArea.Projects, file))
                {
                    ReportIssue(StorageArea.Projects, file, "Nie można odczytać pliku projektu.", primaryError);
                }
            }
        }

        return projects.OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public List<Note> LoadNotes()
    {
        ClearIssues(StorageArea.Notes);
        return LoadNotesFromFolder(NotesFolder, StorageArea.Notes)
            .OrderByDescending(note => note.Modified)
            .ToList();
    }

    public List<Person> LoadPeople()
    {
        ClearIssues(StorageArea.People);
        var people = new List<Person>();
        foreach (var file in EnumerateMarkdownFiles(PeopleFolder, StorageArea.People))
        {
            try
            {
                SafeFileStorage.ValidateContainedFilePath(PeopleFolder, file);
                people.Add(ReadPerson(file));
            }
            catch (Exception primaryError)
            {
                if (TryReadPersonBackup(file, primaryError, out var recovered))
                {
                    people.Add(recovered!);
                }
                else if (!HasIssue(StorageArea.People, file))
                {
                    ReportIssue(StorageArea.People, file, "Nie można odczytać pliku osoby.", primaryError);
                }
            }
        }

        return people.OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public List<Note> LoadTrashedNotes()
    {
        ClearIssues(StorageArea.Trash);
        return LoadNotesFromFolder(TrashFolder, StorageArea.Trash)
            .OrderByDescending(note => note.Modified)
            .ToList();
    }

    public List<Project> LoadTrashedProjects()
    {
        var projects = new List<Project>();
        foreach (var file in EnumerateMarkdownFiles(TrashedProjectsFolder, StorageArea.Trash))
        {
            try
            {
                SafeFileStorage.ValidateContainedFilePath(TrashedProjectsFolder, file);
                projects.Add(ReadProject(file));
            }
            catch (Exception primaryError)
            {
                if (TryReadProjectBackup(
                        file,
                        TrashedProjectsFolder,
                        StorageArea.Trash,
                        primaryError,
                        out var recovered))
                {
                    projects.Add(recovered!);
                }
                else if (!HasIssue(StorageArea.Trash, file))
                {
                    ReportIssue(StorageArea.Trash, file, "Nie można odczytać projektu z kosza.", primaryError);
                }
            }
        }

        return projects.OrderByDescending(project => project.Modified).ToList();
    }

    public List<Person> LoadTrashedPeople()
    {
        var people = new List<Person>();
        foreach (var file in EnumerateMarkdownFiles(TrashedPeopleFolder, StorageArea.Trash))
        {
            try
            {
                SafeFileStorage.ValidateContainedFilePath(TrashedPeopleFolder, file);
                people.Add(ReadPerson(file));
            }
            catch (Exception primaryError)
            {
                if (TryReadPersonBackup(
                        file,
                        TrashedPeopleFolder,
                        StorageArea.Trash,
                        primaryError,
                        out var recovered))
                {
                    people.Add(recovered!);
                }
                else if (!HasIssue(StorageArea.Trash, file))
                {
                    ReportIssue(StorageArea.Trash, file, "Nie można odczytać osoby z kosza.", primaryError);
                }
            }
        }

        return people.OrderByDescending(person => person.Modified).ToList();
    }

    public Project CreateProject(string name, string? parentId = null, bool isFolder = false)
        => CreateProject(name, parentId, isFolder ? ProjectItemType.Folder : ProjectItemType.Project);

    public Project CreateProject(string name, string? parentId, ProjectItemType itemType)
    {
        var existingSlugs = LoadProjects().Select(project => project.Slug);
        var slug = SlugHelper.Unique(SlugHelper.FromName(name), existingSlugs);
        var now = DateTimeOffset.Now;
        var project = new Project
        {
            Id = SlugHelper.NewId(),
            Name = name.Trim(),
            Slug = slug,
            Description = string.Empty,
            ParentId = parentId,
            ItemType = itemType,
            Created = now,
            Modified = now
        };
        SaveProject(project);
        return project;
    }

    public Note CreateNote(string title, IEnumerable<string>? tags = null)
    {
        var now = DateTimeOffset.Now;
        var note = new Note
        {
            Id = SlugHelper.NewId(),
            Title = string.IsNullOrWhiteSpace(title) ? "Nowa notatka" : title.Trim(),
            Body = string.Empty,
            Tags = tags?.ToList() ?? [],
            Created = now,
            Modified = now
        };
        SaveNote(note);
        return note;
    }

    public Person CreatePerson(string name)
    {
        var safeName = string.IsNullOrWhiteSpace(name) ? "Nowa osoba" : name.Trim();
        var existingSlugs = LoadPeople().Select(person => person.Slug);
        var now = DateTimeOffset.Now;
        var person = new Person
        {
            Id = SlugHelper.NewId(),
            Name = safeName,
            Slug = SlugHelper.Unique(SlugHelper.FromName(safeName), existingSlugs),
            Created = now,
            Modified = now
        };
        SavePerson(person);
        return person;
    }

    public void SaveProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        SafeFileStorage.ValidateFileToken(project.Id, "identyfikator projektu");
        SafeFileStorage.ValidateFileToken(project.Slug, "slug projektu");

        var path = SafeFileStorage.GetContainedFilePath(ProjectsFolder, $"{project.Slug}.md");
        var previousPath = ValidatePreviousPath(ProjectsFolder, project.FilePath);
        EnsureTargetIsAvailable(path, previousPath);
        EnsureUnchanged(previousPath, project.PersistedContentHash);

        project.Modified = DateTimeOffset.Now;
        var content = FrontMatter.WriteProject(project);
        WriteDocument(path, previousPath, ProjectsFolder, content, project.PersistedContentHash);
        project.FilePath = path;
        project.PersistedContentHash = ComputeHash(content);
    }

    public void SaveNote(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        SafeFileStorage.ValidateFileToken(note.Id, "identyfikator notatki");

        var slug = SlugHelper.FromName(note.Title);
        var path = SafeFileStorage.GetContainedFilePath(NotesFolder, $"{slug}-{note.Id}.md");
        var previousPath = ValidatePreviousPath(NotesFolder, note.FilePath);
        EnsureTargetIsAvailable(path, previousPath);
        EnsureUnchanged(previousPath, note.PersistedContentHash);

        note.Modified = DateTimeOffset.Now;
        var content = FrontMatter.WriteNote(note);
        WriteDocument(path, previousPath, NotesFolder, content, note.PersistedContentHash);
        note.FilePath = path;
        note.PersistedContentHash = ComputeHash(content);
    }

    public void SavePerson(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        SafeFileStorage.ValidateFileToken(person.Id, "identyfikator osoby");
        SafeFileStorage.ValidateFileToken(person.Slug, "slug osoby");

        var path = SafeFileStorage.GetContainedFilePath(PeopleFolder, $"{person.Slug}.md");
        var previousPath = ValidatePreviousPath(PeopleFolder, person.FilePath);
        EnsureTargetIsAvailable(path, previousPath);
        EnsureUnchanged(previousPath, person.PersistedContentHash);

        person.Modified = DateTimeOffset.Now;
        var content = FrontMatter.WritePerson(person);
        WriteDocument(path, previousPath, PeopleFolder, content, person.PersistedContentHash);
        person.FilePath = path;
        person.PersistedContentHash = ComputeHash(content);
    }

    public void MoveNoteToTrash(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (string.IsNullOrWhiteSpace(note.FilePath) || !File.Exists(note.FilePath))
        {
            SaveNote(note);
        }

        var source = SafeFileStorage.ValidateContainedFilePath(NotesFolder, note.FilePath);
        var destination = SafeFileStorage.GetContainedFilePath(TrashFolder, Path.GetFileName(source));
        destination = UniquePath(TrashFolder, destination);
        File.Move(source, destination, overwrite: false);
        note.FilePath = destination;
        TryMoveSidecarBackup(source, destination, NotesFolder, TrashFolder);
    }

    public void RestoreNoteFromTrash(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var source = SafeFileStorage.ValidateContainedFilePath(TrashFolder, note.FilePath);
        var destination = SafeFileStorage.GetContainedFilePath(NotesFolder, Path.GetFileName(source));
        destination = UniquePath(NotesFolder, destination);
        if (File.Exists(source))
        {
            File.Move(source, destination, overwrite: false);
            note.FilePath = destination;
            TryMoveSidecarBackup(source, destination, TrashFolder, NotesFolder);
        }
        else
        {
            SafeFileStorage.AtomicWriteAllText(destination, FrontMatter.WriteNote(note));
            note.FilePath = destination;
            note.PersistedContentHash = ComputeFileHash(destination);
        }
    }

    public void MoveProjectToTrash(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(project.FilePath) || !File.Exists(project.FilePath))
        {
            SaveProject(project);
        }

        var source = SafeFileStorage.ValidateContainedFilePath(ProjectsFolder, project.FilePath);
        var destination = SafeFileStorage.GetContainedFilePath(TrashedProjectsFolder, Path.GetFileName(source));
        destination = UniquePath(TrashedProjectsFolder, destination);
        File.Move(source, destination, overwrite: false);
        project.FilePath = destination;
        TryMoveSidecarBackup(source, destination, ProjectsFolder, TrashedProjectsFolder);
    }

    public void MovePersonToTrash(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        if (string.IsNullOrWhiteSpace(person.FilePath) || !File.Exists(person.FilePath))
        {
            SavePerson(person);
        }

        var source = SafeFileStorage.ValidateContainedFilePath(PeopleFolder, person.FilePath);
        var destination = SafeFileStorage.GetContainedFilePath(TrashedPeopleFolder, Path.GetFileName(source));
        destination = UniquePath(TrashedPeopleFolder, destination);
        File.Move(source, destination, overwrite: false);
        person.FilePath = destination;
        TryMoveSidecarBackup(source, destination, PeopleFolder, TrashedPeopleFolder);
    }

    public void RestorePersonFromTrash(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        var source = SafeFileStorage.ValidateContainedFilePath(TrashedPeopleFolder, person.FilePath);
        var originalSlug = person.Slug;
        var originalPath = person.FilePath;
        var originalHash = person.PersistedContentHash;
        var existingSlugs = LoadPeople()
            .Where(existing => !string.Equals(existing.Id, person.Id, StringComparison.OrdinalIgnoreCase))
            .Select(existing => existing.Slug);
        person.Slug = SlugHelper.Unique(person.Slug, existingSlugs);
        var destination = SafeFileStorage.GetContainedFilePath(PeopleFolder, $"{person.Slug}.md");
        EnsureTargetIsAvailable(destination, previousPath: null);
        string? copiedBackup = null;
        var sourceBackup = SafeFileStorage.GetContainedFilePath(
            TrashedPeopleFolder,
            Path.GetFileName(source) + ".bak");
        try
        {
            var content = FrontMatter.WritePerson(person);
            SafeFileStorage.AtomicWriteAllText(destination, content);
            if (File.Exists(sourceBackup))
            {
                var backupDestination = SafeFileStorage.GetContainedFilePath(
                    PeopleFolder,
                    Path.GetFileName(destination) + ".bak");
                copiedBackup = UniquePath(PeopleFolder, backupDestination);
                File.Copy(sourceBackup, copiedBackup, overwrite: false);
            }

            if (File.Exists(source))
            {
                File.Delete(source);
            }

            person.FilePath = destination;
            person.PersistedContentHash = ComputeHash(content);
        }
        catch
        {
            TryDeleteFile(destination);
            if (copiedBackup is not null)
            {
                TryDeleteFile(copiedBackup);
            }

            person.Slug = originalSlug;
            person.FilePath = originalPath;
            person.PersistedContentHash = originalHash;
            throw;
        }

        TryDeleteFile(sourceBackup);
    }

    public void RestoreProjectFromTrash(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var source = SafeFileStorage.ValidateContainedFilePath(TrashedProjectsFolder, project.FilePath);
        var originalSlug = project.Slug;
        var originalPath = project.FilePath;
        var originalHash = project.PersistedContentHash;
        var existingSlugs = LoadProjects()
            .Where(existing => !string.Equals(existing.Id, project.Id, StringComparison.OrdinalIgnoreCase))
            .Select(existing => existing.Slug);
        project.Slug = SlugHelper.Unique(project.Slug, existingSlugs);
        var destination = SafeFileStorage.GetContainedFilePath(ProjectsFolder, $"{project.Slug}.md");
        EnsureTargetIsAvailable(destination, previousPath: null);
        string? copiedBackup = null;
        var sourceBackup = SafeFileStorage.GetContainedFilePath(
            TrashedProjectsFolder,
            Path.GetFileName(source) + ".bak");
        try
        {
            var content = FrontMatter.WriteProject(project);
            SafeFileStorage.AtomicWriteAllText(destination, content);

            if (File.Exists(sourceBackup))
            {
                var backupDestination = SafeFileStorage.GetContainedFilePath(
                    ProjectsFolder,
                    Path.GetFileName(destination) + ".bak");
                copiedBackup = UniquePath(ProjectsFolder, backupDestination);
                File.Copy(sourceBackup, copiedBackup, overwrite: false);
            }

            if (File.Exists(source))
            {
                File.Delete(source);
            }

            project.FilePath = destination;
            project.PersistedContentHash = ComputeHash(content);
        }
        catch
        {
            TryDeleteFile(destination);
            if (copiedBackup is not null)
            {
                TryDeleteFile(copiedBackup);
            }

            project.Slug = originalSlug;
            project.FilePath = originalPath;
            project.PersistedContentHash = originalHash;
            throw;
        }

        // The copied sidecar is already safe in Projects. Failure to remove the redundant
        // Trash copy must not invalidate an otherwise completed restore.
        TryDeleteFile(sourceBackup);
    }

    public void DeleteProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!string.IsNullOrWhiteSpace(project.FilePath))
        {
            var existing = SafeFileStorage.ValidateContainedFilePath(ProjectsFolder, project.FilePath);
            if (File.Exists(existing))
            {
                File.Delete(existing);
                return;
            }
        }

        SafeFileStorage.ValidateFileToken(project.Slug, "slug projektu");
        var path = SafeFileStorage.GetContainedFilePath(ProjectsFolder, $"{project.Slug}.md");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private List<Note> LoadNotesFromFolder(string folder, StorageArea area)
    {
        var notes = new List<Note>();
        foreach (var file in EnumerateMarkdownFiles(folder, area))
        {
            try
            {
                SafeFileStorage.ValidateContainedFilePath(folder, file);
                notes.Add(ReadNote(file));
            }
            catch (Exception primaryError)
            {
                if (TryReadNoteBackup(file, area, primaryError, out var recovered))
                {
                    notes.Add(recovered!);
                }
                else if (!HasIssue(area, file))
                {
                    ReportIssue(area, file, "Nie można odczytać pliku notatki.", primaryError);
                }
            }
        }

        return notes;
    }

    private Project ReadProject(string path, string? logicalPath = null)
    {
        var documentPath = logicalPath ?? path;
        var content = File.ReadAllText(path);
        var parsed = FrontMatter.Parse(content);
        var type = parsed["type"] ?? "project";
        if (!string.Equals(type, "project", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Plik w folderze Projects ma typ „{type}”.");
        }

        var name = string.IsNullOrWhiteSpace(parsed.Title)
            ? Path.GetFileNameWithoutExtension(documentPath)
            : parsed.Title;
        var slug = parsed["slug"];
        if (string.IsNullOrWhiteSpace(slug))
        {
            var tags = FrontMatter.SplitTags(parsed["tags"]);
            slug = SlugHelper.FromName(tags.FirstOrDefault() ?? name);
        }

        var id = string.IsNullOrWhiteSpace(parsed["id"]) ? SlugHelper.NewId() : parsed["id"]!;
        SafeFileStorage.ValidateFileToken(id, "identyfikator projektu");
        SafeFileStorage.ValidateFileToken(slug, "slug projektu");
        var parentId = string.IsNullOrWhiteSpace(parsed["parent"]) ? null : parsed["parent"];
        if (parentId is not null)
        {
            SafeFileStorage.ValidateFileToken(parentId, "identyfikator rodzica");
        }

        return new Project
        {
            Id = id,
            Name = name,
            Slug = slug,
            Description = parsed.Body,
            People = PersonTagService.Parse(parsed["people"]),
            Checklist = parsed.Checklist,
            IsArchived = FrontMatter.ReadBool(parsed["archived"]),
            ParentId = parentId,
            ItemType = ProjectItemTypeCatalog.Parse(parsed["kind"]),
            Created = FrontMatter.ReadDate(parsed["created"], File.GetCreationTime(path)),
            Modified = FrontMatter.ReadDate(parsed["modified"], File.GetLastWriteTime(path)),
            FilePath = documentPath,
            PersistedContentHash = ComputePersistedHash(documentPath, content)
        };
    }

    private Note ReadNote(string path, string? logicalPath = null)
    {
        var documentPath = logicalPath ?? path;
        var content = File.ReadAllText(path);
        var parsed = FrontMatter.Parse(content);
        var type = parsed["type"] ?? "note";
        if (!string.Equals(type, "note", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Plik w folderze Notes ma typ „{type}”.");
        }

        var title = string.IsNullOrWhiteSpace(parsed.Title)
            ? Path.GetFileNameWithoutExtension(documentPath)
            : parsed.Title;
        var id = string.IsNullOrWhiteSpace(parsed["id"]) ? SlugHelper.NewId() : parsed["id"]!;
        SafeFileStorage.ValidateFileToken(id, "identyfikator notatki");

        return new Note
        {
            Id = id,
            Title = title,
            Body = parsed.Body,
            Tags = FrontMatter.SplitTags(parsed["tags"]),
            People = PersonTagService.Parse(parsed["people"]),
            Checklist = parsed.Checklist,
            Created = FrontMatter.ReadDate(parsed["created"], File.GetCreationTime(path)),
            Modified = FrontMatter.ReadDate(parsed["modified"], File.GetLastWriteTime(path)),
            FilePath = documentPath,
            PersistedContentHash = ComputePersistedHash(documentPath, content)
        };
    }

    private Person ReadPerson(string path, string? logicalPath = null)
    {
        var documentPath = logicalPath ?? path;
        var content = File.ReadAllText(path);
        var parsed = FrontMatter.Parse(content);
        var type = parsed["type"] ?? "person";
        if (!string.Equals(type, "person", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Plik w folderze People ma typ „{type}”.");
        }

        var name = string.IsNullOrWhiteSpace(parsed.Title)
            ? Path.GetFileNameWithoutExtension(documentPath)
            : parsed.Title;
        var slug = string.IsNullOrWhiteSpace(parsed["slug"])
            ? SlugHelper.FromName(name)
            : parsed["slug"]!;
        var id = string.IsNullOrWhiteSpace(parsed["id"]) ? SlugHelper.NewId() : parsed["id"]!;
        SafeFileStorage.ValidateFileToken(id, "identyfikator osoby");
        SafeFileStorage.ValidateFileToken(slug, "slug osoby");

        return new Person
        {
            Id = id,
            Name = name,
            Slug = slug,
            Role = parsed["role"] ?? string.Empty,
            Description = parsed.Body,
            AvatarPath = parsed["avatar"] ?? string.Empty,
            Created = FrontMatter.ReadDate(parsed["created"], File.GetCreationTime(path)),
            Modified = FrontMatter.ReadDate(parsed["modified"], File.GetLastWriteTime(path)),
            FilePath = documentPath,
            PersistedContentHash = ComputePersistedHash(documentPath, content)
        };
    }

    private bool TryReadProjectBackup(
        string primaryPath,
        string folder,
        StorageArea area,
        Exception primaryError,
        out Project? recovered)
    {
        recovered = null;
        var backupPath = primaryPath + ".bak";
        try
        {
            SafeFileStorage.ValidateContainedFilePath(folder, backupPath);
            if (!File.Exists(backupPath))
            {
                return false;
            }

            recovered = ReadProject(backupPath, primaryPath);
            ReportIssue(
                area,
                primaryPath,
                area == StorageArea.Trash
                    ? "Projekt w koszu jest uszkodzony lub niedostępny; wczytano kopię awaryjną."
                    : "Plik projektu jest uszkodzony lub niedostępny; wczytano kopię awaryjną.",
                primaryError,
                recoveredFromBackup: true);
            return true;
        }
        catch (Exception backupError)
        {
            ReportIssue(
                area,
                primaryPath,
                area == StorageArea.Trash
                    ? "Nie można odczytać projektu z kosza ani jego kopii awaryjnej."
                    : "Nie można odczytać pliku projektu ani jego kopii awaryjnej.",
                new AggregateException(primaryError, backupError));
            return false;
        }
    }

    private bool TryReadNoteBackup(
        string primaryPath,
        StorageArea area,
        Exception primaryError,
        out Note? recovered)
    {
        recovered = null;
        var folder = area == StorageArea.Trash ? TrashFolder : NotesFolder;
        var backupPath = primaryPath + ".bak";
        try
        {
            SafeFileStorage.ValidateContainedFilePath(folder, backupPath);
            if (!File.Exists(backupPath))
            {
                return false;
            }

            recovered = ReadNote(backupPath, primaryPath);
            ReportIssue(
                area,
                primaryPath,
                "Plik notatki jest uszkodzony lub niedostępny; wczytano kopię awaryjną.",
                primaryError,
                recoveredFromBackup: true);
            return true;
        }
        catch (Exception backupError)
        {
            ReportIssue(
                area,
                primaryPath,
                "Nie można odczytać pliku notatki ani jego kopii awaryjnej.",
                new AggregateException(primaryError, backupError));
            return false;
        }
    }

    private bool TryReadPersonBackup(string primaryPath, Exception primaryError, out Person? recovered) =>
        TryReadPersonBackup(primaryPath, PeopleFolder, StorageArea.People, primaryError, out recovered);

    private bool TryReadPersonBackup(
        string primaryPath,
        string folder,
        StorageArea area,
        Exception primaryError,
        out Person? recovered)
    {
        recovered = null;
        var backupPath = primaryPath + ".bak";
        try
        {
            SafeFileStorage.ValidateContainedFilePath(folder, backupPath);
            if (!File.Exists(backupPath))
            {
                return false;
            }

            recovered = ReadPerson(backupPath, primaryPath);
            ReportIssue(
                area,
                primaryPath,
                "Plik osoby jest uszkodzony lub niedostępny; wczytano kopię awaryjną.",
                primaryError,
                recoveredFromBackup: true);
            return true;
        }
        catch (Exception backupError)
        {
            ReportIssue(
                area,
                primaryPath,
                "Nie można odczytać pliku osoby ani jego kopii awaryjnej.",
                new AggregateException(primaryError, backupError));
            return false;
        }
    }

    private string[] EnumerateMarkdownFiles(string folder, StorageArea area)
    {
        try
        {
            SafeFileStorage.ValidateContainedDirectory(Root, folder);
            return Directory.GetFiles(folder, "*.md", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            var issue = ReportIssue(area, folder, "Nie można odczytać folderu danych.", ex);
            throw new StorageReadException(issue);
        }
    }

    private static void EnsureFolders(string root)
    {
        Directory.CreateDirectory(root);
        SafeFileStorage.EnsureContainedDirectory(root, Path.Combine(root, "Projects"));
        SafeFileStorage.EnsureContainedDirectory(root, Path.Combine(root, "Notes"));
        SafeFileStorage.EnsureContainedDirectory(root, Path.Combine(root, "People"));
        SafeFileStorage.EnsureContainedDirectory(root, Path.Combine(root, "Trash"));
        SafeFileStorage.EnsureContainedDirectory(root, Path.Combine(root, "Trash", "Projects"));
        SafeFileStorage.EnsureContainedDirectory(root, Path.Combine(root, "Trash", "People"));
    }

    private void ClearIssues(StorageArea area)
    {
        foreach (var key in _readIssues.Keys.Where(key => key.Area == area).ToList())
        {
            _readIssues.Remove(key);
        }
    }

    private bool HasIssue(StorageArea area, string path) => _readIssues.ContainsKey((area, path));

    private StorageReadIssue ReportIssue(
        StorageArea area,
        string path,
        string message,
        Exception exception,
        bool recoveredFromBackup = false)
    {
        var issue = new StorageReadIssue(area, path, message, exception, recoveredFromBackup);
        _readIssues[(area, path)] = issue;
        ReadIssueDetected?.Invoke(issue);
        return issue;
    }

    private static string? ValidatePreviousPath(string folder, string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : SafeFileStorage.ValidateContainedFilePath(folder, path);
    }

    private static void EnsureTargetIsAvailable(string targetPath, string? previousPath)
    {
        if (File.Exists(targetPath) &&
            (previousPath is null || !SafeFileStorage.PathsEqual(targetPath, previousPath)))
        {
            throw new IOException($"Docelowy plik już istnieje: {targetPath}");
        }

        if (Directory.Exists(targetPath))
        {
            throw new IOException($"Docelowa ścieżka jest folderem: {targetPath}");
        }
    }

    private static void EnsureUnchanged(string? path, string expectedHash)
    {
        if (path is null || string.IsNullOrWhiteSpace(expectedHash) || !File.Exists(path))
        {
            return;
        }

        if (!string.Equals(ComputeFileHash(path), expectedHash, StringComparison.Ordinal))
        {
            throw new StorageConflictException(path);
        }
    }

    private static string ComputePersistedHash(string logicalPath, string parsedContent) =>
        File.Exists(logicalPath) ? ComputeFileHash(logicalPath) : ComputeHash(parsedContent);

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static void ArchiveRenamedFile(
        string? previousPath,
        string currentPath,
        string folder,
        string expectedPreviousHash)
    {
        if (previousPath is null || SafeFileStorage.PathsEqual(previousPath, currentPath) || !File.Exists(previousPath))
        {
            return;
        }

        var backupPath = SafeFileStorage.GetContainedFilePath(folder, Path.GetFileName(previousPath) + ".bak");
        File.Move(previousPath, backupPath, overwrite: true);
        if (!string.IsNullOrWhiteSpace(expectedPreviousHash) &&
            !string.Equals(ComputeFileHash(backupPath), expectedPreviousHash, StringComparison.Ordinal))
        {
            try
            {
                File.Move(backupPath, previousPath, overwrite: false);
            }
            catch (Exception restoreError)
            {
                throw new IOException(
                    $"Wykryto równoczesną zmianę nazwanego pliku; wersję zewnętrzną zachowano jako „{backupPath}”.",
                    restoreError);
            }

            throw new StorageConflictException(previousPath);
        }
    }

    private static void WriteDocument(
        string path,
        string? previousPath,
        string folder,
        string content,
        string expectedPreviousHash)
    {
        var isRename = previousPath is not null && !SafeFileStorage.PathsEqual(previousPath, path);
        SafeFileStorage.AtomicWriteAllText(
            path,
            content,
            isRename ? null : expectedPreviousHash);
        if (!isRename)
        {
            return;
        }

        try
        {
            EnsureUnchanged(previousPath, expectedPreviousHash);
            ArchiveRenamedFile(previousPath, path, folder, expectedPreviousHash);
        }
        catch (Exception archiveError)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception rollbackError)
            {
                throw new IOException(
                    "Nie udało się ukończyć ani wycofać zmiany nazwy pliku.",
                    new AggregateException(archiveError, rollbackError));
            }

            throw new IOException("Nie udało się ukończyć zmiany nazwy pliku; zapis wycofano.", archiveError);
        }
    }

    private static string UniquePath(string folder, string path)
    {
        path = SafeFileStorage.ValidateContainedFilePath(folder, path);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 2;
        string candidate;
        do
        {
            candidate = SafeFileStorage.GetContainedFilePath(folder, $"{name}-{index}{extension}");
            index++;
        }
        while (File.Exists(candidate) || Directory.Exists(candidate));

        return candidate;
    }

    private static void MoveSidecarBackup(
        string source,
        string destination,
        string sourceFolder,
        string destinationFolder)
    {
        var sourceBackup = SafeFileStorage.GetContainedFilePath(sourceFolder, Path.GetFileName(source) + ".bak");
        if (!File.Exists(sourceBackup))
        {
            return;
        }

        var destinationBackup = SafeFileStorage.GetContainedFilePath(
            destinationFolder,
            Path.GetFileName(destination) + ".bak");
        destinationBackup = UniquePath(destinationFolder, destinationBackup);
        File.Move(sourceBackup, destinationBackup, overwrite: false);
    }

    private static void TryMoveSidecarBackup(
        string source,
        string destination,
        string sourceFolder,
        string destinationFolder)
    {
        try
        {
            MoveSidecarBackup(source, destination, sourceFolder, destinationFolder);
        }
        catch
        {
            // The primary file has already been moved. Keep the operation successful and
            // leave the older sidecar in place as an additional manual recovery copy.
        }
    }

    private static void TryDeleteFile(string path)
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
            // Leaving an extra recovery copy is safer than failing the completed operation.
        }
    }
}

internal static class SafeFileStorage
{
    internal static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Ścieżka folderu nie może być pusta.", nameof(path));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static string EnsureContainedDirectory(string root, string directory)
    {
        var rootPath = NormalizeDirectory(root);
        var directoryPath = Path.GetFullPath(directory);
        EnsureUnderRoot(rootPath, directoryPath);
        Directory.CreateDirectory(directoryPath);
        RejectSymbolicLink(directoryPath);
        return directoryPath;
    }

    internal static void ValidateContainedDirectory(string root, string directory)
    {
        var rootPath = NormalizeDirectory(root);
        var directoryPath = Path.GetFullPath(directory);
        EnsureUnderRoot(rootPath, directoryPath);
        RejectSymbolicLink(directoryPath);
    }

    internal static string GetContainedFilePath(string directory, string fileName)
    {
        ValidateFileName(fileName);
        var directoryPath = NormalizeDirectory(directory);
        var path = Path.GetFullPath(Path.Combine(directoryPath, fileName));
        EnsureDirectChild(directoryPath, path);
        RejectSymbolicLink(path);
        return path;
    }

    internal static string ValidateContainedFilePath(string directory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Ścieżka pliku nie może być pusta.", nameof(path));
        }

        var directoryPath = NormalizeDirectory(directory);
        var fullPath = Path.GetFullPath(path);
        EnsureDirectChild(directoryPath, fullPath);
        RejectSymbolicLink(fullPath);
        return fullPath;
    }

    internal static void ValidateFileToken(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.Any(char.IsControl) ||
            value.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 ||
            value.EndsWith(' ') || value.EndsWith('.'))
        {
            throw new InvalidDataException($"Nieprawidłowy {label}: „{value}”.");
        }
    }

    internal static void AtomicWriteAllText(
        string targetPath,
        string content,
        string? expectedExistingHash = null)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Docelowy plik nie ma folderu nadrzędnego.");
        targetPath = ValidateContainedFilePath(directory, targetPath);

        var tempPath = GetContainedFilePath(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = GetContainedFilePath(directory, Path.GetFileName(targetPath) + ".bak");
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
                if (!string.IsNullOrWhiteSpace(expectedExistingHash) &&
                    File.Exists(backupPath) &&
                    !string.Equals(ComputeFileHash(backupPath), expectedExistingHash, StringComparison.Ordinal))
                {
                    RestoreRacedFile(targetPath, backupPath, directory);
                    throw new StorageConflictException(targetPath);
                }
            }
            else
            {
                File.Move(tempPath, targetPath, overwrite: false);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void RestoreRacedFile(string targetPath, string backupPath, string directory)
    {
        var displacedWrite = GetContainedFilePath(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.conflict");
        try
        {
            File.Replace(backupPath, targetPath, displacedWrite, ignoreMetadataErrors: true);
            try
            {
                File.Delete(displacedWrite);
            }
            catch
            {
                // This contains the in-memory draft that is still pending in the app.
                // Leaving it behind is safer than risking either version.
            }
        }
        catch (Exception restoreError)
        {
            throw new IOException(
                $"Wykryto równoczesną zmianę pliku. Obie wersje zachowano jako „{targetPath}” i „{backupPath}”.",
                restoreError);
        }
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);

    internal static void RejectSymbolicLink(string path)
    {
        if ((!File.Exists(path) && !Directory.Exists(path)) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
        {
            return;
        }

        throw new IOException($"Dowiązania symboliczne nie są dozwolone w zarządzanym magazynie: {path}");
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            fileName is "." or "..")
        {
            throw new InvalidDataException($"Nieprawidłowa nazwa pliku: „{fileName}”.");
        }
    }

    private static void EnsureDirectChild(string directory, string path)
    {
        EnsureUnderRoot(directory, path);
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !string.Equals(NormalizeDirectory(parent), directory, PathComparison))
        {
            throw new InvalidDataException($"Ścieżka wychodzi poza dozwolony folder: {path}");
        }
    }

    private static void EnsureUnderRoot(string root, string path)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedPath = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(prefix, PathComparison))
        {
            throw new InvalidDataException($"Ścieżka wychodzi poza katalog danych: {normalizedPath}");
        }
    }
}
