using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapaNotatek.Models;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;
using MapaNotatek.Views;

namespace MapaNotatek;

public partial class MainWindow : Window
{
    private MainViewModel _vm = null!;
    private GraphView _graphControl = null!;
    private NoteListView _notesControl = null!;
    private TaskListView _tasksControl = null!;
    private EditorPanel _editorControl = null!;
    private int _panelIndex;
    private double _lastZoom = 1;
    private bool _zoomSaveReady;
    private bool _workspaceLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => LoadWorkspace();
        Closing += (_, _) => _vm?.FlushPendingSaves();
        AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void LoadWorkspace()
    {
        if (_workspaceLoaded)
        {
            return;
        }

        _workspaceLoaded = true;
        Startup.Log("LoadWorkspace");
        try
        {
            _graphControl = new GraphView();
            _notesControl = new NoteListView();
            _tasksControl = new TaskListView();
            _editorControl = new EditorPanel();
            GraphHost.Content = _graphControl;
            NotesHost.Content = _notesControl;
            TasksHost.Content = _tasksControl;
            EditorHost.Content = _editorControl;

            _vm = new MainViewModel();
            _graphControl.ViewModel = _vm;
            _notesControl.ViewModel = _vm;
            _tasksControl.ViewModel = _vm;
            _editorControl.ViewModel = _vm;
            ProjectsList.ItemsSource = _vm.VisibleProjects;
            _notesControl.Bind();
            _tasksControl.Bind();
            _graphControl.Refresh();
            _editorControl.Refresh();
            ArchivedCheck.IsChecked = _vm.ShowArchived;
            StatusText.Text = _vm.DataFolder;

            _vm.GraphChanged += OnGraphChanged;
            _vm.EditorChanged += OnEditorChanged;
            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.StatusText))
                {
                    StatusText.Text = _vm.StatusText;
                }
            };

            _graphControl.ZoomChanged += zoom =>
            {
                if (!_zoomSaveReady)
                {
                    return;
                }

                if (Math.Abs(zoom - _lastZoom) > 0.02)
                {
                    _lastZoom = zoom;
                    _vm.SaveZoom(zoom);
                }
            };

            _lastZoom = _vm.State.Zoom <= 0 ? 1 : _vm.State.Zoom;
            _graphControl.SetZoom(_lastZoom);
            _zoomSaveReady = true;
            UpdateEditorVisibility();
            _graphControl.Focus();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Błąd ładowania: " + ex.Message;
            Startup.Fail(ex.ToString());
        }
    }

    private void OnGraphChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _graphControl.Refresh();
            _notesControl.Bind();
            _tasksControl.Bind();
            UpdateEditorVisibility();
        });
    }

    private void OnEditorChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _editorControl.Refresh();
            UpdateEditorVisibility();
            _notesControl.Bind();
            _tasksControl.Bind();
        });
    }

    private void UpdateEditorVisibility()
    {
        RightPanel.IsVisible = _vm.IsEditorOpen;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) =>
        _vm.SearchQuery = SearchBox.Text ?? string.Empty;

    private void OnNewNote(object? sender, RoutedEventArgs e) => CreateNote();

    private void OnNewProject(object? sender, RoutedEventArgs e) => CreateProject();

    private void OnSave(object? sender, RoutedEventArgs e) => _vm.SaveNow();

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (!IsTextInputFocused())
        {
            _vm.Undo();
        }
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        if (!IsTextInputFocused())
        {
            _vm.Redo();
        }
    }

    private void OnFocusSearch(object? sender, RoutedEventArgs e) => FocusSearch();

    private async void OnRename(object? sender, RoutedEventArgs e) => await RenameAsync();

    private void OnTrashNote(object? sender, RoutedEventArgs e)
    {
        if (IsGraphOrListFocused())
        {
            EnsureGraphSelection();
            _vm.TrashSelectedNote();
        }
    }

    private void OnGraphView(object? sender, RoutedEventArgs e)
    {
        if (GraphRadio.IsChecked == true)
        {
            ShowCenter(CenterViewKind.Graph);
        }
    }

    private void OnNotesView(object? sender, RoutedEventArgs e)
    {
        if (NotesRadio.IsChecked == true)
        {
            ShowCenter(CenterViewKind.Notes);
        }
    }

    private void OnTasksView(object? sender, RoutedEventArgs e)
    {
        if (TasksRadio.IsChecked == true)
        {
            ShowCenter(CenterViewKind.Tasks);
        }
    }

    private void OnGraphViewMenu(object? sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Graph);

    private void OnNotesViewMenu(object? sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Notes);

    private void OnTasksViewMenu(object? sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Tasks);

    private void OnShowAll(object? sender, RoutedEventArgs e) => _vm.ShowAllProjects();

    private void OnToggleArchived(object? sender, RoutedEventArgs e)
    {
        _vm.ShowArchived = ArchivedCheck.IsChecked == true;
    }

    private void OnToggleArchivedMenu(object? sender, RoutedEventArgs e)
    {
        _vm.ShowArchived = !_vm.ShowArchived;
        ArchivedCheck.IsChecked = _vm.ShowArchived;
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => ChangeZoom(0.1);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => ChangeZoom(-0.1);

    private void OnZoomReset(object? sender, RoutedEventArgs e)
    {
        _graphControl.SetZoom(1);
        _vm.SaveZoom(1);
        _lastZoom = 1;
    }

    private void OnArchiveProject(object? sender, RoutedEventArgs e)
    {
        EnsureGraphSelection();
        _vm.ToggleArchiveSelectedProject();
    }

    private void OnCloseEditor(object? sender, RoutedEventArgs e)
    {
        _vm.CloseEditor();
        _graphControl.Focus();
    }

    private void OnProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Selection alone does not open editor; double-click / Enter does.
    }

    private void OnProjectDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProjectsList.SelectedItem is Project project)
        {
            _vm.SelectProject(project, openEditor: true, focusGraph: true, filterToProject: true);
        }
    }

    private void OnProjectsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ProjectsList.SelectedItem is Project project)
        {
            _vm.SelectProject(project, openEditor: true, focusGraph: true, filterToProject: true);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            _ = RenameAsync();
            e.Handled = true;
        }
    }

    private async void OnExportBackup(object? sender, RoutedEventArgs e) => await ExportBackupAsync();

    private async Task ExportBackupAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder na kopię",
            AllowMultiple = false
        });
        if (folders.Count == 0)
        {
            return;
        }

        var dest = Path.Combine(folders[0].Path.LocalPath, $"MapaNotatek-{DateTime.Now:yyyyMMdd-HHmmss}");
        BackupService.ExportCopy(_vm.DataFolder, dest);
        _vm.StatusText = $"Wyeksportowano kopię do {dest}";
    }

    private async void OnSettings(object? sender, RoutedEventArgs e) => await ShowSettingsAsync();

    private async void OnShortcuts(object? sender, RoutedEventArgs e) => await ShowShortcutsAsync();

    private async void OnCommandPalette(object? sender, RoutedEventArgs e) => await ShowCommandsAsync();

    private void CreateNote()
    {
        _vm.NewNote();
        ShowCenter(CenterViewKind.Graph);
        _editorControl.Refresh();
        UpdateEditorVisibility();
        _editorControl.DefaultFocusTarget().Focus();
    }

    private void CreateProject()
    {
        _vm.NewProject();
        ShowCenter(CenterViewKind.Graph);
        _editorControl.Refresh();
        UpdateEditorVisibility();
        _editorControl.DefaultFocusTarget().Focus();
    }

    private void ShowCenter(CenterViewKind kind)
    {
        _vm.CenterView = kind;
        GraphHost.IsVisible = kind == CenterViewKind.Graph;
        NotesHost.IsVisible = kind == CenterViewKind.Notes;
        TasksHost.IsVisible = kind == CenterViewKind.Tasks;
        GraphRadio.IsChecked = kind == CenterViewKind.Graph;
        NotesRadio.IsChecked = kind == CenterViewKind.Notes;
        TasksRadio.IsChecked = kind == CenterViewKind.Tasks;
        if (kind == CenterViewKind.Graph)
        {
            _graphControl.Focus();
        }
        else if (kind == CenterViewKind.Notes)
        {
            _notesControl.Bind();
            _notesControl.Focus();
        }
        else
        {
            _tasksControl.Bind();
            _tasksControl.Focus();
        }
    }

    private void ChangeZoom(double delta)
    {
        var next = Math.Clamp(_graphControl.ZoomFactor + delta, 0.25, 3);
        _graphControl.SetZoom(next);
        _vm.SaveZoom(next);
        _lastZoom = next;
        ShowCenter(CenterViewKind.Graph);
    }

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CyclePanel(int step)
    {
        _panelIndex = (_panelIndex + step + 3) % 3;
        if (_panelIndex == 2 && !_vm.IsEditorOpen)
        {
            _panelIndex = step > 0 ? 0 : 1;
        }

        switch (_panelIndex)
        {
            case 0:
                SearchBox.Focus();
                break;
            case 1:
                if (_vm.CenterView == CenterViewKind.Graph)
                {
                    _graphControl.Focus();
                }
                else if (_vm.CenterView == CenterViewKind.Notes)
                {
                    _notesControl.Focus();
                }
                else
                {
                    _tasksControl.Focus();
                }

                break;
            default:
                _editorControl.DefaultFocusTarget().Focus();
                break;
        }
    }

    private async Task RenameAsync()
    {
        EnsureGraphSelection();
        var current = _vm.SelectedProject?.Name ?? _vm.SelectedNote?.Title;
        if (current is null)
        {
            return;
        }

        var box = new TextBox { Text = current };
        var cancel = new Button { Content = "Anuluj", MinWidth = 80 };
        var save = new Button { Content = "Zapisz", MinWidth = 80, IsDefault = true };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, save }
        };
        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children = { box, buttons }
        };
        var dialog = new Window
        {
            Title = "Zmień nazwę",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root
        };

        var result = false;
        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) => { result = true; dialog.Close(); };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { result = true; dialog.Close(); e.Handled = true; }
            if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; }
        };

        await dialog.ShowDialog(this);
        if (result)
        {
            _vm.RenameSelected(box.Text ?? string.Empty);
            _editorControl.Refresh();
        }
    }

    private async Task ShowSettingsAsync()
    {
        var panel = new SettingsDialog { ViewModel = _vm, HostWindow = this };
        panel.Bind();
        var dialog = new Window
        {
            Title = "Ustawienia",
            Width = 480,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    new Button
                    {
                        Content = "Zamknij",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Margin = new Thickness(0, 12, 0, 0),
                        [DockPanel.DockProperty] = Dock.Bottom
                    },
                    panel
                }
            }
        };
        var close = (Button)((DockPanel)dialog.Content!).Children[0];
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task ShowShortcutsAsync()
    {
        var dialog = new Window
        {
            Title = "Skróty klawiszowe",
            Width = 480,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    new Button
                    {
                        Content = "Zamknij",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Margin = new Thickness(0, 12, 0, 0),
                        [DockPanel.DockProperty] = Dock.Bottom
                    },
                    new ShortcutsDialog()
                }
            }
        };
        var close = (Button)((DockPanel)dialog.Content!).Children[0];
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task ShowCommandsAsync()
    {
        var palette = new CommandPaletteDialog();
        palette.SetCommands(BuildCommands());
        var chosen = await palette.ShowAsync(this);
        chosen?.Run();
    }

    private IEnumerable<AppCommand> BuildCommands() =>
    [
        new() { Name = "Nowa notatka", Shortcut = PlatformKeys.Chord("N"), Run = CreateNote },
        new() { Name = "Nowy projekt", Shortcut = PlatformKeys.ChordShift("N"), Run = CreateProject },
        new() { Name = "Zapisz", Shortcut = PlatformKeys.Chord("S"), Run = _vm.SaveNow },
        new() { Name = "Szukaj", Shortcut = PlatformKeys.Chord("F"), Run = FocusSearch },
        new() { Name = "Widok grafu", Shortcut = PlatformKeys.Chord("1"), Run = () => ShowCenter(CenterViewKind.Graph) },
        new() { Name = "Lista notatek", Shortcut = PlatformKeys.Chord("2"), Run = () => ShowCenter(CenterViewKind.Notes) },
        new() { Name = "Otwarte zadania", Shortcut = PlatformKeys.Chord("3"), Run = () => ShowCenter(CenterViewKind.Tasks) },
        new() { Name = "Pokaż wszystkie projekty", Shortcut = "", Run = _vm.ShowAllProjects },
        new() { Name = "Zamknij panel", Shortcut = PlatformKeys.Chord("W"), Run = () => _vm.CloseEditor() },
        new() { Name = "Ustawienia", Shortcut = PlatformKeys.Chord(","), Run = () => _ = ShowSettingsAsync() },
        new() { Name = "Skróty klawiszowe", Shortcut = PlatformKeys.Chord("/"), Run = () => _ = ShowShortcutsAsync() },
        new() { Name = "Eksportuj kopię", Shortcut = "", Run = () => _ = ExportBackupAsync() },
        new() { Name = "Archiwizuj projekt", Shortcut = "", Run = _vm.ToggleArchiveSelectedProject },
        new() { Name = "Przenieś notatkę do kosza", Shortcut = PlatformKeys.TrashLabel, Run = _vm.TrashSelectedNote }
    ];

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var mod = PlatformKeys.IsCommand(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var textInput = IsTextInputFocused();
        var graphOrList = IsGraphOrListFocused();

        if (e.Key == Key.F6)
        {
            CyclePanel(shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_vm.IsEditorOpen)
            {
                _vm.CloseEditor();
                _graphControl.Focus();
            }
            else
            {
                ShowCenter(CenterViewKind.Graph);
            }

            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.W)
        {
            _vm.CloseEditor();
            e.Handled = true;
            return;
        }

        if (mod && shift && e.Key == Key.P)
        {
            _ = ShowCommandsAsync();
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.OemComma)
        {
            _ = ShowSettingsAsync();
            e.Handled = true;
            return;
        }

        if (mod && (e.Key == Key.OemQuestion || e.Key == Key.Oem2 || (shift && e.Key == Key.D7)))
        {
            _ = ShowShortcutsAsync();
            e.Handled = true;
            return;
        }

        if (mod && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            ChangeZoom(0.1);
            e.Handled = true;
            return;
        }

        if (mod && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            ChangeZoom(-0.1);
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.D0)
        {
            OnZoomReset(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.D1)
        {
            ShowCenter(CenterViewKind.Graph);
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.D2)
        {
            ShowCenter(CenterViewKind.Notes);
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.D3)
        {
            ShowCenter(CenterViewKind.Tasks);
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.N && !shift)
        {
            CreateNote();
            e.Handled = true;
            return;
        }

        if (mod && shift && e.Key == Key.N)
        {
            CreateProject();
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.S)
        {
            _vm.SaveNow();
            e.Handled = true;
            return;
        }

        if (mod && e.Key == Key.F)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (textInput && IsReservedEditorKey(e.Key, mod))
        {
            return;
        }

        if (mod && e.Key == Key.Z && !shift)
        {
            _vm.Undo();
            e.Handled = true;
            return;
        }

        if ((mod && shift && e.Key == Key.Z) || (mod && e.Key == Key.Y))
        {
            _vm.Redo();
            e.Handled = true;
            return;
        }

        if (!graphOrList)
        {
            return;
        }

        if (e.Key == Key.F2)
        {
            _ = RenameAsync();
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            EnsureGraphSelection();
            _vm.TrashSelectedNote();
            e.Handled = true;
        }
    }

    private void EnsureGraphSelection()
    {
        if (_vm.SelectedGraphId is null)
        {
            return;
        }

        if (_vm.SelectedGraphIsProject)
        {
            var project = _vm.Projects.FirstOrDefault(p => p.Id == _vm.SelectedGraphId);
            if (project is not null && _vm.SelectedProject is null)
            {
                _vm.SelectProject(project, openEditor: _vm.IsEditorOpen, focusGraph: true);
            }
        }
        else
        {
            var note = _vm.Notes.FirstOrDefault(n => n.Id == _vm.SelectedGraphId);
            if (note is not null && _vm.SelectedNote is null)
            {
                _vm.SelectNote(note, openEditor: _vm.IsEditorOpen, focusGraph: true);
            }
        }
    }

    private static bool IsReservedEditorKey(Key key, bool mod)
    {
        if (key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Delete or Key.Back)
        {
            return true;
        }

        return mod && key is Key.C or Key.X or Key.V or Key.A or Key.Z or Key.Y;
    }

    private bool IsTextInputFocused()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is TextBox;
    }

    private bool IsGraphOrListFocused()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, _graphControl) ||
                ReferenceEquals(focused, _notesControl) ||
                ReferenceEquals(focused, _tasksControl) ||
                ReferenceEquals(focused, ProjectsList))
            {
                return true;
            }

            focused = focused.GetVisualParent();
        }

        return false;
    }
}
