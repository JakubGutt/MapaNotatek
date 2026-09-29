using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using MapaNotatek.Models;
using MapaNotatek.Services;

namespace MapaNotatek.ViewModels;

public enum CenterViewKind
{
    Graph,
    Notes,
    Tasks,
    People
}

public sealed class MainViewModel : ObservableObject
{
    private sealed record PersonAssignmentSnapshot(
        List<string> DocumentPeople,
        List<List<string>> TaskPeople);

    private readonly AppStateStore _stateStore = new();
    private readonly StorageReadIssue? _startupStorageIssue;
    private readonly DispatcherTimer _saveTimer;
    private RevisionStore _revisionStore;
    private readonly Stack<(Action Undo, Action Redo)> _undo = new();
    private readonly Stack<(Action Undo, Action Redo)> _redo = new();
    private bool _suppressUndo;
    private string _searchQuery = string.Empty;
    private CenterViewKind _centerView = CenterViewKind.Notes;
    private bool _isEditorOpen;
    private string? _focusedProjectId;
    private string? _selectedProjectId;
    private string? _selectedNoteId;
    private string? _selectedGraphId;
    private bool _selectedGraphIsProject;
    private string _statusText = string.Empty;
    private readonly Dictionary<string, Note> _pendingSaveNotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Project> _pendingSaveProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Person> _pendingSavePeople = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedNavigationKeys = new(StringComparer.OrdinalIgnoreCase);
    private Exception? _lastSaveError;

    public MainViewModel()
    {
        AppState state;
        try
        {
            state = _stateStore.Load();
        }
        catch (StorageReadException ex)
        {
            _startupStorageIssue = ex.Issue;
            state = _stateStore.RecoverWithFreshState();
        }

        _startupStorageIssue ??= _stateStore.LastLoadIssue;
        State = state;
        Store = new MarkdownStore(State.DataFolder ?? _stateStore.DefaultDataFolder);
        _revisionStore = new RevisionStore(Store.Root);
        Store.EnsureFolders();
        _focusedProjectId = State.FocusedProjectId;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            FlushPendingSaves();
        };
        Reload();
    }

    public MarkdownStore Store { get; }
    public AppState State { get; }
    public List<Project> Projects { get; } = [];
    public List<Note> Notes { get; } = [];
    public List<Person> People { get; } = [];
    public ObservableCollection<Project> VisibleProjects { get; } = [];
    public ObservableCollection<NavigationTreeNode> NavigationTree { get; } = [];
    public ObservableCollection<Note> VisibleNotes { get; } = [];
    public ObservableCollection<OpenTask> VisibleTasks { get; } = [];
    public ObservableCollection<Note> RelatedNotes { get; } = [];
    public ObservableCollection<OpenTask> ProjectNoteTasks { get; } = [];
    public ObservableCollection<SidebarItem> PinnedItems { get; } = [];
    public ObservableCollection<SidebarItem> RecentItems { get; } = [];

    public event Action? GraphChanged;
    public event Action? PeopleChanged;
    public event Action? EditorChanged;
    public event Action? EditorOpenRequested;
    public event Action? SelectionChanged;
    public event Action<string>? FocusNodeRequested;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (Set(ref _searchQuery, value))
            {
                RefreshVisible();
                GraphChanged?.Invoke();
            }
        }
    }

    public CenterViewKind CenterView
    {
        get => _centerView;
        set
        {
            if (Set(ref _centerView, value))
            {
                Raise(nameof(IsGraphView));
                Raise(nameof(IsNotesView));
                Raise(nameof(IsTasksView));
                Raise(nameof(IsPeopleView));
            }
        }
    }

    public bool IsGraphView => CenterView == CenterViewKind.Graph;
    public bool IsNotesView => CenterView == CenterViewKind.Notes;
    public bool IsTasksView => CenterView == CenterViewKind.Tasks;
    public bool IsPeopleView => CenterView == CenterViewKind.People;

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set => Set(ref _isEditorOpen, value);
    }

    public string? FocusedProjectId
    {
        get => _focusedProjectId;
        set
        {
            if (Set(ref _focusedProjectId, value))
            {
                State.FocusedProjectId = value;
                TrySaveState();
                RefreshVisible();
                GraphChanged?.Invoke();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public Project? SelectedProject =>
        _selectedProjectId is null ? null : Projects.FirstOrDefault(p => p.Id == _selectedProjectId);

    public Note? SelectedNote =>
        _selectedNoteId is null ? null : Notes.FirstOrDefault(n => n.Id == _selectedNoteId);

    public string? SelectedGraphId => _selectedGraphId;
    public bool SelectedGraphIsProject => _selectedGraphIsProject;

    public string ThemeName => ApplicationThemeName();

    public string DataFolder => Store.Root;

    public bool HasPendingSaves =>
        _pendingSaveNotes.Count > 0 || _pendingSaveProjects.Count > 0 || _pendingSavePeople.Count > 0;

    public int PendingSaveCount => _pendingSaveNotes.Count + _pendingSaveProjects.Count + _pendingSavePeople.Count;

    public bool HasSaveError => _lastSaveError is not null;

    public bool HasSaveConflict => _lastSaveError is StorageConflictException;

    public string? LastSaveErrorMessage => _lastSaveError?.Message;

    public IReadOnlyList<StorageReadIssue> StorageIssues
    {
        get
        {
            var issues = new List<StorageReadIssue>();
            if (_startupStorageIssue is not null)
            {
                issues.Add(_startupStorageIssue);
            }

            issues.AddRange(Store.ReadIssues);
            return issues
                .GroupBy(issue => (issue.Area, issue.Path, issue.Message))
                .Select(group => group.First())
                .ToList();
        }
    }

    public bool HasStorageIssues => StorageIssues.Count > 0;

    public IEnumerable<Project> GraphProjects => VisibleGraphProjects();
    public IEnumerable<Note> GraphNotes => VisibleGraphNotes();

    public void Reload()
    {
        Projects.Clear();
        Projects.AddRange(Store.LoadProjects());
        Notes.Clear();
        Notes.AddRange(Store.LoadNotes());
        People.Clear();
        People.AddRange(Store.LoadPeople());
        LayoutService.ApplyMissingPositions(Projects, Notes, State.NodePositions);
        RefreshVisible();
        Raise(nameof(StorageIssues));
        Raise(nameof(HasStorageIssues));
        GraphChanged?.Invoke();
        PeopleChanged?.Invoke();
        EditorChanged?.Invoke();
    }

    public Project NewProject(double? x = null, double? y = null, string? parentId = null, bool isFolder = false)
        => NewProject(isFolder ? ProjectItemType.Folder : ProjectItemType.Project, x, y, parentId);

    public Project NewProject(ProjectItemType itemType, double? x = null, double? y = null, string? parentId = null)
    {
        var requestedParentId = parentId ?? FocusedProject()?.Id;
        var actualParentId = requestedParentId;
        var parent = requestedParentId is null
            ? null
            : Projects.FirstOrDefault(candidate => string.Equals(candidate.Id, requestedParentId, StringComparison.OrdinalIgnoreCase));
        var parentRejected = parent is not null && !parent.ItemType.CanContain(itemType);
        if (parentRejected)
        {
            actualParentId = null;
        }

        var project = Store.CreateProject(itemType.DefaultName(), actualParentId, itemType);
        Projects.Add(project);
        PlaceNewNode(project.Id, x, y, preferNearParent: actualParentId);
        LayoutService.ApplyMissingPositions(Projects, Notes, State.NodePositions);
        TrySaveState();
        RefreshVisible();
        SelectProject(project, openEditor: true, focusGraph: true);
        StatusText = parentRejected
            ? $"Utworzono: {itemType.Label()}. Wybrany {parent!.ItemType.Label().ToLowerInvariant()} nie może go zawierać, więc element dodano na poziomie głównym"
            : $"Utworzono: {itemType.Label()}";
        GraphChanged?.Invoke();
        FocusNodeRequested?.Invoke(project.Id);
        return project;
    }

    public Project NewFolder(double? x = null, double? y = null, string? parentId = null) =>
        NewProject(ProjectItemType.Folder, x, y, parentId);

    public Note NewNote(double? x = null, double? y = null)
    {
        List<string> tags = [];
        var focused = FocusedProject();
        if (focused is not null)
        {
            tags.Add(focused.Slug);
        }
        else if (SelectedProject is not null)
        {
            tags.Add(SelectedProject.Slug);
        }

        var note = Store.CreateNote("Nowa notatka", tags);
        Notes.Insert(0, note);
        PlaceNewNode(note.Id, x, y, preferNearParent: focused?.Id ?? SelectedProject?.Id);
        LayoutService.ApplyMissingPositions(Projects, Notes, State.NodePositions);
        TrySaveState();
        RefreshVisible();
        SelectNote(note, openEditor: true, focusGraph: true);
        StatusText = "Utworzono notatkę";
        GraphChanged?.Invoke();
        FocusNodeRequested?.Invoke(note.Id);
        return note;
    }

    public Person NewPerson()
    {
        var person = Store.CreatePerson("Nowa osoba");
        People.Add(person);
        People.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        StatusText = "Dodano osobę";
        PeopleChanged?.Invoke();
        return person;
    }

    public (int Projects, int Notes, int Tasks) CountPersonAssignments(Person person)
    {
        var projectCount = Projects.Count(project => PersonTagService.Contains(project.People, person.Slug));
        var noteCount = Notes.Count(note => PersonTagService.Contains(note.People, person.Slug));
        var taskCount = Projects.SelectMany(project => project.Checklist)
            .Concat(Notes.SelectMany(note => note.Checklist))
            .Count(task => PersonTagService.Contains(task.People, person.Slug));
        return (projectCount, noteCount, taskCount);
    }

    public bool DeletePerson(Person person)
    {
        if (!FlushPendingSaves())
        {
            StatusText = "Nie przeniesiono osoby do kosza, ponieważ nie udało się zapisać zmian";
            return false;
        }

        var affectedProjects = Projects
            .Where(project => PersonTagService.Contains(project.People, person.Slug) ||
                              project.Checklist.Any(task => PersonTagService.Contains(task.People, person.Slug)))
            .ToList();
        var affectedNotes = Notes
            .Where(note => PersonTagService.Contains(note.People, person.Slug) ||
                           note.Checklist.Any(task => PersonTagService.Contains(task.People, person.Slug)))
            .ToList();
        var projectSnapshots = affectedProjects.ToDictionary(
            project => project,
            project => new PersonAssignmentSnapshot(
                project.People.ToList(),
                project.Checklist.Select(task => task.People.ToList()).ToList()));
        var noteSnapshots = affectedNotes.ToDictionary(
            note => note,
            note => new PersonAssignmentSnapshot(
                note.People.ToList(),
                note.Checklist.Select(task => task.People.ToList()).ToList()));

        try
        {
            Store.MovePersonToTrash(person);
            foreach (var project in affectedProjects)
            {
                RemovePersonAssignment(project.People, project.Checklist, person.Slug);
                SaveProjectWithHistory(project);
            }

            foreach (var note in affectedNotes)
            {
                RemovePersonAssignment(note.People, note.Checklist, person.Slug);
                SaveNoteWithHistory(note);
            }
        }
        catch (Exception operationError)
        {
            var rollbackErrors = new List<Exception>();
            try
            {
                if (person.FilePath.StartsWith(Store.TrashedPeopleFolder, StringComparison.OrdinalIgnoreCase))
                {
                    Store.RestorePersonFromTrash(person);
                }
            }
            catch (Exception rollbackError)
            {
                rollbackErrors.Add(rollbackError);
            }

            RestorePersonAssignments(projectSnapshots);
            RestorePersonAssignments(noteSnapshots);
            foreach (var project in affectedProjects)
            {
                try
                {
                    SaveProjectWithHistory(project);
                }
                catch (Exception rollbackError)
                {
                    _pendingSaveProjects[project.Id] = project;
                    rollbackErrors.Add(rollbackError);
                }
            }

            foreach (var note in affectedNotes)
            {
                try
                {
                    SaveNoteWithHistory(note);
                }
                catch (Exception rollbackError)
                {
                    _pendingSaveNotes[note.Id] = note;
                    rollbackErrors.Add(rollbackError);
                }
            }

            var error = rollbackErrors.Count == 0
                ? operationError
                : new IOException(
                    "Nie udało się usunąć osoby ani w pełni wycofać zmian przypisań.",
                    new AggregateException([operationError, .. rollbackErrors]));
            RecordSaveFailure(error);
            PeopleChanged?.Invoke();
            EditorChanged?.Invoke();
            return false;
        }

        _pendingSavePeople.Remove(person.Id);
        People.Remove(person);
        RefreshVisible();
        RefreshRelated();
        PeopleChanged?.Invoke();
        GraphChanged?.Invoke();
        EditorChanged?.Invoke();
        StatusText = $"Osobę „{person.Name}” przeniesiono do kosza i usunięto jej przypisania";
        return true;
    }

    private static void RemovePersonAssignment(
        List<string> documentPeople,
        IEnumerable<ChecklistItem> checklist,
        string slug)
    {
        documentPeople.RemoveAll(value => string.Equals(value, slug, StringComparison.OrdinalIgnoreCase));
        foreach (var task in checklist)
        {
            task.People.RemoveAll(value => string.Equals(value, slug, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void RestorePersonAssignments<T>(
        IReadOnlyDictionary<T, PersonAssignmentSnapshot> snapshots) where T : class
    {
        foreach (var (source, snapshot) in snapshots)
        {
            var (documentPeople, checklist) = source switch
            {
                Project project => (project.People, project.Checklist),
                Note note => (note.People, note.Checklist),
                _ => throw new InvalidOperationException("Nieobsługiwane źródło przypisania osoby.")
            };
            documentPeople.Clear();
            documentPeople.AddRange(snapshot.DocumentPeople);
            for (var index = 0; index < checklist.Count && index < snapshot.TaskPeople.Count; index++)
            {
                checklist[index].People = snapshot.TaskPeople[index].ToList();
            }
        }
    }

    private void PlaceNewNode(string id, double? x, double? y, string? preferNearParent)
    {
        // Nested items always fan out around the parent — never stack on the same viewport point.
        if (preferNearParent is not null &&
            State.NodePositions.TryGetValue(preferNearParent, out var parentPos))
        {
            var siblingIndex = LayoutService.CountSiblings(Projects, preferNearParent, id);
            LayoutService.PlaceNear(State.NodePositions, id, parentPos.X, parentPos.Y, siblingIndex);
            return;
        }

        if (x is not null && y is not null)
        {
            LayoutService.PlaceAtAvoidingOverlap(State.NodePositions, id, x.Value, y.Value);
            return;
        }

        LayoutService.PlaceAtAvoidingOverlap(
            State.NodePositions,
            id,
            LayoutService.CanvasWidth / 2,
            LayoutService.CanvasHeight / 2);
    }

    public void SelectProject(Project project, bool openEditor, bool focusGraph, bool filterToProject = false)
    {
        FlushPendingSaves();
        _selectedProjectId = project.Id;
        _selectedNoteId = null;
        if (focusGraph)
        {
            _selectedGraphId = project.Id;
            _selectedGraphIsProject = true;
        }

        if (filterToProject)
        {
            FocusedProjectId = project.Id;
        }

        IsEditorOpen = openEditor;
        Raise(nameof(SelectedProject));
        Raise(nameof(SelectedNote));
        TrackRecent(project.Id);
        RefreshRelated();
        EditorChanged?.Invoke();
        if (openEditor)
        {
            EditorOpenRequested?.Invoke();
        }

        SelectionChanged?.Invoke();
        GraphChanged?.Invoke();
    }

    public void SelectNote(Note note, bool openEditor, bool focusGraph)
    {
        FlushPendingSaves();
        _selectedNoteId = note.Id;
        _selectedProjectId = null;
        if (focusGraph)
        {
            _selectedGraphId = note.Id;
            _selectedGraphIsProject = false;
        }

        IsEditorOpen = openEditor;
        Raise(nameof(SelectedProject));
        Raise(nameof(SelectedNote));
        TrackRecent(note.Id);
        RefreshRelated();
        EditorChanged?.Invoke();
        if (openEditor)
        {
            EditorOpenRequested?.Invoke();
        }

        SelectionChanged?.Invoke();
        GraphChanged?.Invoke();
    }

    public void SelectGraphNode(string id, bool isProject)
    {
        _selectedGraphId = id;
        _selectedGraphIsProject = isProject;
        SelectionChanged?.Invoke();
        // Do not raise GraphChanged — a full Refresh would recreate nodes and break drag.
    }

    public void ClearGraphSelection()
    {
        _selectedGraphId = null;
        _selectedGraphIsProject = false;
        SelectionChanged?.Invoke();
    }

    public void OpenSelectedGraphNode()
    {
        if (_selectedGraphId is null)
        {
            return;
        }

        if (_selectedGraphIsProject)
        {
            var project = Projects.FirstOrDefault(p => p.Id == _selectedGraphId);
            if (project is not null)
            {
                SelectProject(project, openEditor: true, focusGraph: true, filterToProject: false);
            }
        }
        else
        {
            var note = Notes.FirstOrDefault(n => n.Id == _selectedGraphId);
            if (note is not null)
            {
                SelectNote(note, openEditor: true, focusGraph: true);
            }
        }
    }

    public void CloseEditor()
    {
        FlushPendingSaves();
        IsEditorOpen = false;
        EditorChanged?.Invoke();
    }

    public void ShowAllProjects()
    {
        FocusedProjectId = null;
        StatusText = "Widok całej struktury";
    }

    public void MoveNode(string id, double x, double y, bool recordUndo)
    {
        var current = LayoutService.Get(State.NodePositions, id);
        var oldX = current.X;
        var oldY = current.Y;
        current.X = x;
        current.Y = y;
        current.Id = id;
        State.NodePositions[id] = current;
        if (recordUndo && (Math.Abs(oldX - x) > 1 || Math.Abs(oldY - y) > 1))
        {
            PushUndo(
                () =>
                {
                    current.X = oldX;
                    current.Y = oldY;
                    SaveState();
                    GraphChanged?.Invoke();
                },
                () =>
                {
                    current.X = x;
                    current.Y = y;
                    SaveState();
                    GraphChanged?.Invoke();
                });
        }

        TrySaveState();
    }

    public void ScheduleSaveNote(Note note)
    {
        note.Modified = DateTimeOffset.Now;
        _pendingSaveNotes[note.Id] = note;
        Raise(nameof(HasPendingSaves));
        Raise(nameof(PendingSaveCount));
        _saveTimer.Stop();
        _saveTimer.Start();
        StatusText = "Zapisywanie…";
    }

    public void ScheduleSaveProject(Project project)
    {
        project.Modified = DateTimeOffset.Now;
        _pendingSaveProjects[project.Id] = project;
        Raise(nameof(HasPendingSaves));
        Raise(nameof(PendingSaveCount));
        _saveTimer.Stop();
        _saveTimer.Start();
        StatusText = "Zapisywanie…";
    }

    public void ScheduleSavePerson(Person person)
    {
        person.Modified = DateTimeOffset.Now;
        _pendingSavePeople[person.Id] = person;
        Raise(nameof(HasPendingSaves));
        Raise(nameof(PendingSaveCount));
        _saveTimer.Stop();
        _saveTimer.Start();
        StatusText = "Zapisywanie…";
    }

    public List<string> ResolvePeopleAssignments(string? value) =>
        PersonTagService.Resolve(value, People);

    public List<string> KeepRegisteredPeople(IEnumerable<string>? values) =>
        PersonTagService.KeepRegistered(values, People);

    public void UpdateProjectPeople(Project project, string? value)
    {
        var resolved = ResolvePeopleAssignments(value);
        if (project.People.SequenceEqual(resolved, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        project.People = resolved;
        ScheduleSaveProject(project);
        PeopleChanged?.Invoke();
    }

    public void UpdateNotePeople(Note note, string? value)
    {
        var resolved = ResolvePeopleAssignments(value);
        if (note.People.SequenceEqual(resolved, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        note.People = resolved;
        ScheduleSaveNote(note);
        PeopleChanged?.Invoke();
    }

    public void NotifyPeopleAssignmentsChanged() => PeopleChanged?.Invoke();

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (FlushPendingSaves())
        {
            StatusText = SavedStatus();
        }
    }

    public IReadOnlyList<RevisionInfo> GetSelectedNoteRevisions()
    {
        var note = SelectedNote;
        return note is null ? [] : _revisionStore.List("Notes", note.Id);
    }

    public string GetNoteRevisionPreview(RevisionInfo revision)
    {
        var parsed = FrontMatter.Parse(_revisionStore.Read(revision));
        var title = string.IsNullOrWhiteSpace(parsed.Title) ? "Bez tytułu" : parsed.Title;
        var tags = FrontMatter.SplitTags(parsed["tags"]);
        var tagLine = tags.Count == 0 ? string.Empty : $"Tagi: {string.Join(", ", tags)}\n\n";
        return $"{title}\n\n{tagLine}{parsed.Body}".TrimEnd();
    }

    public void RestoreSelectedNoteRevision(RevisionInfo revision)
    {
        var note = SelectedNote ?? throw new InvalidOperationException("Nie wybrano notatki.");
        if (!FlushPendingSaves())
        {
            throw new IOException("Nie można przywrócić wersji, dopóki bieżące zmiany nie zostaną bezpiecznie zapisane.");
        }

        var parsed = FrontMatter.Parse(_revisionStore.Read(revision));
        var previousTitle = note.Title;
        var previousBody = note.Body;
        var previousTags = note.Tags;
        var previousPeople = note.People;
        var previousChecklist = note.Checklist;
        var previousModified = note.Modified;
        var previousPath = note.FilePath;
        var previousHash = note.PersistedContentHash;

        try
        {
            note.Title = string.IsNullOrWhiteSpace(parsed.Title) ? note.Title : parsed.Title;
            note.Body = parsed.Body;
            note.Tags = FrontMatter.SplitTags(parsed["tags"]);
            note.People = PersonTagService.Parse(parsed["people"]);
            note.Checklist = parsed.Checklist;
            SaveNoteWithHistory(note);
        }
        catch
        {
            note.Title = previousTitle;
            note.Body = previousBody;
            note.Tags = previousTags;
            note.People = previousPeople;
            note.Checklist = previousChecklist;
            note.Modified = previousModified;
            note.FilePath = previousPath;
            note.PersistedContentHash = previousHash;
            throw;
        }

        RefreshVisible();
        RefreshRelated();
        GraphChanged?.Invoke();
        EditorChanged?.Invoke();
        StatusText = $"Przywrócono wersję z {revision.TimestampUtc.ToLocalTime():g}";
    }

    public bool IsEmptyWorkspace => Projects.Count == 0 && Notes.Count == 0 && People.Count == 0;

    private static string SavedStatus() => $"Zapisano {DateTime.Now:HH:mm}";

    public void RenameSelected(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (SelectedProject is { } project)
        {
            var previous = project.Name;
            project.Name = name.Trim();
            try
            {
                SaveProjectWithHistory(project);
            }
            catch (Exception ex)
            {
                _pendingSaveProjects[project.Id] = project;
                RecordSaveFailure(ex);
                EditorChanged?.Invoke();
                return;
            }

            PushUndo(
                () =>
                {
                    project.Name = previous;
                    SaveProjectWithHistory(project);
                    Reload();
                },
                () =>
                {
                    project.Name = name.Trim();
                    SaveProjectWithHistory(project);
                    Reload();
                });
            StatusText = SavedStatus();
            EditorChanged?.Invoke();
            return;
        }

        if (SelectedNote is { } note)
        {
            var previous = note.Title;
            note.Title = name.Trim();
            try
            {
                SaveNoteWithHistory(note);
            }
            catch (Exception ex)
            {
                _pendingSaveNotes[note.Id] = note;
                RecordSaveFailure(ex);
                EditorChanged?.Invoke();
                return;
            }

            PushUndo(
                () =>
                {
                    note.Title = previous;
                    SaveNoteWithHistory(note);
                    Reload();
                },
                () =>
                {
                    note.Title = name.Trim();
                    SaveNoteWithHistory(note);
                    Reload();
                });
            StatusText = SavedStatus();
            EditorChanged?.Invoke();
        }
    }

    public void TrashSelectedNote()
    {
        var note = SelectedNote;
        if (note is null && !_selectedGraphIsProject && _selectedGraphId is not null)
        {
            note = Notes.FirstOrDefault(n => n.Id == _selectedGraphId);
        }

        if (note is null)
        {
            return;
        }

        if (!FlushPendingSaves())
        {
            StatusText = "Nie przeniesiono notatki do kosza, ponieważ nie udało się bezpiecznie zapisać zmian";
            return;
        }

        try
        {
            Store.MoveNoteToTrash(note);
        }
        catch (Exception ex)
        {
            RecordSaveFailure(ex);
            return;
        }

        Notes.Remove(note);
        var snapshot = note;
        PushUndo(
            () =>
            {
                Store.RestoreNoteFromTrash(snapshot);
                if (!Notes.Contains(snapshot))
                {
                    Notes.Insert(0, snapshot);
                }

                Reload();
            },
            () =>
            {
                Store.MoveNoteToTrash(snapshot);
                Notes.Remove(snapshot);
                Reload();
            });
        _selectedNoteId = null;
        IsEditorOpen = false;
        RefreshVisible();
        var stateSaved = true;
        try
        {
            SaveState();
        }
        catch (Exception ex)
        {
            stateSaved = false;
            RecordSaveFailure(ex);
        }
        if (stateSaved)
        {
            StatusText = "Notatka przeniesiona do kosza";
        }
        GraphChanged?.Invoke();
        EditorChanged?.Invoke();
    }

    public void ToggleTask(OpenTask task)
    {
        var wasDone = task.Item.IsDone;
        task.Item.IsDone = !wasDone;
        if (task.IsProject)
        {
            var project = Projects.FirstOrDefault(p => p.Id == task.SourceId);
            if (project is not null)
            {
                try
                {
                    SaveProjectWithHistory(project);
                }
                catch (Exception ex)
                {
                    _pendingSaveProjects[project.Id] = project;
                    RecordSaveFailure(ex);
                    RefreshVisible();
                    EditorChanged?.Invoke();
                    return;
                }
            }
        }
        else
        {
            var note = Notes.FirstOrDefault(n => n.Id == task.SourceId);
            if (note is not null)
            {
                try
                {
                    SaveNoteWithHistory(note);
                }
                catch (Exception ex)
                {
                    _pendingSaveNotes[note.Id] = note;
                    RecordSaveFailure(ex);
                    RefreshVisible();
                    EditorChanged?.Invoke();
                    return;
                }
            }
        }

        PushUndo(
            () =>
            {
                task.Item.IsDone = wasDone;
                SaveTaskSource(task);
                RefreshVisible();
                RefreshRelated();
                EditorChanged?.Invoke();
            },
            () =>
            {
                task.Item.IsDone = !wasDone;
                SaveTaskSource(task);
                RefreshVisible();
                RefreshRelated();
                EditorChanged?.Invoke();
            });
        RefreshVisible();
        RefreshRelated();
        EditorChanged?.Invoke();
    }

    public void UpdateTaskPeople(OpenTask task, string? people)
    {
        var normalized = ResolvePeopleAssignments(people);
        if (task.Item.People.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        task.Item.People = normalized;
        if (task.IsProject)
        {
            var project = Projects.FirstOrDefault(candidate => candidate.Id == task.SourceId);
            if (project is not null)
            {
                ScheduleSaveProject(project);
            }
        }
        else
        {
            var note = Notes.FirstOrDefault(candidate => candidate.Id == task.SourceId);
            if (note is not null)
            {
                ScheduleSaveNote(note);
            }
        }

        PeopleChanged?.Invoke();
    }

    public bool MoveTask(OpenTask movedTask, OpenTask targetTask, bool placeAfter)
    {
        if (ReferenceEquals(movedTask.Item, targetTask.Item))
        {
            return false;
        }

        var ordered = CollectOpenTasks(Notes, Projects).ToList();
        var originalOrder = ordered.Select(task => task.Item).ToList();
        var movedIndex = ordered.FindIndex(task => ReferenceEquals(task.Item, movedTask.Item));
        if (movedIndex < 0)
        {
            return false;
        }

        var moved = ordered[movedIndex];
        ordered.RemoveAt(movedIndex);
        var targetIndex = ordered.FindIndex(task => ReferenceEquals(task.Item, targetTask.Item));
        if (targetIndex < 0)
        {
            return false;
        }

        ordered.Insert(targetIndex + (placeAfter ? 1 : 0), moved);
        if (ordered.Select(task => task.Item).SequenceEqual(originalOrder))
        {
            return false;
        }

        var previousPriorities = ordered.ToDictionary(task => task.Item, task => task.Item.Priority);
        var newPriorities = ordered
            .Select((task, index) => (task.Item, Priority: (int?)(index * 100)))
            .ToDictionary(pair => pair.Item, pair => pair.Priority);

        if (!ApplyTaskPriorities(newPriorities))
        {
            return false;
        }

        PushUndo(
            () => ApplyTaskPriorities(previousPriorities),
            () => ApplyTaskPriorities(newPriorities));
        StatusText = "Zmieniono priorytet zadania";
        return true;
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _suppressUndo = true;
        var action = _undo.Pop();
        try
        {
            action.Undo();
            _redo.Push(action);
            StatusText = "Cofnięto";
        }
        catch (Exception ex)
        {
            _undo.Push(action);
            RecordSaveFailure(ex);
        }
        finally
        {
            _suppressUndo = false;
        }
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _suppressUndo = true;
        var action = _redo.Pop();
        try
        {
            action.Redo();
            _undo.Push(action);
            StatusText = "Ponowiono";
        }
        catch (Exception ex)
        {
            _redo.Push(action);
            RecordSaveFailure(ex);
        }
        finally
        {
            _suppressUndo = false;
        }
    }

    public void ChangeDataFolder(string folder, bool loadLibraryState = false)
    {
        if (!FlushPendingSaves())
        {
            throw new IOException("Nie można zmienić biblioteki, dopóki niezapisane zmiany nie zostaną bezpiecznie zapisane.");
        }

        LibraryFolderService.EnsureCanOpen(folder, Store.Root);
        var fullPath = Path.GetFullPath(folder);
        AppState? libraryState = null;
        if (loadLibraryState && File.Exists(Path.Combine(fullPath, "app-state.json")))
        {
            libraryState = new AppStateStore(fullPath).Load();
        }

        // Read the candidate before touching the active library. This catches inaccessible
        // folders and fatal parse/enumeration errors while rollback is still trivial.
        var candidateStore = new MarkdownStore(fullPath);
        _ = candidateStore.LoadProjects();
        _ = candidateStore.LoadNotes();

        var previousRoot = Store.Root;
        var previousState = CloneState(State);
        var previousFocusedProjectId = _focusedProjectId;
        try
        {
            Store.SetRoot(fullPath);
            _revisionStore = new RevisionStore(Store.Root);
            if (libraryState is not null)
            {
                CopyState(libraryState, State);
                _focusedProjectId = libraryState.FocusedProjectId;
            }

            State.DataFolder = fullPath;
            Reload();
            SaveState();
        }
        catch
        {
            CopyState(previousState, State);
            _focusedProjectId = previousFocusedProjectId;
            Store.SetRoot(previousRoot);
            _revisionStore = new RevisionStore(Store.Root);
            Reload();
            throw;
        }

        StatusText = libraryState is null
            ? "Otwarto lokalną bibliotekę"
            : "Przywrócono bibliotekę wraz z układem i przypiętymi elementami";
    }

    private static AppState CloneState(AppState state) => new()
    {
        DataFolder = state.DataFolder,
        Zoom = state.Zoom,
        NodePositions = state.NodePositions.ToDictionary(
            pair => pair.Key,
            pair => new GraphPosition
            {
                Id = pair.Value.Id,
                X = pair.Value.X,
                Y = pair.Value.Y
            },
            StringComparer.OrdinalIgnoreCase),
        FocusedProjectId = state.FocusedProjectId,
        EditorFont = state.EditorFont,
        EditorFontSize = state.EditorFontSize,
        SidebarVisible = state.SidebarVisible,
        EditorDetailsVisible = state.EditorDetailsVisible,
        PinnedIds = state.PinnedIds.ToList(),
        RecentIds = state.RecentIds.ToList()
    };

    private static void CopyState(AppState source, AppState destination)
    {
        var clone = CloneState(source);
        destination.DataFolder = clone.DataFolder;
        destination.Zoom = clone.Zoom;
        destination.NodePositions = clone.NodePositions;
        destination.FocusedProjectId = clone.FocusedProjectId;
        destination.EditorFont = clone.EditorFont;
        destination.EditorFontSize = clone.EditorFontSize;
        destination.SidebarVisible = clone.SidebarVisible;
        destination.EditorDetailsVisible = clone.EditorDetailsVisible;
        destination.PinnedIds = clone.PinnedIds;
        destination.RecentIds = clone.RecentIds;
    }

    public void SaveZoom(double zoom)
    {
        State.Zoom = zoom;
        TrySaveState();
    }

    public void SaveEditorAppearance(string font, double fontSize)
    {
        State.EditorFont = font is "Inter" or "Szeryfowa" or "Monospace" ? font : "Inter";
        State.EditorFontSize = Math.Clamp(fontSize, 14, 18);
        TrySaveState();
    }

    public void SaveSidebarVisibility(bool visible)
    {
        State.SidebarVisible = visible;
        TrySaveState();
    }

    public void SaveEditorDetailsVisibility(bool visible)
    {
        State.EditorDetailsVisible = visible;
        TrySaveState();
    }

    public bool FlushPendingSaves()
    {
        _saveTimer.Stop();
        var changed = false;
        Exception? firstError = null;

        foreach (var note in _pendingSaveNotes.Values.ToList())
        {
            try
            {
                SaveNoteWithHistory(note);
                _pendingSaveNotes.Remove(note.Id);
                changed = true;
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        foreach (var project in _pendingSaveProjects.Values.ToList())
        {
            try
            {
                SaveProjectWithHistory(project);
                _pendingSaveProjects.Remove(project.Id);
                changed = true;
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        foreach (var person in _pendingSavePeople.Values.ToList())
        {
            try
            {
                Store.SavePerson(person);
                _pendingSavePeople.Remove(person.Id);
                changed = true;
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        if (changed)
        {
            RefreshVisible();
            RefreshRelated();
            GraphChanged?.Invoke();
            PeopleChanged?.Invoke();
        }

        Raise(nameof(HasPendingSaves));
        Raise(nameof(PendingSaveCount));

        if (firstError is null)
        {
            _lastSaveError = null;
            Raise(nameof(HasSaveError));
            Raise(nameof(HasSaveConflict));
            Raise(nameof(LastSaveErrorMessage));
            if (changed)
            {
                StatusText = SavedStatus();
            }

            return true;
        }

        _lastSaveError = firstError;
        Raise(nameof(HasSaveError));
        Raise(nameof(HasSaveConflict));
        Raise(nameof(LastSaveErrorMessage));
        StatusText = $"Nie udało się zapisać wszystkich zmian: {firstError.Message}";
        return false;
    }

    private void RecordSaveFailure(Exception error)
    {
        _lastSaveError = error;
        Raise(nameof(HasPendingSaves));
        Raise(nameof(PendingSaveCount));
        Raise(nameof(HasSaveError));
        Raise(nameof(HasSaveConflict));
        Raise(nameof(LastSaveErrorMessage));
        StatusText = $"Nie udało się bezpiecznie zapisać zmiany: {error.Message}";
    }

    public int PreservePendingChangesAsCopies()
    {
        if (!HasSaveConflict)
        {
            return 0;
        }

        var copies = 0;
        string? firstCopyId = null;
        var firstCopyIsProject = false;
        foreach (var original in _pendingSaveNotes.Values.ToList())
        {
            var copy = Store.CreateNote(original.Title + " — kopia lokalna", original.Tags);
            copy.Body = original.Body;
            copy.People = original.People.ToList();
            copy.Checklist = original.Checklist
                .Select(item => new ChecklistItem
                {
                    Text = item.Text,
                    IsDone = item.IsDone,
                    People = item.People.ToList(),
                    Priority = item.Priority
                })
                .ToList();
            Store.SaveNote(copy);
            firstCopyId ??= copy.Id;
            _pendingSaveNotes.Remove(original.Id);
            copies++;
        }

        foreach (var original in _pendingSaveProjects.Values.ToList())
        {
            var copy = Store.CreateProject(
                original.Name + " — kopia lokalna",
                original.ParentId,
                original.ItemType);
            copy.Description = original.Description;
            copy.People = original.People.ToList();
            copy.Checklist = original.Checklist
                .Select(item => new ChecklistItem
                {
                    Text = item.Text,
                    IsDone = item.IsDone,
                    People = item.People.ToList(),
                    Priority = item.Priority
                })
                .ToList();
            Store.SaveProject(copy);
            if (firstCopyId is null)
            {
                firstCopyId = copy.Id;
                firstCopyIsProject = true;
            }

            _pendingSaveProjects.Remove(original.Id);
            copies++;
        }

        foreach (var original in _pendingSavePeople.Values.ToList())
        {
            var copy = Store.CreatePerson(original.Name + " — kopia lokalna");
            copy.Role = original.Role;
            copy.Description = original.Description;
            copy.AvatarPath = original.AvatarPath;
            Store.SavePerson(copy);
            _pendingSavePeople.Remove(original.Id);
            copies++;
        }

        _lastSaveError = null;
        Reload();
        if (firstCopyId is not null)
        {
            if (firstCopyIsProject)
            {
                var project = Projects.FirstOrDefault(item => item.Id == firstCopyId);
                if (project is not null)
                {
                    SelectProject(project, openEditor: true, focusGraph: true);
                }
            }
            else
            {
                var note = Notes.FirstOrDefault(item => item.Id == firstCopyId);
                if (note is not null)
                {
                    SelectNote(note, openEditor: true, focusGraph: true);
                }
            }
        }

        Raise(nameof(HasPendingSaves));
        Raise(nameof(PendingSaveCount));
        Raise(nameof(HasSaveError));
        Raise(nameof(HasSaveConflict));
        Raise(nameof(LastSaveErrorMessage));
        StatusText = copies == 1
            ? "Niezapisane zmiany zachowano jako osobną kopię lokalną"
            : $"Niezapisane zmiany zachowano jako {copies} osobne kopie lokalne";
        return copies;
    }

    public void RestoreNoteFromTrash(Note note)
    {
        Store.RestoreNoteFromTrash(note);
        if (!Notes.Any(n => n.Id == note.Id))
        {
            Notes.Insert(0, note);
        }

        Reload();
        SelectNote(note, openEditor: true, focusGraph: true);
        StatusText = "Przywrócono z kosza";
    }

    public IReadOnlyList<Note> LoadTrashedNotes() => Store.LoadTrashedNotes();

    public IReadOnlyList<Project> LoadTrashedProjects() => Store.LoadTrashedProjects();

    public IReadOnlyList<Person> LoadTrashedPeople() => Store.LoadTrashedPeople();

    public void RestoreProjectFromTrash(Project project)
    {
        Store.RestoreProjectFromTrash(project);
        Reload();
        var restored = Projects.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, project.Id, StringComparison.OrdinalIgnoreCase));
        if (restored is not null)
        {
            SelectProject(restored, openEditor: true, focusGraph: true);
        }

        StatusText = "Projekt przywrócono z kosza";
    }

    public void RestorePersonFromTrash(Person person)
    {
        Store.RestorePersonFromTrash(person);
        Reload();
        StatusText = "Osobę przywrócono z kosza; wcześniejsze przypisania nie zostały odtworzone";
    }

    public void TogglePinSelected()
    {
        var id = SelectedNote?.Id ?? SelectedProject?.Id ?? SelectedGraphId;
        if (id is null)
        {
            return;
        }

        if (State.PinnedIds.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            State.PinnedIds.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            StatusText = "Odpięto";
        }
        else
        {
            State.PinnedIds.Insert(0, id);
            StatusText = "Przypięto";
        }

        TrySaveState();
        RefreshPinnedAndRecent();
    }

    public bool IsPinned(string id) =>
        State.PinnedIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    public void SetProjectParent(Project project, string? parentId)
    {
        if (parentId is not null &&
            (string.Equals(project.Id, parentId, StringComparison.OrdinalIgnoreCase) ||
             IsAncestor(project.Id, parentId)))
        {
            StatusText = "Nie można utworzyć cyklu w hierarchii";
            return;
        }

        var parent = parentId is null
            ? null
            : Projects.FirstOrDefault(candidate => string.Equals(candidate.Id, parentId, StringComparison.OrdinalIgnoreCase));
        if (parentId is not null && parent is null)
        {
            StatusText = "Nie znaleziono elementu nadrzędnego";
            return;
        }

        if (parent is not null && !parent.ItemType.CanContain(project.ItemType))
        {
            StatusText = $"{parent.ItemType.Label()} nie może zawierać elementu typu {project.ItemType.Label().ToLowerInvariant()}";
            return;
        }

        var oldParent = project.ParentId;
        project.ParentId = parentId;
        try
        {
            SaveProjectWithHistory(project);
        }
        catch (Exception ex)
        {
            _pendingSaveProjects[project.Id] = project;
            RecordSaveFailure(ex);
            RefreshVisible();
            GraphChanged?.Invoke();
            return;
        }
        PushUndo(
            () =>
            {
                project.ParentId = oldParent;
                SaveProjectWithHistory(project);
                RefreshVisible();
                GraphChanged?.Invoke();
            },
            () =>
            {
                project.ParentId = parentId;
                SaveProjectWithHistory(project);
                RefreshVisible();
                GraphChanged?.Invoke();
            });
        RefreshVisible();
        GraphChanged?.Invoke();
        StatusText = parentId is null ? "Przeniesiono na poziom główny" : "Zmieniono element nadrzędny";
    }

    public bool CanSetProjectParent(Project project, Project parent) =>
        !string.Equals(project.Id, parent.Id, StringComparison.OrdinalIgnoreCase) &&
        !IsAncestor(project.Id, parent.Id) &&
        parent.ItemType.CanContain(project.ItemType);

    public void AttachNoteToProject(Note note, Project project)
    {
        var slug = project.Slug;
        if (note.Tags.Contains(slug, StringComparer.OrdinalIgnoreCase))
        {
            StatusText = "Notatka już w tym projekcie";
            return;
        }

        note.Tags.Add(slug);
        ScheduleSaveNote(note);
        RefreshVisible();
        RefreshRelated();
        GraphChanged?.Invoke();
        StatusText = $"Dodano notatkę do „{project.Name}”";
    }

    public void DeleteSelectedProject()
    {
        var project = SelectedProject;
        if (project is null && _selectedGraphIsProject && _selectedGraphId is not null)
        {
            project = Projects.FirstOrDefault(p => p.Id == _selectedGraphId);
        }

        if (project is not null)
        {
            DeleteProject(project);
        }
    }

    public void DeleteProject(Project project)
    {
        if (!FlushPendingSaves())
        {
            StatusText = "Nie przeniesiono projektu do kosza, ponieważ nie udało się zapisać zmian";
            return;
        }

        var parentId = project.ParentId;
        var parent = parentId is null
            ? null
            : Projects.FirstOrDefault(candidate => string.Equals(candidate.Id, parentId, StringComparison.OrdinalIgnoreCase));
        var children = Projects.Where(p =>
            string.Equals(p.ParentId, project.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        var savedChildren = new List<Project>();
        try
        {
            foreach (var child in children)
            {
                child.ParentId = parent is not null && parent.ItemType.CanContain(child.ItemType)
                    ? parentId
                    : null;
                SaveProjectWithHistory(child);
                savedChildren.Add(child);
            }

            Store.MoveProjectToTrash(project);
        }
        catch (Exception operationError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var child in children)
            {
                child.ParentId = project.Id;
            }

            foreach (var child in savedChildren)
            {
                try
                {
                    SaveProjectWithHistory(child);
                }
                catch (Exception rollbackError)
                {
                    _pendingSaveProjects[child.Id] = child;
                    rollbackErrors.Add(rollbackError);
                }
            }

            var error = rollbackErrors.Count == 0
                ? operationError
                : new IOException(
                    "Nie udało się przenieść projektu do kosza ani w pełni wycofać zmian dzieci.",
                    new AggregateException([operationError, .. rollbackErrors]));
            RecordSaveFailure(error);
            RefreshVisible();
            GraphChanged?.Invoke();
            EditorChanged?.Invoke();
            return;
        }

        Projects.Remove(project);
        State.NodePositions.Remove(project.Id);
        State.PinnedIds.RemoveAll(x => string.Equals(x, project.Id, StringComparison.OrdinalIgnoreCase));
        State.RecentIds.RemoveAll(x => string.Equals(x, project.Id, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_focusedProjectId, project.Id, StringComparison.OrdinalIgnoreCase))
        {
            _focusedProjectId = null;
            State.FocusedProjectId = null;
        }

        if (string.Equals(_selectedProjectId, project.Id, StringComparison.OrdinalIgnoreCase))
        {
            _selectedProjectId = null;
        }

        if (string.Equals(_selectedGraphId, project.Id, StringComparison.OrdinalIgnoreCase))
        {
            _selectedGraphId = null;
        }

        IsEditorOpen = false;
        var stateSaved = true;
        try
        {
            SaveState();
        }
        catch (Exception ex)
        {
            stateSaved = false;
            RecordSaveFailure(ex);
        }
        RefreshVisible();
        GraphChanged?.Invoke();
        EditorChanged?.Invoke();
        if (stateSaved)
        {
            StatusText = $"{project.ItemType.Label()} „{project.Name}” przeniesiono do kosza (dzieci przeniesiono wyżej)";
        }
    }

    public bool OpenWikiLink(string title)
    {
        var note = Notes.FirstOrDefault(n =>
            string.Equals(n.Title, title, StringComparison.CurrentCultureIgnoreCase));
        if (note is not null)
        {
            SelectNote(note, openEditor: true, focusGraph: true);
            FocusNodeRequested?.Invoke(note.Id);
            return true;
        }

        var project = Projects.FirstOrDefault(p =>
            string.Equals(p.Name, title, StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(p.Slug, title, StringComparison.OrdinalIgnoreCase));
        if (project is not null)
        {
            SelectProject(project, openEditor: true, focusGraph: true);
            FocusNodeRequested?.Invoke(project.Id);
            return true;
        }

        StatusText = $"Nie znaleziono [[{title}]]";
        return false;
    }

    private bool IsAncestor(string possibleAncestorId, string nodeId)
    {
        var current = Projects.FirstOrDefault(p => p.Id == nodeId);
        var guard = 0;
        while (current?.ParentId is not null && guard++ < 64)
        {
            if (string.Equals(current.ParentId, possibleAncestorId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = Projects.FirstOrDefault(p => p.Id == current.ParentId);
        }

        return false;
    }

    private void TrackRecent(string id)
    {
        State.RecentIds.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        State.RecentIds.Insert(0, id);
        if (State.RecentIds.Count > 20)
        {
            State.RecentIds.RemoveRange(20, State.RecentIds.Count - 20);
        }

        TrySaveState();
        RefreshPinnedAndRecent();
    }

    private void RefreshPinnedAndRecent()
    {
        PinnedItems.Clear();
        foreach (var id in State.PinnedIds)
        {
            var item = ResolveSidebarItem(id);
            if (item is not null)
            {
                PinnedItems.Add(item);
            }
        }

        RecentItems.Clear();
        foreach (var id in State.RecentIds)
        {
            var item = ResolveSidebarItem(id);
            if (item is not null && RecentItems.Count < 8)
            {
                RecentItems.Add(item);
            }
        }
    }

    private SidebarItem? ResolveSidebarItem(string id)
    {
        var project = Projects.FirstOrDefault(p => p.Id == id);
        if (project is not null)
        {
            var prefix = project.ItemType.Label() + " · ";
            return new SidebarItem
            {
                Id = project.Id,
                Title = prefix + project.Name,
                IsProject = true,
                IsFolder = project.IsFolder,
                ItemType = project.ItemType
            };
        }

        var note = Notes.FirstOrDefault(n => n.Id == id);
        return note is null
            ? null
            : new SidebarItem { Id = note.Id, Title = note.Title, IsProject = false };
    }

    public Project? FocusedProject() =>
        _focusedProjectId is null ? null : Projects.FirstOrDefault(p => p.Id == _focusedProjectId);

    private void SaveTaskSource(OpenTask task)
    {
        if (task.IsProject)
        {
            var project = Projects.FirstOrDefault(p => p.Id == task.SourceId);
            if (project is not null)
            {
                SaveProjectWithHistory(project);
            }
        }
        else
        {
            var note = Notes.FirstOrDefault(n => n.Id == task.SourceId);
            if (note is not null)
            {
                SaveNoteWithHistory(note);
            }
        }
    }

    private bool ApplyTaskPriorities(IReadOnlyDictionary<ChecklistItem, int?> priorities)
    {
        var affectedItems = priorities
            .Where(pair => pair.Key.Priority != pair.Value)
            .Select(pair => pair.Key)
            .ToHashSet();
        if (affectedItems.Count == 0)
        {
            return true;
        }

        foreach (var (item, priority) in priorities)
        {
            item.Priority = priority;
        }

        var saved = true;
        foreach (var project in Projects.Where(project => project.Checklist.Any(affectedItems.Contains)))
        {
            try
            {
                SaveProjectWithHistory(project);
            }
            catch (Exception ex)
            {
                _pendingSaveProjects[project.Id] = project;
                RecordSaveFailure(ex);
                saved = false;
            }
        }

        foreach (var note in Notes.Where(note => note.Checklist.Any(affectedItems.Contains)))
        {
            try
            {
                SaveNoteWithHistory(note);
            }
            catch (Exception ex)
            {
                _pendingSaveNotes[note.Id] = note;
                RecordSaveFailure(ex);
                saved = false;
            }
        }

        Raise(nameof(HasPendingSaves));
        Raise(nameof(PendingSaveCount));
        RefreshVisible();
        RefreshRelated();
        EditorChanged?.Invoke();
        return saved;
    }

    private void RefreshVisible()
    {
        VisibleProjects.Clear();
        foreach (var project in Projects.Where(ShouldShowProjectInList))
        {
            VisibleProjects.Add(project);
        }

        RebuildNavigationTree();

        VisibleNotes.Clear();
        foreach (var note in Notes.Where(ShouldShowNoteInList))
        {
            VisibleNotes.Add(note);
        }

        var visibleTasks = CollectOpenTasks(
                Notes.Where(ShouldShowNoteInList),
                Projects.Where(ShouldShowProjectInList))
            .ToList();
        VisibleTasks.Clear();
        foreach (var task in visibleTasks)
        {
            VisibleTasks.Add(task);
        }

        RefreshPinnedAndRecent();
    }

    private void RebuildNavigationTree()
    {
        CaptureNavigationExpansionState();
        NavigationTree.Clear();
        var visibleProjects = Projects.Where(ShouldShowProjectInList).ToList();
        var visibleNotes = Notes.Where(ShouldShowNoteInList)
            .OrderBy(note => note.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var byParent = visibleProjects.ToLookup(p => p.ParentId ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        NavigationTreeNode Build(Project project)
        {
            var node = new NavigationTreeNode
            {
                Project = project,
                IsExpanded = _expandedNavigationKeys.Contains("project:" + project.Id)
            };
            foreach (var child in SortTreeChildren(byParent[project.Id]))
            {
                node.Children.Add(Build(child));
            }

            foreach (var note in visibleNotes.Where(note => LayoutService.NoteLinksTo(note, project)))
            {
                node.Children.Add(new NavigationTreeNode { Note = note });
            }

            return node;
        }

        foreach (var root in SortTreeChildren(byParent[string.Empty]))
        {
            NavigationTree.Add(Build(root));
        }

        var unassignedNotes = visibleNotes.Where(note =>
                Projects.All(project => !LayoutService.NoteLinksTo(note, project)))
            .ToList();
        if (unassignedNotes.Count > 0)
        {
            var unassignedGroup = new NavigationTreeNode
            {
                GroupTitle = "Notatki bez projektu",
                IsExpanded = _expandedNavigationKeys.Contains("group:Notatki bez projektu")
            };
            foreach (var note in unassignedNotes)
            {
                unassignedGroup.Children.Add(new NavigationTreeNode { Note = note });
            }

            NavigationTree.Add(unassignedGroup);
        }
    }

    private void CaptureNavigationExpansionState()
    {
        foreach (var node in FlattenNavigationTree(NavigationTree))
        {
            if (node.IsExpanded)
            {
                _expandedNavigationKeys.Add(node.ExpansionKey);
            }
            else
            {
                _expandedNavigationKeys.Remove(node.ExpansionKey);
            }
        }
    }

    private static IEnumerable<NavigationTreeNode> FlattenNavigationTree(
        IEnumerable<NavigationTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenNavigationTree(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<Project> SortTreeChildren(IEnumerable<Project> projects) =>
        projects
            .OrderBy(p => p.ItemType.SortOrder())
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);

    private void RefreshRelated()
    {
        RelatedNotes.Clear();
        ProjectNoteTasks.Clear();
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        foreach (var note in Notes.Where(n => LayoutService.NoteLinksTo(n, project)))
        {
            RelatedNotes.Add(note);
            foreach (var item in note.Checklist
                         .Where(c => !c.IsDone)
                         .OrderBy(c => c.Priority.HasValue ? 0 : 1)
                         .ThenBy(c => c.Priority))
            {
                ProjectNoteTasks.Add(new OpenTask
                {
                    Text = item.Text,
                    SourceId = note.Id,
                    SourceTitle = note.Title,
                    IsProject = false,
                    Item = item
                });
            }
        }
    }

    private bool ShouldShowProjectInList(Project project)
    {
        return SearchService.Matches(SearchQuery, project) ||
               Notes.Any(n => LayoutService.NoteLinksTo(n, project) && SearchService.Matches(SearchQuery, n));
    }

    private bool ShouldShowNoteInList(Note note)
    {
        if (!SearchService.Matches(SearchQuery, note))
        {
            return false;
        }

        var focused = FocusedProject();
        if (focused is not null && !LayoutService.NoteLinksTo(note, focused))
        {
            return false;
        }

        return true;
    }

    private IEnumerable<Project> VisibleGraphProjects()
    {
        var focused = FocusedProject();
        var scopeIds = focused is null
            ? null
            : LayoutService.ProjectSubtree(Projects, focused.Id)
                .Select(project => project.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var project in Projects)
        {
            if (scopeIds is not null && !scopeIds.Contains(project.Id))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery) &&
                !SearchService.Matches(SearchQuery, project) &&
                !Notes.Any(n => LayoutService.NoteLinksTo(n, project) && SearchService.Matches(SearchQuery, n)))
            {
                continue;
            }

            yield return project;
        }
    }

    private IEnumerable<Note> VisibleGraphNotes()
    {
        var focused = FocusedProject();
        var scopeProjects = focused is null
            ? null
            : LayoutService.ProjectSubtree(Projects, focused.Id);
        foreach (var note in Notes)
        {
            if (!SearchService.Matches(SearchQuery, note))
            {
                continue;
            }

            if (scopeProjects is not null && !scopeProjects.Any(project => LayoutService.NoteLinksTo(note, project)))
            {
                continue;
            }

            yield return note;
        }
    }

    private static IEnumerable<OpenTask> CollectOpenTasks(IEnumerable<Note> notes, IEnumerable<Project> projects)
    {
        var tasks = new List<OpenTask>();
        foreach (var project in projects)
        {
            foreach (var item in project.Checklist.Where(c => !c.IsDone))
            {
                tasks.Add(new OpenTask
                {
                    Text = item.Text,
                    SourceId = project.Id,
                    SourceTitle = project.Name,
                    IsProject = true,
                    Item = item
                });
            }
        }

        foreach (var note in notes)
        {
            foreach (var item in note.Checklist.Where(c => !c.IsDone))
            {
                tasks.Add(new OpenTask
                {
                    Text = item.Text,
                    SourceId = note.Id,
                    SourceTitle = note.Title,
                    IsProject = false,
                    Item = item
                });
            }
        }

        return tasks
            .OrderBy(task => task.Item.Priority.HasValue ? 0 : 1)
            .ThenBy(task => task.Item.Priority);
    }

    private void PushUndo(Action undo, Action redo)
    {
        if (_suppressUndo)
        {
            return;
        }

        _undo.Push((undo, redo));
        _redo.Clear();
    }

    private void SaveState() => _stateStore.Save(State);

    private bool TrySaveState()
    {
        try
        {
            SaveState();
            return true;
        }
        catch (Exception ex)
        {
            RecordSaveFailure(ex);
            return false;
        }
    }

    private void SaveNoteWithHistory(Note note)
    {
        SanitizePeopleAssignments(note.People, note.Checklist);
        _revisionStore.CaptureExisting("Notes", note.Id, note.FilePath);
        Store.SaveNote(note);
    }

    private void SaveProjectWithHistory(Project project)
    {
        SanitizePeopleAssignments(project.People, project.Checklist);
        var oldSlug = project.Slug;
        var desired = SlugHelper.FromName(project.Name);
        if (string.Equals(oldSlug, desired, StringComparison.OrdinalIgnoreCase))
        {
            _revisionStore.CaptureExisting("Projects", project.Id, project.FilePath);
            Store.SaveProject(project);
            return;
        }

        var newSlug = SlugHelper.Unique(
            desired,
            Projects.Where(other => other.Id != project.Id).Select(other => other.Slug));
        var affected = Notes
            .Where(note => note.Tags.Any(tag => string.Equals(tag, oldSlug, StringComparison.OrdinalIgnoreCase)))
            .Select(note => (Note: note, Tags: note.Tags.ToList()))
            .ToList();
        var savedNotes = new List<Note>();

        try
        {
            foreach (var (note, _) in affected)
            {
                note.Tags = note.Tags
                    .Select(tag => string.Equals(tag, oldSlug, StringComparison.OrdinalIgnoreCase) ? newSlug : tag)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                SaveNoteWithHistory(note);
                savedNotes.Add(note);
            }

            project.Slug = newSlug;
            _revisionStore.CaptureExisting("Projects", project.Id, project.FilePath);
            Store.SaveProject(project);
        }
        catch (Exception saveError)
        {
            project.Slug = oldSlug;
            foreach (var (note, tags) in affected)
            {
                note.Tags = tags;
            }

            var rollbackErrors = new List<Exception>();
            foreach (var note in savedNotes)
            {
                try
                {
                    SaveNoteWithHistory(note);
                }
                catch (Exception rollbackError)
                {
                    _pendingSaveNotes[note.Id] = note;
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (rollbackErrors.Count == 0)
            {
                throw;
            }

            throw new IOException(
                "Nie udało się zapisać zmiany nazwy projektu ani w pełni wycofać powiązań notatek.",
                new AggregateException([saveError, .. rollbackErrors]));
        }
    }

    private void SanitizePeopleAssignments(List<string> documentPeople, IEnumerable<ChecklistItem> checklist)
    {
        var registered = KeepRegisteredPeople(documentPeople);
        documentPeople.Clear();
        documentPeople.AddRange(registered);
        foreach (var item in checklist)
        {
            item.People = KeepRegisteredPeople(item.People);
        }
    }

    private static string ApplicationThemeName()
    {
        var variant = Application.Current?.ActualThemeVariant;
        if (Equals(variant, ThemeVariant.Dark))
        {
            return "Ciemny (systemowy)";
        }

        if (Equals(variant, ThemeVariant.Light))
        {
            return "Jasny (systemowy)";
        }

        return "Systemowy";
    }
}
