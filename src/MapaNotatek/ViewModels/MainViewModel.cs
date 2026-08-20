using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using MapaNotatek.Models;
using MapaNotatek.Services;

namespace MapaNotatek.ViewModels;

public enum CenterViewKind
{
    Graph,
    Notes,
    Tasks
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AppStateStore _stateStore = new();
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly Stack<(Action Undo, Action Redo)> _undo = new();
    private readonly Stack<(Action Undo, Action Redo)> _redo = new();
    private bool _suppressUndo;
    private string _searchQuery = string.Empty;
    private CenterViewKind _centerView = CenterViewKind.Graph;
    private bool _isEditorOpen;
    private bool _showArchived;
    private string? _focusedProjectId;
    private string? _selectedProjectId;
    private string? _selectedNoteId;
    private string? _selectedGraphId;
    private bool _selectedGraphIsProject;
    private string _statusText = string.Empty;
    private Note? _pendingSaveNote;
    private Project? _pendingSaveProject;

    public MainViewModel(DispatcherQueue dispatcher)
    {
        State = _stateStore.Load();
        Store = new MarkdownStore(State.DataFolder ?? _stateStore.DefaultDataFolder);
        Store.EnsureFolders();
        _showArchived = State.ShowArchived;
        _focusedProjectId = State.FocusedProjectId;
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(800);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => FlushPendingSaves();
        Reload();
    }

    public MarkdownStore Store { get; }
    public AppState State { get; }
    public List<Project> Projects { get; } = [];
    public List<Note> Notes { get; } = [];
    public ObservableCollection<Project> VisibleProjects { get; } = [];
    public ObservableCollection<Note> VisibleNotes { get; } = [];
    public ObservableCollection<OpenTask> VisibleTasks { get; } = [];
    public ObservableCollection<Note> RelatedNotes { get; } = [];
    public ObservableCollection<OpenTask> ProjectNoteTasks { get; } = [];

    public event Action? GraphChanged;
    public event Action? EditorChanged;
    public event Action? SelectionChanged;

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
            }
        }
    }

    public bool IsGraphView => CenterView == CenterViewKind.Graph;
    public bool IsNotesView => CenterView == CenterViewKind.Notes;
    public bool IsTasksView => CenterView == CenterViewKind.Tasks;

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set => Set(ref _isEditorOpen, value);
    }

    public bool ShowArchived
    {
        get => _showArchived;
        set
        {
            if (Set(ref _showArchived, value))
            {
                State.ShowArchived = value;
                SaveState();
                RefreshVisible();
                GraphChanged?.Invoke();
            }
        }
    }

    public string? FocusedProjectId
    {
        get => _focusedProjectId;
        set
        {
            if (Set(ref _focusedProjectId, value))
            {
                State.FocusedProjectId = value;
                SaveState();
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

    public string ThemeName =>
        ApplicationThemeName();

    public string DataFolder => Store.Root;

    public IEnumerable<Project> GraphProjects => VisibleGraphProjects();
    public IEnumerable<Note> GraphNotes => VisibleGraphNotes();

    public void Reload()
    {
        Projects.Clear();
        Projects.AddRange(Store.LoadProjects());
        Notes.Clear();
        Notes.AddRange(Store.LoadNotes());
        LayoutService.ApplyMissingPositions(Projects, Notes, State.NodePositions);
        RefreshVisible();
        GraphChanged?.Invoke();
        EditorChanged?.Invoke();
    }

    public Project NewProject()
    {
        var project = Store.CreateProject("Nowy projekt");
        Projects.Add(project);
        LayoutService.ApplyMissingPositions(Projects, Notes, State.NodePositions);
        SaveState();
        RefreshVisible();
        SelectProject(project, openEditor: true, focusGraph: true);
        StatusText = "Utworzono projekt";
        GraphChanged?.Invoke();
        return project;
    }

    public Note NewNote()
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
        LayoutService.ApplyMissingPositions(Projects, Notes, State.NodePositions);
        SaveState();
        RefreshVisible();
        SelectNote(note, openEditor: true, focusGraph: true);
        StatusText = "Utworzono notatkę";
        GraphChanged?.Invoke();
        return note;
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
        RefreshRelated();
        EditorChanged?.Invoke();
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
        RefreshRelated();
        EditorChanged?.Invoke();
        SelectionChanged?.Invoke();
        GraphChanged?.Invoke();
    }

    public void SelectGraphNode(string id, bool isProject)
    {
        _selectedGraphId = id;
        _selectedGraphIsProject = isProject;
        SelectionChanged?.Invoke();
        GraphChanged?.Invoke();
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
        StatusText = "Widok wszystkich projektów";
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

        SaveState();
    }

    public void ScheduleSaveNote(Note note)
    {
        note.Modified = DateTimeOffset.Now;
        _pendingSaveNote = note;
        _pendingSaveProject = null;
        _saveTimer.Stop();
        _saveTimer.Start();
        StatusText = "Zapisywanie…";
    }

    public void ScheduleSaveProject(Project project)
    {
        project.Modified = DateTimeOffset.Now;
        _pendingSaveProject = project;
        _pendingSaveNote = null;
        _saveTimer.Stop();
        _saveTimer.Start();
        StatusText = "Zapisywanie…";
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        FlushPendingSaves();
        StatusText = "Zapisano";
    }

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
            ApplyProjectSlug(project);
            Store.SaveProject(project);
            PushUndo(
                () =>
                {
                    project.Name = previous;
                    Store.SaveProject(project);
                    Reload();
                },
                () =>
                {
                    project.Name = name.Trim();
                    Store.SaveProject(project);
                    Reload();
                });
            ScheduleSaveProject(project);
            EditorChanged?.Invoke();
            return;
        }

        if (SelectedNote is { } note)
        {
            var previous = note.Title;
            note.Title = name.Trim();
            Store.SaveNote(note);
            PushUndo(
                () =>
                {
                    note.Title = previous;
                    Store.SaveNote(note);
                    Reload();
                },
                () =>
                {
                    note.Title = name.Trim();
                    Store.SaveNote(note);
                    Reload();
                });
            ScheduleSaveNote(note);
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

        FlushPendingSaves();
        Store.MoveNoteToTrash(note);
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
        SaveState();
        StatusText = "Notatka przeniesiona do kosza";
        GraphChanged?.Invoke();
        EditorChanged?.Invoke();
    }

    public void ToggleArchiveSelectedProject()
    {
        var project = SelectedProject;
        if (project is null && _selectedGraphIsProject && _selectedGraphId is not null)
        {
            project = Projects.FirstOrDefault(p => p.Id == _selectedGraphId);
        }

        if (project is null)
        {
            return;
        }

        var next = !project.IsArchived;
        project.IsArchived = next;
        Store.SaveProject(project);
        PushUndo(
            () =>
            {
                project.IsArchived = !next;
                Store.SaveProject(project);
                RefreshVisible();
                GraphChanged?.Invoke();
            },
            () =>
            {
                project.IsArchived = next;
                Store.SaveProject(project);
                RefreshVisible();
                GraphChanged?.Invoke();
            });
        RefreshVisible();
        GraphChanged?.Invoke();
        EditorChanged?.Invoke();
        StatusText = project.IsArchived ? "Projekt zarchiwizowany" : "Projekt przywrócony";
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
                Store.SaveProject(project);
            }
        }
        else
        {
            var note = Notes.FirstOrDefault(n => n.Id == task.SourceId);
            if (note is not null)
            {
                Store.SaveNote(note);
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

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _suppressUndo = true;
        var action = _undo.Pop();
        action.Undo();
        _redo.Push(action);
        _suppressUndo = false;
        StatusText = "Cofnięto";
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _suppressUndo = true;
        var action = _redo.Pop();
        action.Redo();
        _undo.Push(action);
        _suppressUndo = false;
        StatusText = "Ponowiono";
    }

    public void ChangeDataFolder(string folder)
    {
        FlushPendingSaves();
        Store.SetRoot(folder);
        State.DataFolder = folder;
        SaveState();
        Reload();
        StatusText = "Zmieniono folder danych";
    }

    public void SaveZoom(double zoom)
    {
        State.Zoom = zoom;
        SaveState();
    }

    public void FlushPendingSaves()
    {
        var changed = false;
        if (_pendingSaveNote is not null)
        {
            Store.SaveNote(_pendingSaveNote);
            _pendingSaveNote = null;
            StatusText = "Zapisano";
            changed = true;
        }

        if (_pendingSaveProject is not null)
        {
            ApplyProjectSlug(_pendingSaveProject);
            Store.SaveProject(_pendingSaveProject);
            _pendingSaveProject = null;
            StatusText = "Zapisano";
            changed = true;
        }

        if (changed)
        {
            RefreshVisible();
            RefreshRelated();
            GraphChanged?.Invoke();
        }
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
                Store.SaveProject(project);
            }
        }
        else
        {
            var note = Notes.FirstOrDefault(n => n.Id == task.SourceId);
            if (note is not null)
            {
                Store.SaveNote(note);
            }
        }
    }

    private void RefreshVisible()
    {
        VisibleProjects.Clear();
        foreach (var project in Projects.Where(ShouldShowProjectInList))
        {
            VisibleProjects.Add(project);
        }

        VisibleNotes.Clear();
        foreach (var note in Notes.Where(ShouldShowNoteInList))
        {
            VisibleNotes.Add(note);
        }

        VisibleTasks.Clear();
        foreach (var task in CollectOpenTasks(Notes, Projects))
        {
            VisibleTasks.Add(task);
        }
    }

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
            foreach (var item in note.Checklist.Where(c => !c.IsDone))
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
        if (!ShowArchived && project.IsArchived)
        {
            return false;
        }

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
        foreach (var project in Projects)
        {
            if (!ShowArchived && project.IsArchived)
            {
                continue;
            }

            var focused = FocusedProject();
            if (focused is not null && project.Id != focused.Id)
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
        var projects = VisibleGraphProjects().ToList();
        foreach (var note in Notes)
        {
            if (!SearchService.Matches(SearchQuery, note))
            {
                continue;
            }

            var focused = FocusedProject();
            if (focused is not null && !LayoutService.NoteLinksTo(note, focused))
            {
                continue;
            }

            if (focused is null && !ShowArchived)
            {
                var linked = Projects.Where(p => LayoutService.NoteLinksTo(note, p)).ToList();
                if (linked.Count > 0 && linked.All(p => p.IsArchived))
                {
                    continue;
                }
            }

            if (projects.Count > 0 && focused is null)
            {
                // keep unassigned notes visible on the full graph
            }

            yield return note;
        }
    }

    private static IEnumerable<OpenTask> CollectOpenTasks(IEnumerable<Note> notes, IEnumerable<Project> projects)
    {
        foreach (var project in projects)
        {
            foreach (var item in project.Checklist.Where(c => !c.IsDone))
            {
                yield return new OpenTask
                {
                    Text = item.Text,
                    SourceId = project.Id,
                    SourceTitle = project.Name,
                    IsProject = true,
                    Item = item
                };
            }
        }

        foreach (var note in notes)
        {
            foreach (var item in note.Checklist.Where(c => !c.IsDone))
            {
                yield return new OpenTask
                {
                    Text = item.Text,
                    SourceId = note.Id,
                    SourceTitle = note.Title,
                    IsProject = false,
                    Item = item
                };
            }
        }
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

    private void ApplyProjectSlug(Project project)
    {
        var oldSlug = project.Slug;
        var desired = SlugHelper.FromName(project.Name);
        if (string.Equals(oldSlug, desired, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var others = Projects.Where(p => p.Id != project.Id).Select(p => p.Slug);
        var newSlug = SlugHelper.Unique(desired, others);
        foreach (var note in Notes.Where(n => n.Tags.Any(t => string.Equals(t, oldSlug, StringComparison.OrdinalIgnoreCase))))
        {
            note.Tags = note.Tags
                .Select(t => string.Equals(t, oldSlug, StringComparison.OrdinalIgnoreCase) ? newSlug : t)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Store.SaveNote(note);
        }

        project.Slug = newSlug;
    }

    private void SaveState() => _stateStore.Save(State);

    private static string ApplicationThemeName()
    {
        try
        {
            var settings = new Windows.UI.ViewManagement.UISettings();
            var color = settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            var isDark = color.R < 128 && color.G < 128 && color.B < 128;
            return isDark ? "Ciemny (systemowy)" : "Jasny (systemowy)";
        }
        catch
        {
            return "Systemowy";
        }
    }
}
