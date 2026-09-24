using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    private bool _editorPageActive;
    private bool _allowClose;
    private bool _saveFailureDialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => LoadWorkspace();
        Closing += OnWindowClosing;
        AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || _vm is null || _vm.FlushPendingSaves())
        {
            return;
        }

        e.Cancel = true;
        if (_saveFailureDialogOpen)
        {
            return;
        }

        _saveFailureDialogOpen = true;
        try
        {
            var retry = new Button { Content = "Spróbuj ponownie", IsDefault = true, MinWidth = 130 };
            var cancel = new Button { Content = "Wróć do aplikacji", IsCancel = true, MinWidth = 130 };
            var discard = new Button { Content = "Zamknij bez zapisu", MinWidth = 150 };
            var preserve = new Button
            {
                Content = "Zachowaj jako osobną kopię",
                MinWidth = 190,
                IsVisible = _vm.HasSaveConflict
            };
            var decision = "cancel";
            var dialog = new Window
            {
                Title = "Nie wszystkie zmiany zostały zapisane",
                Width = 560,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = _vm.HasSaveConflict
                                ? "Plik został zmieniony także poza aplikacją. MapaNotatek nie nadpisała żadnej wersji. Możesz zachować swoją wersję jako osobną notatkę lub projekt."
                                : "Aplikacja nie może teraz bezpiecznie zapisać części zmian. Najczęstsze przyczyny to brak miejsca albo uprawnień do folderu danych.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = "Zalecane: wróć, napraw problem i użyj Plik → Zapisz. Zamknięcie bez zapisu może utracić ostatnie zmiany.",
                            TextWrapping = TextWrapping.Wrap,
                            Opacity = 0.72
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { discard, preserve, cancel, retry }
                        }
                    }
                }
            };
            retry.Click += (_, _) => { decision = "retry"; dialog.Close(); };
            discard.Click += (_, _) => { decision = "discard"; dialog.Close(); };
            preserve.Click += (_, _) => { decision = "preserve"; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);

            if (decision == "retry" && _vm.FlushPendingSaves())
            {
                _allowClose = true;
                Close();
            }
            else if (decision == "discard")
            {
                _allowClose = true;
                Close();
            }
            else if (decision == "preserve")
            {
                try
                {
                    if (_vm.PreservePendingChangesAsCopies() > 0)
                    {
                        _allowClose = true;
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync("Nie udało się zachować kopii", ex.Message);
                }
            }
        }
        finally
        {
            _saveFailureDialogOpen = false;
        }
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
            ProjectsTree.ItemsSource = _vm.ProjectTree;
            PinnedList.ItemsSource = _vm.PinnedItems;
            RecentList.ItemsSource = _vm.RecentItems;
            AttachTreeContextMenu();
            AttachTreeDragDrop();
            _notesControl.Bind();
            _notesControl.NoteSelectedOnGraph += note =>
            {
                _graphControl.Refresh();
                if (GraphHost.IsVisible)
                {
                    _graphControl.CenterOnNode(note.Id);
                }
            };
            _tasksControl.Bind();
            _graphControl.Refresh();
            _graphControl.DeleteProjectRequested += project => _ = ConfirmDeleteProjectAsync(project);
            _editorControl.DeleteProjectRequested += project => _ = ConfirmDeleteProjectAsync(project);
            _editorControl.FocusModeChanged += ApplyFocusMode;
            _editorControl.Refresh();
            StatusText.Text = _vm.DataFolder;
            UpdateEmptyState();

            _vm.GraphChanged += OnGraphChanged;
            _vm.EditorChanged += OnEditorChanged;
            _vm.FocusNodeRequested += id =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _graphControl.Refresh();
                    if (GraphHost.IsVisible)
                    {
                        _graphControl.CenterOnNode(id);
                    }
                }, DispatcherPriority.Background);
            };
            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.StatusText))
                {
                    StatusText.Text = _vm.StatusText;
                    _editorControl.UpdateSaveStatus(_vm.StatusText);
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
            var startupNote = _vm.State.RecentIds
                .Select(id => _vm.Notes.FirstOrDefault(note => string.Equals(note.Id, id, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(note => note is not null)
                ?? _vm.Notes.FirstOrDefault();
            if (startupNote is not null)
            {
                _vm.SelectNote(startupNote, openEditor: true, focusGraph: true);
                ShowEditorPage();
                _editorControl.Refresh();
                UpdateEditorVisibility();
            }
            else
            {
                ShowCenter(CenterViewKind.Notes);
            }

            if (_vm.HasStorageIssues)
            {
                _vm.StatusText = "Biblioteka została otwarta z ostrzeżeniami — sprawdź szczegóły";
                Dispatcher.UIThread.Post(
                    () => _ = ShowStorageIssuesAsync(),
                    DispatcherPriority.Background);
            }
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
            ProjectsTree.ItemsSource = null;
            ProjectsTree.ItemsSource = _vm.ProjectTree;
            PinnedList.ItemsSource = null;
            PinnedList.ItemsSource = _vm.PinnedItems;
            RecentList.ItemsSource = null;
            RecentList.ItemsSource = _vm.RecentItems;
            UpdateEditorVisibility();
            UpdateEmptyState();
        });
    }

    private void OnEditorChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _editorControl.Refresh();
            if (_vm.IsEditorOpen)
            {
                ShowEditorPage();
            }
            else if (_editorPageActive)
            {
                ShowCenter(CenterViewKind.Notes);
            }

            _notesControl.Bind();
            _tasksControl.Bind();
        });
    }

    private void UpdateEditorVisibility()
    {
        RightPanel.IsVisible = _editorPageActive && _vm.IsEditorOpen;
    }

    private void UpdateEmptyState()
    {
        EmptyStateHost.IsVisible = _vm.IsEmptyWorkspace;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) =>
        _vm.SearchQuery = SearchBox.Text ?? string.Empty;

    private void OnNewNote(object? sender, RoutedEventArgs e) => CreateNote();

    private void OnNewFromTemplate(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string templateId })
        {
            CreateNoteFromTemplate(templateId);
        }
    }

    private void OnShowTemplateMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control anchor)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var template in NoteTemplateCatalog.BuiltIn)
        {
            var item = new MenuItem
            {
                Header = template.Name
            };
            ToolTip.SetTip(item, template.Description);
            item.Click += (_, _) => CreateNoteFromTemplate(template.Id);
            menu.Items.Add(item);
        }

        anchor.ContextMenu = menu;
        menu.Open(anchor);
    }

    private void OnNewProject(object? sender, RoutedEventArgs e) => CreateProject();

    private void OnSave(object? sender, RoutedEventArgs e) => _vm.SaveNow();

    private async void OnPreserveConflictCopy(object? sender, RoutedEventArgs e)
    {
        if (!_vm.HasSaveConflict)
        {
            _vm.StatusText = "Nie ma konfliktu plików wymagającego zachowania osobnej kopii";
            return;
        }

        try
        {
            _vm.PreservePendingChangesAsCopies();
            ShowEditorPage();
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Nie udało się zachować kopii: " + ex.Message;
            await ShowMessageAsync("Nie udało się zachować kopii", ex.Message);
        }
    }

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

    private void OnEditorView(object? sender, RoutedEventArgs e)
    {
        if (EditorRadio.IsChecked != true)
        {
            return;
        }

        if (_vm.IsEditorOpen)
        {
            ShowEditorPage();
        }
        else if (_vm.SelectedNote is { } note)
        {
            _vm.SelectNote(note, openEditor: true, focusGraph: false);
        }
        else if (_vm.SelectedProject is { } project)
        {
            _vm.SelectProject(project, openEditor: true, focusGraph: false);
        }
        else
        {
            ShowCenter(CenterViewKind.Notes);
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

    private void OnZoomIn(object? sender, RoutedEventArgs e) => ChangeZoom(0.1);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => ChangeZoom(-0.1);

    private void OnZoomReset(object? sender, RoutedEventArgs e)
    {
        _graphControl.SetZoom(1);
        _vm.SaveZoom(1);
        _lastZoom = 1;
    }

    private void OnCloseEditor(object? sender, RoutedEventArgs e)
    {
        _editorControl.ExitFocusMode();
        _vm.CloseEditor();
        ShowCenter(CenterViewKind.Notes);
    }

    private void OnProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // unused — TreeView handlers below
    }

    private void OnProjectDoubleTapped(object? sender, TappedEventArgs e)
    {
        // unused
    }

    private void OnProjectsKeyDown(object? sender, KeyEventArgs e)
    {
        // unused
    }

    private void OnProjectTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProjectsTree.SelectedItem is not ProjectTreeNode node)
        {
            return;
        }

        _vm.SelectGraphNode(node.Project.Id, isProject: true);
        ShowCenter(CenterViewKind.Graph);
        _graphControl.Refresh();
        _graphControl.CenterOnNode(node.Project.Id);
    }

    private void OnProjectTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProjectsTree.SelectedItem is ProjectTreeNode node)
        {
            _vm.SelectProject(node.Project, openEditor: true, focusGraph: true, filterToProject: true);
            ShowEditorPage();
        }
    }

    private void OnPinnedSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PinnedList.SelectedItem is SidebarItem item)
        {
            NavigateSidebarItem(item, openEditor: false);
        }
    }

    private void OnRecentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem is SidebarItem item)
        {
            NavigateSidebarItem(item, openEditor: false);
        }
    }

    private void OnPinnedDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (PinnedList.SelectedItem is SidebarItem item)
        {
            NavigateSidebarItem(item, openEditor: true);
        }
    }

    private void OnRecentDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RecentList.SelectedItem is SidebarItem item)
        {
            NavigateSidebarItem(item, openEditor: true);
        }
    }

    private void NavigateSidebarItem(SidebarItem item, bool openEditor)
    {
        if (item.IsProject)
        {
            var project = _vm.Projects.FirstOrDefault(p => p.Id == item.Id);
            if (project is null)
            {
                return;
            }

            _vm.SelectProject(project, openEditor: openEditor, focusGraph: true, filterToProject: openEditor);
            _graphControl.Refresh();
            if (openEditor)
            {
                ShowEditorPage();
            }
            else
            {
                ShowCenter(CenterViewKind.Graph);
                _graphControl.CenterOnNode(project.Id);
            }
            return;
        }

        var note = _vm.Notes.FirstOrDefault(n => n.Id == item.Id);
        if (note is not null)
        {
            NavigateToNoteOnGraph(note, openEditor);
        }
    }

    private void NavigateToNoteOnGraph(Note note, bool openEditor)
    {
        _vm.SelectNote(note, openEditor: openEditor, focusGraph: true);
        _graphControl.Refresh();
        if (openEditor)
        {
            ShowEditorPage();
        }
        else
        {
            ShowCenter(CenterViewKind.Graph);
            _graphControl.CenterOnNode(note.Id);
        }
    }

    private void AttachTreeContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (ProjectsTree.SelectedItem is not ProjectTreeNode node)
            {
                return;
            }

            var p = node.Project;
            menu.Items.Add(MenuAction("Otwórz", () => _vm.SelectProject(p, true, true, true)));
            menu.Items.Add(MenuAction("Nowa notatka", () =>
            {
                _vm.SelectProject(p, false, true, true);
                CreateNote();
            }));
            menu.Items.Add(MenuAction("Nowy podprojekt", () =>
            {
                var c = _graphControl.GetViewportCenterInCanvas();
                _vm.NewProject(c.X, c.Y, p.Id);
            }));
            menu.Items.Add(MenuAction("Nowy folder wewnątrz", () =>
            {
                var c = _graphControl.GetViewportCenterInCanvas();
                _vm.NewFolder(c.X, c.Y, p.Id);
            }));
            menu.Items.Add(MenuAction(_vm.IsPinned(p.Id) ? "Odepnij" : "Przypnij", () =>
            {
                _vm.SelectProject(p, _vm.IsEditorOpen, true);
                _vm.TogglePinSelected();
            }));
            menu.Items.Add(MenuAction("Przenieś do root", () => _vm.SetProjectParent(p, null)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuAction(p.IsFolder ? "Usuń folder…" : "Usuń projekt…",
                () => _ = ConfirmDeleteProjectAsync(p)));
        };
        ProjectsTree.ContextMenu = menu;
    }

    private Point? _treeDragStart;
    private ProjectTreeNode? _treeDragNode;
    private PointerPressedEventArgs? _treeDragPress;

    private void AttachTreeDragDrop()
    {
        DragDrop.SetAllowDrop(ProjectsTree, true);
        DragDrop.AddDragOverHandler(ProjectsTree, OnTreeDragOver);
        DragDrop.AddDropHandler(ProjectsTree, OnTreeDrop);
        ProjectsTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        ProjectsTree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        ProjectsTree.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            _treeDragStart = null;
            _treeDragNode = null;
            _treeDragPress = null;
        }, RoutingStrategies.Tunnel);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ProjectsTree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _treeDragStart = e.GetPosition(ProjectsTree);
        _treeDragNode = FindTreeNodeAt(e.Source as Control);
        _treeDragPress = e;
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_treeDragStart is null || _treeDragNode is null || _treeDragPress is null ||
            !e.GetCurrentPoint(ProjectsTree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pos = e.GetPosition(ProjectsTree);
        var dx = pos.X - _treeDragStart.Value.X;
        var dy = pos.Y - _treeDragStart.Value.Y;
        if ((dx * dx) + (dy * dy) < 64)
        {
            return;
        }

        var node = _treeDragNode;
        var press = _treeDragPress;
        _treeDragStart = null;
        _treeDragNode = null;
        _treeDragPress = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText("project:" + node.Project.Id));
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        var text = e.DataTransfer.TryGetText();
        e.DragEffects = text is not null &&
                        (text.StartsWith("project:", StringComparison.Ordinal) ||
                         text.StartsWith("note:", StringComparison.Ordinal))
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        var target = FindTreeNodeAt(e.Source as Control)?.Project;
        var text = e.DataTransfer.TryGetText();
        if (target is null || text is null)
        {
            return;
        }

        if (text.StartsWith("project:", StringComparison.Ordinal))
        {
            var projectId = text["project:".Length..];
            var project = _vm.Projects.FirstOrDefault(p => p.Id == projectId);
            if (project is not null)
            {
                _vm.SetProjectParent(project, target.Id);
            }

            e.Handled = true;
            return;
        }

        if (text.StartsWith("note:", StringComparison.Ordinal))
        {
            var noteId = text["note:".Length..];
            var note = _vm.Notes.FirstOrDefault(n => n.Id == noteId);
            if (note is not null)
            {
                _vm.AttachNoteToProject(note, target);
            }

            e.Handled = true;
        }
    }

    private static ProjectTreeNode? FindTreeNodeAt(Control? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current.DataContext is ProjectTreeNode node)
            {
                return node;
            }

            current = current.Parent as Control;
        }

        return null;
    }

    private async Task ConfirmDeleteProjectAsync(Project project)
    {
        var kind = project.IsFolder ? "folder" : "projekt";
        var dialog = new Window
        {
            Title = $"Przenieś {kind} do kosza",
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var ok = false;
        var message = new TextBlock
        {
            Text = $"Przenieść {kind} „{project.Name}” do kosza? Dzieci zostaną przeniesione poziom wyżej, a notatki pozostaną. Projekt będzie można później przywrócić.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16)
        };
        var deleteBtn = new Button { Content = "Przenieś do kosza", MinWidth = 140, IsDefault = true };
        var cancelBtn = new Button { Content = "Anuluj", MinWidth = 90, IsCancel = true };
        deleteBtn.Click += (_, _) => { ok = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16),
            Children = { cancelBtn, deleteBtn }
        };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(message);
        dialog.Content = root;
        await dialog.ShowDialog(this);
        if (ok)
        {
            _vm.DeleteProject(project);
            UpdateEmptyState();
        }
    }

    private async void OnDeleteProject(object? sender, RoutedEventArgs e)
    {
        var project = _vm.SelectedProject ??
                      (ProjectsTree.SelectedItem as ProjectTreeNode)?.Project;
        if (project is null && _vm.SelectedGraphIsProject && _vm.SelectedGraphId is not null)
        {
            project = _vm.Projects.FirstOrDefault(p => p.Id == _vm.SelectedGraphId);
        }

        if (project is not null)
        {
            await ConfirmDeleteProjectAsync(project);
        }
    }

    private static MenuItem MenuAction(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private async void OnExportBackup(object? sender, RoutedEventArgs e) => await ExportBackupAsync();

    private async Task ExportBackupAsync()
    {
        if (!_vm.FlushPendingSaves())
        {
            await ShowMessageAsync(
                "Kopia nie została utworzona",
                "Nie udało się zapisać wszystkich bieżących zmian. Sprawdź uprawnienia i wolne miejsce, a następnie spróbuj ponownie.");
            return;
        }

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
        try
        {
            _vm.StatusText = "Tworzenie i sprawdzanie kopii…";
            await Task.Run(() => BackupService.ExportCopy(_vm.DataFolder, dest));
            _vm.StatusText = $"Utworzono zweryfikowaną kopię: {dest}";
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Nie udało się utworzyć kopii: " + ex.Message;
            await ShowMessageAsync("Kopia nie została utworzona", ex.Message);
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var close = new Button
        {
            Content = "OK",
            IsDefault = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            MinWidth = 88
        };
        var dialog = new Window
        {
            Title = title,
            Width = 480,
            MinHeight = 180,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task ShowStorageIssuesAsync()
    {
        var issues = _vm.StorageIssues;
        if (issues.Count == 0)
        {
            return;
        }

        var details = string.Join(
            Environment.NewLine + Environment.NewLine,
            issues.Take(10).Select(issue =>
                $"• {issue.Message}\n  {issue.Path}" +
                (issue.RecoveredFromBackup ? "\n  Wczytano lokalną kopię awaryjną." : string.Empty)));
        if (issues.Count > 10)
        {
            details += $"\n\n…oraz {issues.Count - 10} dalszych ostrzeżeń.";
        }

        await ShowMessageAsync(
            "Ostrzeżenia dotyczące danych",
            details +
            "\n\nUszkodzone pliki stanu są zachowywane w folderze Recovery. Aplikacja nigdy nie wysyła diagnostyki ani treści przez internet.");
    }

    private async void OnSettings(object? sender, RoutedEventArgs e) => await ShowSettingsAsync();

    private async void OnShortcuts(object? sender, RoutedEventArgs e) => await ShowShortcutsAsync();

    private async void OnCommandPalette(object? sender, RoutedEventArgs e) => await ShowCommandsAsync();

    private void CreateNote()
    {
        try
        {
            var center = _graphControl.GetViewportCenterInCanvas();
            _vm.NewNote(center.X, center.Y);
            ShowEditorPage();
            _editorControl.Refresh();
            UpdateEditorVisibility();
            UpdateEmptyState();
            _editorControl.DefaultFocusTarget().Focus();
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Nie udało się utworzyć notatki: " + ex.Message;
            _ = ShowMessageAsync("Nie utworzono notatki", ex.Message);
        }
    }

    private void CreateNoteFromTemplate(string templateId)
    {
        try
        {
            var template = NoteTemplateCatalog.Get(templateId);
            var center = _graphControl.GetViewportCenterInCanvas();
            var note = _vm.NewNote(center.X, center.Y);
            note.Title = NoteTemplateCatalog.ResolveTitle(template, DateTimeOffset.Now);
            note.Body = template.Body.Trim();
            note.Checklist = FrontMatter.Parse(note.Body).Checklist;
            _vm.ScheduleSaveNote(note);
            _vm.SaveNow();
            ShowEditorPage();
            _editorControl.Refresh();
            UpdateEditorVisibility();
            UpdateEmptyState();
            _editorControl.DefaultFocusTarget().Focus();
            _vm.StatusText = $"Utworzono notatkę z szablonu „{template.Name}”";
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Nie udało się utworzyć notatki z szablonu: " + ex.Message;
            _ = ShowMessageAsync("Nie utworzono notatki", ex.Message);
        }
    }

    private void CreateProject()
    {
        try
        {
            var center = _graphControl.GetViewportCenterInCanvas();
            _vm.NewProject(center.X, center.Y);
            ShowEditorPage();
            _editorControl.Refresh();
            UpdateEditorVisibility();
            UpdateEmptyState();
            _editorControl.DefaultFocusTarget().Focus();
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Nie udało się utworzyć projektu: " + ex.Message;
            _ = ShowMessageAsync("Nie utworzono projektu", ex.Message);
        }
    }

    private void OnNewFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var center = _graphControl.GetViewportCenterInCanvas();
            var parent = _vm.FocusedProject()?.Id ??
                         (ProjectsTree.SelectedItem as ProjectTreeNode)?.Project.Id;
            _vm.NewFolder(center.X, center.Y, parent);
            ShowEditorPage();
            _editorControl.Refresh();
            UpdateEditorVisibility();
            UpdateEmptyState();
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Nie udało się utworzyć folderu: " + ex.Message;
            _ = ShowMessageAsync("Nie utworzono folderu", ex.Message);
        }
    }

    private void OnTogglePin(object? sender, RoutedEventArgs e) => _vm.TogglePinSelected();

    private void OnCenterSelection(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedGraphId is not null)
        {
            ShowCenter(CenterViewKind.Graph);
            _graphControl.CenterOnNode(_vm.SelectedGraphId);
        }
    }

    private async void OnShowTrash(object? sender, RoutedEventArgs e) => await ShowTrashAsync();

    private async Task ShowTrashAsync()
    {
        var trashedNotes = _vm.LoadTrashedNotes().ToList();
        var trashedProjects = _vm.LoadTrashedProjects().ToList();
        var entries = trashedNotes
            .Select(note => (Note: (Note?)note, Project: (Project?)null, Label: $"Notatka · {note.Title}"))
            .Concat(trashedProjects.Select(project =>
                (Note: (Note?)null, Project: (Project?)project, Label: $"{(project.IsFolder ? "Folder" : "Projekt")} · {project.Name}")))
            .ToList();
        var list = new ListBox
        {
            Height = 320,
            ItemsSource = entries.Select(entry => entry.Label).ToList()
        };
        var restore = new Button { Content = "Przywróć", MinWidth = 100, IsDefault = true };
        var close = new Button { Content = "Zamknij", MinWidth = 80 };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { restore, close }
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = entries.Count == 0 ? "Kosz jest pusty." : "Wybierz element do przywrócenia:", TextWrapping = TextWrapping.Wrap },
                list
            }
        });
        var dialog = new Window
        {
            Title = "Kosz",
            Width = 480,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root
        };
        close.Click += (_, _) => dialog.Close();
        restore.Click += async (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < entries.Count)
            {
                var entry = entries[list.SelectedIndex];
                try
                {
                    if (entry.Note is not null)
                    {
                        _vm.RestoreNoteFromTrash(entry.Note);
                    }
                    else if (entry.Project is not null)
                    {
                        _vm.RestoreProjectFromTrash(entry.Project);
                    }

                    dialog.Close();
                }
                catch (Exception ex)
                {
                    _vm.StatusText = "Nie udało się przywrócić elementu: " + ex.Message;
                    dialog.Close();
                    await ShowMessageAsync("Przywracanie nie powiodło się", ex.Message);
                }
            }
        };
        await dialog.ShowDialog(this);
    }

    private void ShowCenter(CenterViewKind kind)
    {
        _editorControl.ExitFocusMode();
        _editorPageActive = false;
        _vm.CenterView = kind;
        RightPanel.IsVisible = false;
        GraphHost.IsVisible = kind == CenterViewKind.Graph;
        NotesHost.IsVisible = kind == CenterViewKind.Notes;
        TasksHost.IsVisible = kind == CenterViewKind.Tasks;
        EditorRadio.IsChecked = false;
        GraphRadio.IsChecked = kind == CenterViewKind.Graph;
        NotesRadio.IsChecked = kind == CenterViewKind.Notes;
        TasksRadio.IsChecked = kind == CenterViewKind.Tasks;
        UpdateEmptyState();
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

    private void ShowEditorPage()
    {
        if (!_vm.IsEditorOpen)
        {
            return;
        }

        _editorPageActive = true;
        GraphHost.IsVisible = false;
        NotesHost.IsVisible = false;
        TasksHost.IsVisible = false;
        EmptyStateHost.IsVisible = false;
        RightPanel.IsVisible = true;
        EditorRadio.IsChecked = true;
        GraphRadio.IsChecked = false;
        NotesRadio.IsChecked = false;
        TasksRadio.IsChecked = false;
    }

    private void ApplyFocusMode(bool enabled)
    {
        SidebarPanel.IsVisible = !enabled;
        WorkspaceNav.IsVisible = !enabled;
        MainMenu.IsVisible = !enabled;
        StatusBar.IsVisible = !enabled;
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

    private void FindInDocument()
    {
        if (_editorPageActive && _vm.SelectedNote is not null)
        {
            _editorControl.ShowFindPanel();
            return;
        }

        FocusSearch();
    }

    private void OnFindInDocument(object? sender, RoutedEventArgs e) => FindInDocument();

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
            try
            {
                _vm.RenameSelected(box.Text ?? string.Empty);
                _editorControl.Refresh();
            }
            catch (Exception ex)
            {
                _vm.StatusText = "Nie udało się zmienić nazwy: " + ex.Message;
                await ShowMessageAsync("Zmiana nazwy nie powiodła się", ex.Message);
            }
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
        new() { Name = "Szablon: spotkanie", Shortcut = "", Run = () => CreateNoteFromTemplate("meeting") },
        new() { Name = "Szablon: decyzja", Shortcut = "", Run = () => CreateNoteFromTemplate("decision") },
        new() { Name = "Szablon: plan projektu", Shortcut = "", Run = () => CreateNoteFromTemplate("project-brief") },
        new() { Name = "Szablon: procedura", Shortcut = "", Run = () => CreateNoteFromTemplate("procedure") },
        new() { Name = "Szablon: notatka dzienna", Shortcut = "", Run = () => CreateNoteFromTemplate("daily") },
        new() { Name = "Nowy projekt", Shortcut = PlatformKeys.ChordShift("N"), Run = CreateProject },
        new() { Name = "Nowy folder", Shortcut = "", Run = () => OnNewFolder(null, new RoutedEventArgs()) },
        new() { Name = "Zapisz", Shortcut = PlatformKeys.Chord("S"), Run = _vm.SaveNow },
        new() { Name = "Znajdź w dokumencie", Shortcut = PlatformKeys.Chord("F"), Run = FindInDocument },
        new() { Name = "Szukaj w całej bibliotece", Shortcut = PlatformKeys.ChordShift("F"), Run = FocusSearch },
        new() { Name = "Widok grafu", Shortcut = PlatformKeys.Chord("1"), Run = () => ShowCenter(CenterViewKind.Graph) },
        new() { Name = "Lista notatek", Shortcut = PlatformKeys.Chord("2"), Run = () => ShowCenter(CenterViewKind.Notes) },
        new() { Name = "Otwarte zadania", Shortcut = PlatformKeys.Chord("3"), Run = () => ShowCenter(CenterViewKind.Tasks) },
        new() { Name = "Pokaż wszystkie projekty", Shortcut = "", Run = _vm.ShowAllProjects },
        new() { Name = "Kosz", Shortcut = "", Run = () => _ = ShowTrashAsync() },
        new() { Name = "Przypnij / odepnij", Shortcut = "", Run = _vm.TogglePinSelected },
        new() { Name = "Wyśrodkuj zaznaczenie", Shortcut = "", Run = () => OnCenterSelection(null, new RoutedEventArgs()) },
        new() { Name = "Zamknij dokument", Shortcut = PlatformKeys.Chord("W"), Run = () => _vm.CloseEditor() },
        new() { Name = "Ustawienia", Shortcut = PlatformKeys.Chord(","), Run = () => _ = ShowSettingsAsync() },
        new() { Name = "Skróty klawiszowe", Shortcut = PlatformKeys.Chord("/"), Run = () => _ = ShowShortcutsAsync() },
        new() { Name = "Eksportuj kopię", Shortcut = "", Run = () => _ = ExportBackupAsync() },
        new() { Name = "Usuń folder / projekt…", Shortcut = "", Run = () => OnDeleteProject(null, new RoutedEventArgs()) },
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
            if (_editorControl.IsFindPanelOpen)
            {
                _editorControl.CloseFindPanel();
            }
            else if (_editorControl.IsFocusModeEnabled)
            {
                _editorControl.ExitFocusMode();
            }
            else if (_editorPageActive && _vm.IsEditorOpen)
            {
                _vm.CloseEditor();
                ShowCenter(CenterViewKind.Notes);
            }
            else if (_vm.FocusedProjectId is not null)
            {
                _vm.ShowAllProjects();
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
            _editorControl.ExitFocusMode();
            _vm.CloseEditor();
            ShowCenter(CenterViewKind.Notes);
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
            if (shift)
            {
                FocusSearch();
            }
            else
            {
                FindInDocument();
            }
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
                ReferenceEquals(focused, ProjectsTree))
            {
                return true;
            }

            focused = focused.GetVisualParent();
        }

        return false;
    }
}
