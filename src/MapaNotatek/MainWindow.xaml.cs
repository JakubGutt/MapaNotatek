using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using MapaNotatek.Models;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;
using MapaNotatek.Views;

namespace MapaNotatek;

public sealed partial class MainWindow : Window
{
    private MainViewModel _vm = null!;
    private GraphView GraphControl = null!;
    private NoteListView NotesControl = null!;
    private TaskListView TasksControl = null!;
    private EditorPanel EditorControl = null!;
    private int _panelIndex;
    private float _lastZoom = 1;
    private bool _zoomSaveReady;
    private bool _workspaceLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Title = "MapaNotatek";
        StatusText.Text = "Ładowanie…";
        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        }
        catch (Exception ex)
        {
            Startup.Log("Resize: " + ex.Message);
        }

        RootGrid.Loaded += (_, _) => LoadWorkspace();
    }

    public void LoadWorkspace()
    {
        if (_workspaceLoaded)
        {
            return;
        }

        _workspaceLoaded = true;
        Startup.Log("LoadWorkspace");
        try
        {
        GraphControl = new GraphView();
        NotesControl = new NoteListView();
        TasksControl = new TaskListView();
        EditorControl = new EditorPanel();
        GraphHost.Children.Add(GraphControl);
        NotesHost.Children.Add(NotesControl);
        TasksHost.Children.Add(TasksControl);
        EditorHost.Children.Add(EditorControl);
        _vm = new MainViewModel(DispatcherQueue);
        GraphControl.ViewModel = _vm;
        NotesControl.ViewModel = _vm;
        TasksControl.ViewModel = _vm;
        EditorControl.ViewModel = _vm;
        ProjectsList.ItemsSource = _vm.VisibleProjects;
        NotesControl.Bind();
        TasksControl.Bind();
        GraphControl.Refresh();
        EditorControl.Refresh();
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

        GraphControl.ZoomChanged += zoom =>
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

        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        Closed += (_, _) => _vm?.FlushPendingSaves();
        Activated += OnFirstActivated;
        UpdateEditorVisibility();
        OnFirstActivated(this, null!);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Błąd ładowania: " + ex.Message;
            Startup.Fail(ex.ToString());
        }
    }

    private void OnFirstActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        _lastZoom = (float)_vm.State.Zoom;
        if (_lastZoom <= 0)
        {
            _lastZoom = 1;
        }

        GraphControl.SetZoom(_lastZoom);
        _zoomSaveReady = true;
        GraphControl.Focus(FocusState.Programmatic);
    }

    public void ApplyWindowSize()
    {
        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        }
        catch (Exception ex)
        {
            Startup.Log("Resize: " + ex.Message);
        }
    }

    private void OnGraphChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            GraphControl.Refresh();
            NotesControl.Bind();
            TasksControl.Bind();
            UpdateEditorVisibility();
        });
    }

    private void OnEditorChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EditorControl.Refresh();
            UpdateEditorVisibility();
            NotesControl.Bind();
            TasksControl.Bind();
        });
    }

    private void UpdateEditorVisibility()
    {
        RightColumn.Width = _vm.IsEditorOpen ? new GridLength(360) : new GridLength(0);
        RightPanel.Visibility = _vm.IsEditorOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _vm.SearchQuery = SearchBox.Text;
    }

    private void OnNewNote(object sender, RoutedEventArgs e) => CreateNote();

    private void OnNewProject(object sender, RoutedEventArgs e) => CreateProject();

    private void OnSave(object sender, RoutedEventArgs e) => _vm.SaveNow();

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (!IsTextInputFocused())
        {
            _vm.Undo();
        }
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (!IsTextInputFocused())
        {
            _vm.Redo();
        }
    }

    private void OnFocusSearch(object sender, RoutedEventArgs e) => FocusSearch();

    private async void OnRename(object sender, RoutedEventArgs e) => await RenameAsync();

    private void OnTrashNote(object sender, RoutedEventArgs e)
    {
        if (IsGraphOrListFocused())
        {
            EnsureGraphSelection();
            _vm.TrashSelectedNote();
        }
    }

    private void OnGraphView(object sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Graph);

    private void OnNotesView(object sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Notes);

    private void OnTasksView(object sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Tasks);

    private void OnShowAll(object sender, RoutedEventArgs e) => _vm.ShowAllProjects();

    private void OnToggleArchived(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box)
        {
            _vm.ShowArchived = box.IsChecked == true;
            return;
        }

        _vm.ShowArchived = !_vm.ShowArchived;
        ArchivedCheck.IsChecked = _vm.ShowArchived;
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => ChangeZoom(0.1f);

    private void OnZoomOut(object sender, RoutedEventArgs e) => ChangeZoom(-0.1f);

    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        GraphControl.SetZoom(1);
        _vm.SaveZoom(1);
        _lastZoom = 1;
    }

    private void OnArchiveProject(object sender, RoutedEventArgs e)
    {
        EnsureGraphSelection();
        _vm.ToggleArchiveSelectedProject();
    }

    private void OnCloseEditor(object sender, RoutedEventArgs e)
    {
        _vm.CloseEditor();
        GraphControl.Focus(FocusState.Programmatic);
    }

    private void OnProjectClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Project project)
        {
            _vm.SelectProject(project, openEditor: true, focusGraph: true, filterToProject: true);
        }
    }

    private void OnProjectsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ProjectsList.SelectedItem is Project project)
        {
            _vm.SelectProject(project, openEditor: true, focusGraph: true, filterToProject: true);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.F2)
        {
            _ = RenameAsync();
            e.Handled = true;
        }
    }

    private async void OnExportBackup(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        var dest = Path.Combine(folder.Path, $"MapaNotatek-{DateTime.Now:yyyyMMdd-HHmmss}");
        BackupService.ExportCopy(_vm.DataFolder, dest);
        _vm.StatusText = $"Wyeksportowano kopię do {dest}";
    }

    private async void OnSettings(object sender, RoutedEventArgs e) => await ShowSettingsAsync();

    private async void OnShortcuts(object sender, RoutedEventArgs e) => await ShowShortcutsAsync();

    private async void OnCommandPalette(object sender, RoutedEventArgs e) => await ShowCommandsAsync();

    private void CreateNote()
    {
        _vm.NewNote();
        ShowCenter(CenterViewKind.Graph);
        EditorControl.Refresh();
        UpdateEditorVisibility();
        EditorControl.DefaultFocusTarget().Focus(FocusState.Programmatic);
    }

    private void CreateProject()
    {
        _vm.NewProject();
        ShowCenter(CenterViewKind.Graph);
        EditorControl.Refresh();
        UpdateEditorVisibility();
        EditorControl.DefaultFocusTarget().Focus(FocusState.Programmatic);
    }

    private void ShowCenter(CenterViewKind kind)
    {
        _vm.CenterView = kind;
        GraphControl.Visibility = kind == CenterViewKind.Graph ? Visibility.Visible : Visibility.Collapsed;
        NotesControl.Visibility = kind == CenterViewKind.Notes ? Visibility.Visible : Visibility.Collapsed;
        TasksControl.Visibility = kind == CenterViewKind.Tasks ? Visibility.Visible : Visibility.Collapsed;
        GraphHost.Visibility = GraphControl.Visibility;
        NotesHost.Visibility = NotesControl.Visibility;
        TasksHost.Visibility = TasksControl.Visibility;
        GraphRadio.IsChecked = kind == CenterViewKind.Graph;
        NotesRadio.IsChecked = kind == CenterViewKind.Notes;
        TasksRadio.IsChecked = kind == CenterViewKind.Tasks;
        if (kind == CenterViewKind.Graph)
        {
            GraphControl.Focus(FocusState.Programmatic);
        }
        else if (kind == CenterViewKind.Notes)
        {
            NotesControl.Bind();
            NotesControl.Focus(FocusState.Programmatic);
        }
        else
        {
            TasksControl.Bind();
            TasksControl.Focus(FocusState.Programmatic);
        }
    }

    private void ChangeZoom(float delta)
    {
        var next = Math.Clamp(GraphControl.ZoomFactor + delta, 0.25f, 3f);
        GraphControl.SetZoom(next);
        _vm.SaveZoom(next);
        _lastZoom = next;
        ShowCenter(CenterViewKind.Graph);
    }

    private void FocusSearch()
    {
        SearchBox.Focus(FocusState.Programmatic);
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
                SearchBox.Focus(FocusState.Programmatic);
                break;
            case 1:
                if (_vm.CenterView == CenterViewKind.Graph)
                {
                    GraphControl.Focus(FocusState.Programmatic);
                }
                else if (_vm.CenterView == CenterViewKind.Notes)
                {
                    NotesControl.Focus(FocusState.Programmatic);
                }
                else
                {
                    TasksControl.Focus(FocusState.Programmatic);
                }

                break;
            default:
                EditorControl.DefaultFocusTarget().Focus(FocusState.Programmatic);
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
        var dialog = new ContentDialog
        {
            Title = "Zmień nazwę",
            Content = box,
            PrimaryButtonText = "Zapisz",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        box.SelectAll();
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _vm.RenameSelected(box.Text);
            EditorControl.Refresh();
        }
    }

    private async Task ShowSettingsAsync()
    {
        var panel = new SettingsDialog
        {
            ViewModel = _vm,
            HostWindow = this
        };
        panel.Bind();
        var dialog = new ContentDialog
        {
            Title = "Ustawienia",
            Content = panel,
            PrimaryButtonText = "Zamknij",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ShowShortcutsAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Skróty klawiszowe",
            Content = new ShortcutsDialog(),
            PrimaryButtonText = "Zamknij",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ShowCommandsAsync()
    {
        var palette = new CommandPaletteDialog();
        palette.SetCommands(BuildCommands());
        var dialog = new ContentDialog
        {
            Title = "Polecenia",
            Content = palette,
            PrimaryButtonText = "Uruchom",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        palette.CloseRequested = dialog.Hide;
        dialog.PrimaryButtonClick += (_, _) => palette.Confirm();
        await dialog.ShowAsync();
        palette.Chosen?.Run();
    }

    private IEnumerable<AppCommand> BuildCommands() =>
    [
        new() { Name = "Nowa notatka", Shortcut = "Ctrl+N", Run = CreateNote },
        new() { Name = "Nowy projekt", Shortcut = "Ctrl+Shift+N", Run = CreateProject },
        new() { Name = "Zapisz", Shortcut = "Ctrl+S", Run = _vm.SaveNow },
        new() { Name = "Szukaj", Shortcut = "Ctrl+F", Run = FocusSearch },
        new() { Name = "Widok grafu", Shortcut = "Ctrl+1", Run = () => ShowCenter(CenterViewKind.Graph) },
        new() { Name = "Lista notatek", Shortcut = "Ctrl+2", Run = () => ShowCenter(CenterViewKind.Notes) },
        new() { Name = "Otwarte zadania", Shortcut = "Ctrl+3", Run = () => ShowCenter(CenterViewKind.Tasks) },
        new() { Name = "Pokaż wszystkie projekty", Shortcut = "", Run = _vm.ShowAllProjects },
        new() { Name = "Zamknij panel", Shortcut = "Ctrl+W", Run = () => _vm.CloseEditor() },
        new() { Name = "Ustawienia", Shortcut = "Ctrl+,", Run = () => _ = ShowSettingsAsync() },
        new() { Name = "Skróty klawiszowe", Shortcut = "Ctrl+/", Run = () => _ = ShowShortcutsAsync() },
        new() { Name = "Eksportuj kopię", Shortcut = "", Run = () => _ = ShowSettingsAsync() },
        new() { Name = "Archiwizuj projekt", Shortcut = "", Run = _vm.ToggleArchiveSelectedProject },
        new() { Name = "Przenieś notatkę do kosza", Shortcut = "Delete", Run = _vm.TrashSelectedNote }
    ];

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var textInput = IsTextInputFocused();
        var graphOrList = IsGraphOrListFocused();

        if (e.Key == VirtualKey.F6)
        {
            CyclePanel(shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            if (_vm.IsEditorOpen)
            {
                _vm.CloseEditor();
                GraphControl.Focus(FocusState.Programmatic);
            }
            else
            {
                ShowCenter(CenterViewKind.Graph);
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == VirtualKey.W)
        {
            _vm.CloseEditor();
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && e.Key == VirtualKey.N)
        {
            return;
        }

        if (ctrl && shift && e.Key == VirtualKey.N)
        {
            return;
        }

        if (ctrl && e.Key == VirtualKey.S)
        {
            return;
        }

        if (ctrl && e.Key == VirtualKey.F)
        {
            return;
        }

        if (ctrl && e.Key is VirtualKey.Number1 or VirtualKey.NumberPad1
                 or VirtualKey.Number2 or VirtualKey.NumberPad2
                 or VirtualKey.Number3 or VirtualKey.NumberPad3)
        {
            return;
        }

        if (ctrl && shift && e.Key == VirtualKey.P)
        {
            _ = ShowCommandsAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && (IsVk(e.Key, 0xBC) || e.Key == VirtualKey.Separator))
        {
            _ = ShowSettingsAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && (IsVk(e.Key, 0xBF) || e.Key == VirtualKey.Divide || IsVk(e.OriginalKey, 0xBF)))
        {
            _ = ShowShortcutsAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == VirtualKey.Add || IsVk(e.Key, 0xBB)))
        {
            ChangeZoom(0.1f);
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == VirtualKey.Subtract || IsVk(e.Key, 0xBD)))
        {
            ChangeZoom(-0.1f);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key is VirtualKey.Number0 or VirtualKey.NumberPad0)
        {
            OnZoomReset(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (textInput && IsReservedEditorKey(e.Key, ctrl))
        {
            return;
        }

        if (ctrl && e.Key == VirtualKey.Z)
        {
            _vm.Undo();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == VirtualKey.Y)
        {
            _vm.Redo();
            e.Handled = true;
            return;
        }

        if (!graphOrList)
        {
            return;
        }

        if (e.Key == VirtualKey.F2)
        {
            _ = RenameAsync();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Delete)
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

    private static bool IsReservedEditorKey(VirtualKey key, bool ctrl)
    {
        if (key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Delete or VirtualKey.Back)
        {
            return true;
        }

        return ctrl && key is VirtualKey.C or VirtualKey.X or VirtualKey.V or VirtualKey.A or VirtualKey.Z or VirtualKey.Y;
    }

    private bool IsTextInputFocused()
    {
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot);
        return IsInTextInput(focused as DependencyObject);
    }

    private bool IsGraphOrListFocused()
    {
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, GraphControl) ||
                ReferenceEquals(focused, NotesControl) ||
                ReferenceEquals(focused, TasksControl) ||
                ReferenceEquals(focused, ProjectsList))
            {
                return true;
            }

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private static bool IsInTextInput(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is TextBox or RichEditBox or PasswordBox)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsVk(VirtualKey key, int nativeCode) => (int)key == nativeCode;
}
