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
    private PeopleView _peopleControl = null!;
    private EditorPanel _editorControl = null!;
    private int _panelIndex;
    private double _lastZoom = 1;
    private bool _zoomSaveReady;
    private bool _rightAltHeld;
    private readonly DispatcherTimer _zoomSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _workspaceLoaded;
    private bool _graphWasPresented;
    private bool _editorPageActive;
    private bool _sidebarVisiblePreference = true;
    private bool _focusModeActive;
    private bool _allowClose;
    private bool _saveFailureDialogOpen;
    private readonly Stack<NavigationSnapshot> _backHistory = new();
    private readonly Stack<NavigationSnapshot> _forwardHistory = new();
    private bool _restoringNavigation;
    private bool _navigationCapturedForPendingEditor;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => LoadWorkspace();
        Closing += OnWindowClosing;
        Deactivated += (_, _) => _rightAltHeld = false;
        AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnRootKeyUp, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        _zoomSaveTimer.Tick += (_, _) =>
        {
            _zoomSaveTimer.Stop();
            if (_zoomSaveReady && _vm is not null)
            {
                _vm.SaveZoom(_lastZoom);
            }
        };
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || _vm is null)
        {
            return;
        }

        _zoomSaveTimer.Stop();
        if (_zoomSaveReady)
        {
            _vm.SaveZoom(_lastZoom);
        }

        e.Cancel = true;
        if (_saveFailureDialogOpen)
        {
            return;
        }

        if (_vm.FlushPendingSaves())
        {
            _saveFailureDialogOpen = true;
            try
            {
                await PromptBackupAndCloseAsync();
            }
            finally
            {
                _saveFailureDialogOpen = false;
            }
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
                await PromptBackupAndCloseAsync();
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
                        await PromptBackupAndCloseAsync();
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

    private async Task PromptBackupAndCloseAsync()
    {
        while (true)
        {
            var update = new Button { Content = "Zaktualizuj kopię i zamknij", MinWidth = 190, IsDefault = true };
            var change = new Button { Content = "Zmień miejsce kopii", MinWidth = 150 };
            var without = new Button { Content = "Zamknij bez nowej kopii", MinWidth = 175 };
            var cancel = new Button { Content = "Wróć do aplikacji", MinWidth = 135, IsCancel = true };
            var decision = "cancel";
            var target = _vm.State.BackupFolder;
            var dialog = new Window
            {
                Title = "Kopia bezpieczeństwa",
                Width = 620,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(target)
                                ? "Nie wskazano jeszcze miejsca kopii. Przed zamknięciem wybierz osobny folder na dysku."
                                : $"Czy zaktualizować zweryfikowaną kopię Current/Previous?\n{target}",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = "Najpierw powstanie kompletna nowa kopia i zostanie sprawdzona. Poprzednia poprawna wersja nie jest usuwana, dopóki nowa nie przejdzie weryfikacji.",
                            Classes = { "metadata" },
                            TextWrapping = TextWrapping.Wrap
                        },
                        new WrapPanel
                        {
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            ItemSpacing = 8,
                            LineSpacing = 8,
                            Children = { cancel, without, change, update }
                        }
                    }
                }
            };
            update.Click += (_, _) => { decision = "update"; dialog.Close(); };
            change.Click += (_, _) => { decision = "change"; dialog.Close(); };
            without.Click += (_, _) => { decision = "without"; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);

            if (decision == "cancel")
            {
                return;
            }
            if (decision == "without")
            {
                _allowClose = true;
                Close();
                return;
            }
            if (decision == "change" || string.IsNullOrWhiteSpace(target))
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Wybierz zewnętrzny folder kopii MapaNotatek",
                    AllowMultiple = false
                });
                if (folders.Count == 0)
                {
                    continue;
                }

                target = Path.Combine(folders[0].Path.LocalPath, "MapaNotatek-Backup");
                _vm.SaveBackupFolder(target);
                if (decision == "change")
                {
                    continue;
                }
            }

            try
            {
                _vm.StatusText = "Tworzenie i sprawdzanie kopii bezpieczeństwa…";
                await Task.Run(() => BackupService.UpdateRotatingCopy(_vm.DataFolder, target!));
                _vm.StatusText = "Kopia bezpieczeństwa została zweryfikowana";
                _allowClose = true;
                Close();
                return;
            }
            catch (Exception ex)
            {
                _vm.StatusText = "Nie udało się zaktualizować kopii: " + ex.Message;
                await ShowMessageAsync(
                    "Kopia nie została zaktualizowana",
                    ex.Message + "\n\nAplikacja pozostaje otwarta. Możesz ponowić próbę, zmienić miejsce albo świadomie zamknąć bez nowej kopii.");
            }
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
            _tasksControl.TaskCountsChanged += UpdateTaskCounters;
            _peopleControl = new PeopleView();
            _editorControl = new EditorPanel();
            GraphHost.Content = _graphControl;
            NotesHost.Content = _notesControl;
            TasksHost.Content = _tasksControl;
            PeopleHost.Content = _peopleControl;
            EditorHost.Content = _editorControl;

            _vm = new MainViewModel();
            _sidebarVisiblePreference = _vm.State.SidebarVisible;
            ApplyShellVisibility();
            _graphControl.ViewModel = _vm;
            _notesControl.ViewModel = _vm;
            _tasksControl.ViewModel = _vm;
            _peopleControl.ViewModel = _vm;
            _editorControl.ViewModel = _vm;
            LibraryTree.ItemsSource = _vm.NavigationTree;
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
            _peopleControl.ProjectOpenRequested += project =>
            {
                _vm.SelectProject(project, openEditor: true, focusGraph: true);
                ShowEditorPage();
            };
            _peopleControl.NoteOpenRequested += note =>
            {
                _vm.SelectNote(note, openEditor: true, focusGraph: true);
                ShowEditorPage();
            };
            _peopleControl.DeletePersonRequested += person => _ = ConfirmDeletePersonAsync(person);
            _peopleControl.Refresh();
            _graphControl.Refresh();
            _graphControl.DeleteProjectRequested += project => _ = ConfirmDeleteProjectAsync(project);
            _graphControl.ExportSystemRequested += project => _ = ExportSystemAsync(project);
            _graphControl.ConfirmHierarchyMove = ConfirmHierarchyMoveAsync;
            _editorControl.DeleteProjectRequested += project => _ = ConfirmDeleteProjectAsync(project);
            _editorControl.NewProjectNoteRequested += CreateNote;
            _editorControl.FocusModeChanged += ApplyFocusMode;
            _editorControl.Refresh();
            StatusText.Text = string.IsNullOrWhiteSpace(_vm.StatusText) ? _vm.DataFolder : _vm.StatusText;
            UpdateEmptyState();

            _vm.GraphChanged += OnGraphChanged;
            _vm.PeopleChanged += OnPeopleChanged;
            _vm.EditorChanged += OnEditorChanged;
            _vm.EditorOpenRequested += OnEditorOpenRequested;
            _vm.NavigationStarting += OnNavigationStarting;
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

                _lastZoom = zoom;
                _zoomSaveTimer.Stop();
                _zoomSaveTimer.Start();
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
            else if (!string.IsNullOrWhiteSpace(_vm.LibrarySafetyMessage))
            {
                Dispatcher.UIThread.Post(
                    () => _ = ShowMessageAsync(
                        _vm.IsLibraryReadOnly ? "Biblioteka tylko do odczytu" : "Bezpieczna migracja biblioteki",
                        _vm.LibrarySafetyMessage),
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
            if (PeopleHost.IsVisible)
            {
                _peopleControl.Refresh();
            }
            UpdateEditorVisibility();
            UpdateEmptyState();
        });
    }

    private void UpdateTaskCounters(int openCount, int totalCount)
    {
        TasksTabCountText.Text = openCount.ToString();
        ToolTip.SetTip(TasksRadio, $"Otwarte zadania: {openCount} z {totalCount}");
    }

    private void OnPeopleChanged()
    {
        Dispatcher.UIThread.Post(_peopleControl.Refresh);
    }

    private void OnEditorChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _editorControl.Refresh();
            if (_editorPageActive && !_vm.IsEditorOpen)
            {
                ShowCenter(CenterViewKind.Notes);
            }

            _notesControl.Bind();
            _tasksControl.Bind();
        });
    }

    private void OnEditorOpenRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_vm.IsEditorOpen)
            {
                ShowEditorPage();
            }
        });
    }

    private void OnNavigationStarting()
    {
        if (_restoringNavigation || _editorPageActive)
        {
            return;
        }

        RecordCurrentNavigation();
        _navigationCapturedForPendingEditor = true;
    }

    private void UpdateEditorVisibility()
    {
        RightPanel.IsVisible = _editorPageActive && _vm.IsEditorOpen;
    }

    private void UpdateEmptyState()
    {
        EmptyStateHost.IsVisible = _vm.IsEmptyWorkspace &&
                                   _vm.CenterView != CenterViewKind.People &&
                                   !_editorPageActive;
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
        Dispatcher.UIThread.Post(() => menu.Open(anchor), DispatcherPriority.Input);
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

    private void OnPeopleView(object? sender, RoutedEventArgs e)
    {
        if (PeopleRadio.IsChecked == true)
        {
            ShowCenter(CenterViewKind.People);
        }
    }

    private void OnGraphViewMenu(object? sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Graph);

    private void OnNotesViewMenu(object? sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Notes);

    private void OnTasksViewMenu(object? sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.Tasks);

    private void OnPeopleViewMenu(object? sender, RoutedEventArgs e) => ShowCenter(CenterViewKind.People);

    private void OnNewPerson(object? sender, RoutedEventArgs e)
    {
        ShowCenter(CenterViewKind.People);
        _peopleControl.CreatePerson();
    }

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

    private void OnLibraryTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LibraryTree.SelectedItem is not NavigationTreeNode node)
        {
            return;
        }

        if (node.IsGroup)
        {
            return;
        }

        _vm.SelectGraphNode(node.ItemId, node.IsProject);
        ShowCenter(CenterViewKind.Graph);
        _graphControl.Refresh();
        _graphControl.CenterOnNode(node.ItemId);
    }

    private void OnLibraryTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (LibraryTree.SelectedItem is not NavigationTreeNode node)
        {
            return;
        }

        if (node.Project is { } project)
        {
            _vm.SelectProject(project, openEditor: true, focusGraph: true, filterToProject: true);
        }
        else if (node.Note is { } note)
        {
            _vm.SelectNote(note, openEditor: true, focusGraph: true);
        }

        if (!node.IsGroup)
        {
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
            if (LibraryTree.SelectedItem is not NavigationTreeNode node)
            {
                return;
            }

            if (node.Note is { } note)
            {
                menu.Items.Add(MenuAction("Otwórz", () =>
                {
                    _vm.SelectNote(note, openEditor: true, focusGraph: true);
                    ShowEditorPage();
                }));
                menu.Items.Add(MenuAction(_vm.IsPinned(note.Id) ? "Odepnij" : "Przypnij", () =>
                {
                    _vm.SelectNote(note, _vm.IsEditorOpen, focusGraph: true);
                    _vm.TogglePinSelected();
                }));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuAction("Przenieś notatkę do kosza", () =>
                {
                    _vm.SelectNote(note, openEditor: false, focusGraph: true);
                    _vm.TrashSelectedNote();
                }));
                return;
            }

            if (node.Project is not { } p)
            {
                return;
            }

            menu.Items.Add(MenuAction("Otwórz", () => _vm.SelectProject(p, true, true, true)));
            menu.Items.Add(MenuAction("Nowa notatka", () =>
            {
                _vm.SelectProject(p, false, true, true);
                CreateNote();
            }));
            var addInside = new MenuItem { Header = "Dodaj wewnątrz" };
            foreach (var itemType in p.ItemType.AllowedChildren())
            {
                var capturedType = itemType;
                addInside.Items.Add(MenuAction(itemType.Label(), () => CreateProject(capturedType, p.Id)));
            }

            if (addInside.Items.Count > 0)
            {
                menu.Items.Add(addInside);
            }
            menu.Items.Add(MenuAction(_vm.IsPinned(p.Id) ? "Odepnij" : "Przypnij", () =>
            {
                _vm.SelectProject(p, _vm.IsEditorOpen, true);
                _vm.TogglePinSelected();
            }));
            menu.Items.Add(MenuAction("Przenieś do root", () => _vm.SetProjectParent(p, null)));
            if (p.ItemType == ProjectItemType.System)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuAction("Eksportuj system…", () => _ = ExportSystemAsync(p)));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuAction($"Usuń: {p.ItemType.Label().ToLowerInvariant()}…",
                () => _ = ConfirmDeleteProjectAsync(p)));
        };
        LibraryTree.ContextMenu = menu;
    }

    private Point? _treeDragStart;
    private NavigationTreeNode? _treeDragNode;
    private PointerPressedEventArgs? _treeDragPress;

    private void AttachTreeDragDrop()
    {
        DragDrop.SetAllowDrop(LibraryTree, true);
        DragDrop.AddDragOverHandler(LibraryTree, OnTreeDragOver);
        DragDrop.AddDropHandler(LibraryTree, OnTreeDrop);
        LibraryTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        LibraryTree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        LibraryTree.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            _treeDragStart = null;
            _treeDragNode = null;
            _treeDragPress = null;
        }, RoutingStrategies.Tunnel);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(LibraryTree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _treeDragStart = e.GetPosition(LibraryTree);
        _treeDragNode = FindTreeNodeAt(e.Source as Control);
        _treeDragPress = e;
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_treeDragStart is null || _treeDragNode is null || _treeDragPress is null ||
            !e.GetCurrentPoint(LibraryTree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pos = e.GetPosition(LibraryTree);
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
        var payload = node.Project is { } project
            ? "project:" + project.Id
            : node.Note is { } note
                ? "note:" + note.Id
                : null;
        if (payload is null)
        {
            return;
        }

        data.Add(DataTransferItem.CreateText(payload));
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        var text = e.DataTransfer.TryGetText();
        var target = FindTreeNodeAt(e.Source as Control)?.Project;
        var canDrop = target is not null && text is not null &&
                      (text.StartsWith("note:", StringComparison.Ordinal) ||
                       (text.StartsWith("project:", StringComparison.Ordinal) &&
                        _vm.Projects.FirstOrDefault(project => project.Id == text["project:".Length..]) is { } dragged &&
                        (target.ItemType == ProjectItemType.System && MainViewModel.SupportsSystemMembership(dragged) ||
                         _vm.CanSetProjectParent(dragged, target))));
        e.DragEffects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
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
                if (target.ItemType == ProjectItemType.System && MainViewModel.SupportsSystemMembership(project))
                {
                    _vm.AddProjectToSystem(project, target);
                }
                else
                {
                    _vm.SetProjectParent(project, target.Id);
                }
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

    private static NavigationTreeNode? FindTreeNodeAt(Control? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current.DataContext is NavigationTreeNode node)
            {
                return node;
            }

            current = current.Parent as Control;
        }

        return null;
    }

    private async Task ConfirmDeleteProjectAsync(Project project)
    {
        var kind = project.ItemType.Label().ToLowerInvariant();
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
            Text = $"Przenieść {kind} „{project.Name}” do kosza? Dzieci zostaną przeniesione poziom wyżej, a notatki pozostaną. Element będzie można później przywrócić.",
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

    private async Task<bool> ConfirmHierarchyMoveAsync(Project child, Project parent)
    {
        var accepted = false;
        var move = new Button { Content = "Zmień rodzica", MinWidth = 120, IsDefault = true };
        var cancel = new Button { Content = "Anuluj", MinWidth = 90, IsCancel = true };
        var dialog = new Window
        {
            Title = "Zmiana hierarchii",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"„{child.Name}” ma już element nadrzędny. Czy przenieść go pod „{parent.Name}”? Przypisania do systemów pozostaną bez zmian.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, move }
                    }
                }
            }
        };
        move.Click += (_, _) => { accepted = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return accepted;
    }

    private async Task ConfirmDeletePersonAsync(Person person)
    {
        var assignments = _vm.CountPersonAssignments(person);
        var dialog = new Window
        {
            Title = "Przenieś osobę do kosza",
            Width = 460,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var ok = false;
        var message = new TextBlock
        {
            Text = $"Przenieść osobę „{person.Name}” do kosza? Zostanie usunięta z {assignments.Projects} projektów i folderów, {assignments.Notes} notatek oraz {assignments.Tasks} zadań. Profil będzie można przywrócić, ale przypisania nie zostaną wtedy odtworzone automatycznie.",
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
        if (ok && _vm.DeletePerson(person))
        {
            _peopleControl.Refresh();
            UpdateEmptyState();
        }
    }

    private async void OnDeleteProject(object? sender, RoutedEventArgs e)
    {
        var project = _vm.SelectedProject ??
                      (LibraryTree.SelectedItem as NavigationTreeNode)?.Project;
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

    private async void OnExportSystem(object? sender, RoutedEventArgs e) => await ExportSystemAsync();

    private async Task ExportSystemAsync(Project? system = null)
    {
        system ??= _vm.SelectedProject ?? (LibraryTree.SelectedItem as NavigationTreeNode)?.Project;
        if (system is null && _vm.SelectedGraphIsProject && _vm.SelectedGraphId is not null)
        {
            system = _vm.Projects.FirstOrDefault(project => project.Id == _vm.SelectedGraphId);
        }

        if (system?.ItemType != ProjectItemType.System)
        {
            await ShowMessageAsync("Wybierz system", "Zaznacz system w drzewie albo na grafie, a następnie ponów eksport.");
            return;
        }

        if (!_vm.FlushPendingSaves())
        {
            await ShowMessageAsync("Nie można utworzyć paczki", "Najpierw zapisz wszystkie bieżące zmiany.");
            return;
        }

        var safeName = SlugHelper.FromName(system.Name);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Eksportuj system jako propozycję zmian",
            SuggestedFileName = $"{safeName}-{DateTime.Now:yyyyMMdd-HHmm}{SystemTransferPackageService.Extension}",
            DefaultExtension = SystemTransferPackageService.Extension.TrimStart('.'),
            FileTypeChoices =
            [
                new FilePickerFileType("Pakiet MapaNotatek")
                {
                    Patterns = [$"*{SystemTransferPackageService.Extension}"],
                    MimeTypes = ["application/zip"]
                }
            ]
        });
        if (file is null)
        {
            return;
        }

        try
        {
            _vm.StatusText = "Tworzenie i weryfikowanie paczki systemu…";
            var systemId = system.Id;
            var state = _vm.State;
            var path = file.Path.LocalPath;
            await Task.Run(() => SystemTransferPackageService.Export(_vm.DataFolder, systemId, path, state));
            _vm.StatusText = $"Utworzono paczkę systemu: {path}";
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Nie udało się wyeksportować systemu: " + ex.Message;
            await ShowMessageAsync("Nie utworzono paczki", ex.Message);
        }
    }

    private async void OnOpenMergeProposal(object? sender, RoutedEventArgs e) => await OpenMergeProposalAsync();

    private async Task OpenMergeProposalAsync()
    {
        if (!_vm.FlushPendingSaves())
        {
            await ShowMessageAsync("Nie można otworzyć propozycji", "Najpierw zapisz wszystkie bieżące zmiany.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Otwórz propozycję scalenia",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Pakiet MapaNotatek")
                {
                    Patterns = [$"*{SystemTransferPackageService.Extension}"],
                    MimeTypes = ["application/zip"]
                }
            ]
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            _vm.StatusText = "Sprawdzanie paczki i porównywanie zmian…";
            var path = files[0].Path.LocalPath;
            var proposal = await Task.Run(() => SystemMergeService.CreateProposal(_vm.DataFolder, path));
            var dialog = new MergeReviewDialog(proposal);
            var accepted = await dialog.ShowDialog<bool>(this);
            if (!accepted || !dialog.Accepted)
            {
                _vm.StatusText = "Propozycja scalenia została zamknięta bez zmian";
                return;
            }

            _vm.StatusText = "Scalanie na bezpiecznej kopii biblioteki…";
            var result = await Task.Run(() => SystemMergeService.Apply(_vm.DataFolder, proposal));
            _vm.ChangeDataFolder(_vm.DataFolder, loadLibraryState: true);
            _graphControl.Refresh();
            _notesControl.Bind();
            _tasksControl.Bind();
            _peopleControl.Refresh();
            UpdateEmptyState();
            _vm.StatusText = $"Scalono: +{result.Added}, zmieniono {result.Modified}, usunięto {result.Deleted}, pominięto {result.Skipped}";
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Scalenie nie zostało wykonane: " + ex.Message;
            await ShowMessageAsync("Nie udało się scalić zmian", ex.Message);
        }
    }

    private async void OnMergeHistory(object? sender, RoutedEventArgs e) => await ShowMergeHistoryAsync();

    private async Task ShowMergeHistoryAsync()
    {
        var history = SystemMergeService.ListHistory(_vm.DataFolder);
        var list = new ListBox
        {
            ItemsSource = history.Count == 0
                ? [new MergeHistoryListItem(null, "Brak wykonanych scaleń.")]
                : history.Select(item => new MergeHistoryListItem(
                    item,
                    $"{item.MergedUtc.ToLocalTime():g}  •  {item.SourceSystemName}  •  +{item.Added} ~{item.Modified} −{item.Deleted}")).ToList()
        };
        list.SelectedIndex = history.Count > 0 ? 0 : -1;
        var close = new Button { Content = "Zamknij", MinWidth = 90, IsDefault = true };
        var revert = new Button { Content = "Przygotuj cofnięcie…", MinWidth = 170, IsEnabled = history.Count > 0 };
        var dialog = new Window
        {
            Title = "Historia scaleń",
            Width = 760,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(14),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 12, 0, 0),
                        Children = { revert, close },
                        [DockPanel.DockProperty] = Dock.Bottom
                    },
                    list
                }
            }
        };
        list.SelectionChanged += (_, _) =>
            revert.IsEnabled = (list.SelectedItem as MergeHistoryListItem)?.Entry is not null;
        close.Click += (_, _) => dialog.Close();
        revert.Click += (_, _) =>
        {
            if ((list.SelectedItem as MergeHistoryListItem)?.Entry is not { } entry)
            {
                return;
            }
            dialog.Close(entry);
        };
        var selected = await dialog.ShowDialog<MergeHistoryEntry?>(this);
        if (selected is not null)
        {
            await RevertMergeAsync(selected);
        }
    }

    private async Task RevertMergeAsync(MergeHistoryEntry entry)
    {
        try
        {
            _vm.StatusText = "Przygotowywanie odwrotnej propozycji zmian…";
            var proposal = await Task.Run(() => SystemMergeService.CreateRevertProposal(_vm.DataFolder, entry.MergeId));
            if (proposal.Changes.Count == 0)
            {
                await ShowMessageAsync("Brak zmian do cofnięcia", "Stan biblioteki jest już zgodny ze stanem sprzed tego scalenia.");
                return;
            }

            var review = new MergeReviewDialog(proposal);
            var accepted = await review.ShowDialog<bool>(this);
            if (!accepted || !review.Accepted)
            {
                _vm.StatusText = "Cofnięcie zostało anulowane";
                return;
            }

            _vm.StatusText = "Cofanie scalenia na bezpiecznej kopii…";
            var result = await Task.Run(() => SystemMergeService.ApplyRevert(_vm.DataFolder, proposal));
            _vm.ChangeDataFolder(_vm.DataFolder, loadLibraryState: true);
            _graphControl.Refresh();
            _notesControl.Bind();
            _tasksControl.Bind();
            _peopleControl.Refresh();
            UpdateEmptyState();
            _vm.StatusText = $"Cofnięto scalenie: +{result.Added}, zmieniono {result.Modified}, usunięto {result.Deleted}";
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Nie udało się cofnąć scalenia: " + ex.Message;
            await ShowMessageAsync("Nie cofnięto scalenia", ex.Message);
        }
    }

    private sealed record MergeHistoryListItem(MergeHistoryEntry? Entry, string Text)
    {
        public override string ToString() => Text;
    }

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
            _vm.NormalizeTaskItems(note.Checklist);
            var templateBlocks = VisualDocumentService.Parse(note.Body);
            var taskIndex = 0;
            foreach (var block in templateBlocks.Where(block => block.Kind == DocumentBlockKind.Checklist))
            {
                var task = note.Checklist[taskIndex++];
                block.TaskId = task.Id;
                block.TaskPriority = task.Priority;
            }
            note.Body = VisualDocumentService.Serialize(templateBlocks);
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

    private void CreateProject() => CreateProject(ProjectItemType.Project);

    private void CreateProject(ProjectItemType itemType, string? explicitParentId = null)
    {
        try
        {
            var center = _graphControl.GetViewportCenterInCanvas();
            var parentId = explicitParentId ?? _vm.FocusedProject()?.Id ??
                           (LibraryTree.SelectedItem as NavigationTreeNode)?.Project?.Id;
            _vm.NewProject(itemType, center.X, center.Y, parentId);
            ShowEditorPage();
            _editorControl.Refresh();
            UpdateEditorVisibility();
            UpdateEmptyState();
            _editorControl.DefaultFocusTarget().Focus();
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Nie udało się utworzyć elementu typu {itemType.Label().ToLowerInvariant()}: " + ex.Message;
            _ = ShowMessageAsync("Nie utworzono elementu", ex.Message);
        }
    }

    private void OnNewProjectItem(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string tag } ||
            !Enum.TryParse<ProjectItemType>(tag, ignoreCase: true, out var itemType))
        {
            return;
        }

        CreateProject(itemType);
    }

    private void OnNewFolder(object? sender, RoutedEventArgs e) =>
        CreateProject(ProjectItemType.Folder);

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
        var trashedPeople = _vm.LoadTrashedPeople().ToList();
        var entries = trashedNotes
            .Select(note => (Item: (object)note, Label: $"Notatka · {note.Title}"))
            .Concat(trashedProjects.Select(project =>
                (Item: (object)project, Label: $"{project.ItemType.Label()} · {project.Name}")))
            .Concat(trashedPeople.Select(person =>
                (Item: (object)person, Label: $"Osoba · {person.Name}")))
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
                    if (entry.Item is Note note)
                    {
                        _vm.RestoreNoteFromTrash(note);
                    }
                    else if (entry.Item is Project project)
                    {
                        _vm.RestoreProjectFromTrash(project);
                    }
                    else if (entry.Item is Person person)
                    {
                        _vm.RestorePersonFromTrash(person);
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

    private void ShowCenter(CenterViewKind kind, bool recordHistory = true)
    {
        var changingView = _editorPageActive || _vm.CenterView != kind ||
                           !(kind switch
                           {
                               CenterViewKind.Graph => GraphHost.IsVisible,
                               CenterViewKind.Notes => NotesHost.IsVisible,
                               CenterViewKind.Tasks => TasksHost.IsVisible,
                               _ => PeopleHost.IsVisible
                           });
        if (recordHistory && changingView && !_restoringNavigation)
        {
            RecordCurrentNavigation();
        }

        _editorControl.ExitFocusMode();
        _editorPageActive = false;
        _vm.CenterView = kind;
        RightPanel.IsVisible = false;
        GraphHost.IsVisible = kind == CenterViewKind.Graph;
        NotesHost.IsVisible = kind == CenterViewKind.Notes;
        TasksHost.IsVisible = kind == CenterViewKind.Tasks;
        PeopleHost.IsVisible = kind == CenterViewKind.People;
        EditorRadio.IsChecked = false;
        GraphRadio.IsChecked = kind == CenterViewKind.Graph;
        NotesRadio.IsChecked = kind == CenterViewKind.Notes;
        TasksRadio.IsChecked = kind == CenterViewKind.Tasks;
        PeopleRadio.IsChecked = kind == CenterViewKind.People;
        UpdateEmptyState();
        if (kind == CenterViewKind.Graph)
        {
            _graphControl.Focus();
            if (!_graphWasPresented)
            {
                _graphWasPresented = true;
                Dispatcher.UIThread.Post(_graphControl.FitToContent, DispatcherPriority.Background);
            }
        }
        else if (kind == CenterViewKind.Notes)
        {
            _notesControl.Bind();
            _notesControl.Focus();
        }
        else if (kind == CenterViewKind.Tasks)
        {
            _tasksControl.Bind();
            _tasksControl.Focus();
        }
        else
        {
            _peopleControl.Refresh();
            _peopleControl.Focus();
        }
    }

    private void ShowEditorPage(bool recordHistory = true)
    {
        if (!_vm.IsEditorOpen)
        {
            return;
        }

        if (recordHistory && !_editorPageActive && !_restoringNavigation && !_navigationCapturedForPendingEditor)
        {
            RecordCurrentNavigation();
        }
        _navigationCapturedForPendingEditor = false;

        _editorPageActive = true;
        GraphHost.IsVisible = false;
        NotesHost.IsVisible = false;
        TasksHost.IsVisible = false;
        PeopleHost.IsVisible = false;
        EmptyStateHost.IsVisible = false;
        RightPanel.IsVisible = true;
        EditorRadio.IsChecked = true;
        GraphRadio.IsChecked = false;
        NotesRadio.IsChecked = false;
        TasksRadio.IsChecked = false;
        PeopleRadio.IsChecked = false;
    }

    private NavigationSnapshot CaptureNavigationSnapshot() => new(
        _editorPageActive,
        _vm.CenterView,
        _vm.SelectedProject?.Id,
        _vm.SelectedNote?.Id,
        _graphControl.CaptureState());

    private void RecordCurrentNavigation()
    {
        if (!_workspaceLoaded || _vm is null || _restoringNavigation)
        {
            return;
        }

        var snapshot = CaptureNavigationSnapshot();
        if (_backHistory.TryPeek(out var previous) && previous.SameDestination(snapshot))
        {
            return;
        }

        _backHistory.Push(snapshot);
        while (_backHistory.Count > 100)
        {
            var kept = _backHistory.ToArray().Take(100).ToArray();
            _backHistory.Clear();
            foreach (var item in kept.Reverse())
            {
                _backHistory.Push(item);
            }
        }
        _forwardHistory.Clear();
        UpdateNavigationButtons();
    }

    private void OnNavigateBack(object? sender, RoutedEventArgs e) => NavigateHistory(_backHistory, _forwardHistory);

    private void OnNavigateForward(object? sender, RoutedEventArgs e) => NavigateHistory(_forwardHistory, _backHistory);

    private void NavigateHistory(Stack<NavigationSnapshot> source, Stack<NavigationSnapshot> destination)
    {
        if (source.Count == 0)
        {
            return;
        }

        destination.Push(CaptureNavigationSnapshot());
        var snapshot = source.Pop();
        RestoreNavigationSnapshot(snapshot);
        UpdateNavigationButtons();
    }

    private void RestoreNavigationSnapshot(NavigationSnapshot snapshot)
    {
        _restoringNavigation = true;
        try
        {
            if (snapshot.IsEditor)
            {
                var project = snapshot.ProjectId is null ? null : _vm.Projects.FirstOrDefault(item => item.Id == snapshot.ProjectId);
                var note = snapshot.NoteId is null ? null : _vm.Notes.FirstOrDefault(item => item.Id == snapshot.NoteId);
                if (project is not null)
                {
                    _vm.SelectProject(project, openEditor: true, focusGraph: false);
                    ShowEditorPage(recordHistory: false);
                }
                else if (note is not null)
                {
                    _vm.SelectNote(note, openEditor: true, focusGraph: false);
                    ShowEditorPage(recordHistory: false);
                }
                else
                {
                    ShowCenter(snapshot.CenterView, recordHistory: false);
                }
            }
            else
            {
                ShowCenter(snapshot.CenterView, recordHistory: false);
            }

            _graphControl.RestoreState(snapshot.GraphState);
        }
        finally
        {
            _restoringNavigation = false;
            _navigationCapturedForPendingEditor = false;
        }
    }

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = _backHistory.Count > 0;
        ForwardButton.IsEnabled = _forwardHistory.Count > 0;
    }

    private void ApplyFocusMode(bool enabled)
    {
        _focusModeActive = enabled;
        ApplyShellVisibility();
    }

    private void OnToggleSidebar(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        SetSidebarVisibility(!_sidebarVisiblePreference);
    }

    private void SetSidebarVisibility(bool visible)
    {
        _sidebarVisiblePreference = visible;
        _vm.SaveSidebarVisibility(visible);
        _vm.StatusText = visible ? "Pokazano panel nawigacji" : "Ukryto panel nawigacji";
        ApplyShellVisibility();
    }

    private void OnToggleEditorDetails(object? sender, RoutedEventArgs e)
    {
        if (_editorPageActive && _vm?.SelectedNote is not null)
        {
            _editorControl.ToggleMetadataPanel();
            return;
        }

        if (_vm is not null)
        {
            _vm.SaveEditorDetailsVisibility(!_vm.State.EditorDetailsVisible);
            _vm.StatusText = _vm.State.EditorDetailsVisible
                ? "Panel szczegółów będzie widoczny w edytorze"
                : "Panel szczegółów będzie ukryty w edytorze";
        }
    }

    private void ApplyShellVisibility()
    {
        SidebarPanel.IsVisible = _sidebarVisiblePreference && !_focusModeActive;
        WorkspaceNav.IsVisible = !_focusModeActive;
        MainMenu.IsVisible = !_focusModeActive;
        StatusBar.IsVisible = !_focusModeActive;
        SidebarToggleButton.Content = SidebarPanel.IsVisible ? "Ukryj panel" : "Pokaż panel";
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
        if (!_sidebarVisiblePreference)
        {
            SetSidebarVisibility(true);
        }

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
        for (var attempt = 0; attempt < 3; attempt++)
        {
            _panelIndex = (_panelIndex + step + 3) % 3;
            if (_panelIndex == 0 && !SidebarPanel.IsVisible)
            {
                continue;
            }

            if (_panelIndex == 2 && !_vm.IsEditorOpen)
            {
                continue;
            }

            break;
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
                else if (_vm.CenterView == CenterViewKind.Tasks)
                {
                    _tasksControl.Focus();
                }
                else
                {
                    _peopleControl.Focus();
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
        panel.SetShortcuts(BuildShortcutHelp());
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
                    BuildShortcutsDialog()
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
        new() { Name = "Nowa notatka", Shortcut = PlatformKeys.Chord("N"), Run = CreateNote,
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.N) },
        new() { Name = "Szablon: spotkanie", Shortcut = "", Run = () => CreateNoteFromTemplate("meeting") },
        new() { Name = "Szablon: decyzja", Shortcut = "", Run = () => CreateNoteFromTemplate("decision") },
        new() { Name = "Szablon: plan projektu", Shortcut = "", Run = () => CreateNoteFromTemplate("project-brief") },
        new() { Name = "Szablon: procedura", Shortcut = "", Run = () => CreateNoteFromTemplate("procedure") },
        new() { Name = "Szablon: notatka dzienna", Shortcut = "", Run = () => CreateNoteFromTemplate("daily") },
        new() { Name = "Nowy system", Shortcut = "", Run = () => CreateProject(ProjectItemType.System) },
        new() { Name = "Nowy produkt", Shortcut = "", Run = () => CreateProject(ProjectItemType.Product) },
        new() { Name = "Nowy podsystem", Shortcut = "", Run = () => CreateProject(ProjectItemType.Subsystem) },
        new() { Name = "Nowy komponent", Shortcut = "", Run = () => CreateProject(ProjectItemType.Component) },
        new() { Name = "Nowy projekt ogólny", Shortcut = PlatformKeys.ChordShift("N"), Run = CreateProject,
            MatchesShortcut = e => MatchesCommand(e, shift: true, Key.N) },
        new() { Name = "Nowy folder", Shortcut = "", Run = () => CreateProject(ProjectItemType.Folder) },
        new() { Name = "Nowa osoba", Shortcut = "", Run = () => OnNewPerson(null, new RoutedEventArgs()) },
        new() { Name = "Zapisz", Shortcut = PlatformKeys.Chord("S"), Run = _vm.SaveNow,
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.S) },
        new() { Name = "Znajdź w dokumencie", Shortcut = PlatformKeys.Chord("F"), Run = FindInDocument,
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.F) },
        new() { Name = "Szukaj w całej bibliotece", Shortcut = PlatformKeys.ChordShift("F"), Run = FocusSearch,
            MatchesShortcut = e => MatchesCommand(e, shift: true, Key.F) },
        new() { Name = "Cofnij", Shortcut = PlatformKeys.Chord("Z"), Run = _vm.Undo,
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.Z), AllowWhenTyping = false },
        new() { Name = "Ponów", Shortcut = PlatformKeys.RedoLabel, Run = _vm.Redo,
            MatchesShortcut = MatchesRedo, AllowWhenTyping = false },
        new() { Name = "Widok grafu", Shortcut = PlatformKeys.Chord("1"), Run = () => ShowCenter(CenterViewKind.Graph),
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.D1) },
        new() { Name = "Lista notatek", Shortcut = PlatformKeys.Chord("2"), Run = () => ShowCenter(CenterViewKind.Notes),
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.D2) },
        new() { Name = "Otwarte zadania", Shortcut = PlatformKeys.Chord("3"), Run = () => ShowCenter(CenterViewKind.Tasks),
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.D3) },
        new() { Name = "Osoby", Shortcut = PlatformKeys.Chord("4"), Run = () => ShowCenter(CenterViewKind.People),
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.D4) },
        new() { Name = "Pokaż całą strukturę", Shortcut = "", Run = _vm.ShowAllProjects },
        new() { Name = "Kosz", Shortcut = "", Run = () => _ = ShowTrashAsync() },
        new() { Name = "Przypnij / odepnij", Shortcut = "", Run = _vm.TogglePinSelected },
        new() { Name = "Wyśrodkuj zaznaczenie", Shortcut = "", Run = () => OnCenterSelection(null, new RoutedEventArgs()) },
        new() { Name = "Powiększ graf", Shortcut = PlatformKeys.Chord("+"), Run = () => ChangeZoom(0.1),
            MatchesShortcut = MatchesZoomIn },
        new() { Name = "Pomniejsz graf", Shortcut = PlatformKeys.Chord("-"), Run = () => ChangeZoom(-0.1),
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.OemMinus, Key.Subtract) },
        new() { Name = "Domyślne powiększenie", Shortcut = PlatformKeys.Chord("0"), Run = () => OnZoomReset(this, new RoutedEventArgs()),
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.D0) },
        new() { Name = "Zamknij dokument", Shortcut = PlatformKeys.Chord("W"), Run = CloseDocument,
            MatchesShortcut = e => MatchesCommand(e, shift: false, Key.W) },
        new() { Name = "Lista poleceń", Shortcut = PlatformKeys.ChordShift("P"), Run = () => _ = ShowCommandsAsync(),
            MatchesShortcut = e => MatchesCommand(e, shift: true, Key.P) },
        new() { Name = "Ustawienia", Shortcut = PlatformKeys.Chord(","), Run = () => _ = ShowSettingsAsync(),
            MatchesShortcut = MatchesSettings },
        new() { Name = "Skróty klawiszowe", Shortcut = PlatformKeys.Chord("/"), Run = () => _ = ShowShortcutsAsync(),
            MatchesShortcut = MatchesShortcutHelp },
        new() { Name = "Eksportuj kopię", Shortcut = "", Run = () => _ = ExportBackupAsync() },
        new() { Name = "Eksportuj zaznaczony system", Shortcut = "", Run = () => _ = ExportSystemAsync() },
        new() { Name = "Otwórz propozycję scalenia", Shortcut = "", Run = () => _ = OpenMergeProposalAsync() },
        new() { Name = "Historia scaleń", Shortcut = "", Run = () => _ = ShowMergeHistoryAsync() },
        new() { Name = "Usuń element struktury…", Shortcut = "", Run = () => OnDeleteProject(null, new RoutedEventArgs()) },
        new() { Name = "Przenieś notatkę do kosza", Shortcut = PlatformKeys.TrashLabel, Run = _vm.TrashSelectedNote }
    ];

    private ShortcutsDialog BuildShortcutsDialog()
    {
        var dialog = new ShortcutsDialog();
        dialog.SetShortcuts(BuildShortcutHelp());
        return dialog;
    }

    private IReadOnlyList<ShortcutInfo> BuildShortcutHelp() =>
        BuildCommands()
            .Where(command => command.MatchesShortcut is not null && !string.IsNullOrWhiteSpace(command.Shortcut))
            .Select(command => new ShortcutInfo { Keys = command.Shortcut, Action = command.Name })
            .Concat(ShortcutCatalog.Contextual)
            .ToList();

    private bool TryRunGlobalCommand(KeyEventArgs e, bool textInput)
    {
        if (textInput && PlatformKeys.IsTextComposition(e, _rightAltHeld))
        {
            return false;
        }

        var command = BuildCommands().FirstOrDefault(candidate =>
            candidate.MatchesShortcut?.Invoke(e) == true && (candidate.AllowWhenTyping || !textInput));
        if (command is null)
        {
            return false;
        }

        command.Run();
        e.Handled = true;
        return true;
    }

    private static bool MatchesCommand(KeyEventArgs e, bool shift, params Key[] keys) =>
        PlatformKeys.IsExactCommand(e.KeyModifiers, shift) &&
        keys.Contains(e.Key);

    private static bool MatchesRedo(KeyEventArgs e) =>
        PlatformKeys.IsMac
            ? MatchesCommand(e, shift: true, Key.Z)
            : MatchesCommand(e, shift: false, Key.Y);

    private static bool MatchesZoomIn(KeyEventArgs e) =>
        (PlatformKeys.IsExactCommand(e.KeyModifiers, shift: false) ||
         PlatformKeys.IsExactCommand(e.KeyModifiers, shift: true)) &&
        e.Key is Key.OemPlus or Key.Add;

    private static bool MatchesSettings(KeyEventArgs e) =>
        PlatformKeys.IsExactCommand(e.KeyModifiers, shift: false) &&
        e.Key == Key.OemComma;

    private static bool MatchesShortcutHelp(KeyEventArgs e) =>
        (PlatformKeys.IsExactCommand(e.KeyModifiers, shift: false) ||
         PlatformKeys.IsExactCommand(e.KeyModifiers, shift: true)) &&
        (e.Key is Key.OemQuestion or Key.Oem2 ||
         (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.D7));

    private void CloseDocument()
    {
        _editorControl.ExitFocusMode();
        _vm.CloseEditor();
        ShowCenter(CenterViewKind.Notes);
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.RightAlt || e.PhysicalKey == PhysicalKey.AltRight)
        {
            _rightAltHeld = true;
        }

        if (_vm is null)
        {
            return;
        }

        var mod = PlatformKeys.IsCommand(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var textInput = IsTextInputFocused();
        var graphOrList = IsGraphOrListFocused();

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Left)
        {
            NavigateHistory(_backHistory, _forwardHistory);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Right)
        {
            NavigateHistory(_forwardHistory, _backHistory);
            e.Handled = true;
            return;
        }

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

        if (TryRunGlobalCommand(e, textInput))
        {
            return;
        }

        if (textInput && IsReservedEditorKey(e.Key, mod))
        {
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

    private void OnRootKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.RightAlt || e.PhysicalKey == PhysicalKey.AltRight)
        {
            _rightAltHeld = false;
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
                ReferenceEquals(focused, LibraryTree))
            {
                return true;
            }

            focused = focused.GetVisualParent();
        }

        return false;
    }
}

internal sealed record NavigationSnapshot(
    bool IsEditor,
    CenterViewKind CenterView,
    string? ProjectId,
    string? NoteId,
    GraphViewState GraphState)
{
    public bool SameDestination(NavigationSnapshot other) =>
        IsEditor == other.IsEditor &&
        CenterView == other.CenterView &&
        string.Equals(ProjectId, other.ProjectId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(NoteId, other.NoteId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(GraphState.FocusedProjectId, other.GraphState.FocusedProjectId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(GraphState.SelectedId, other.GraphState.SelectedId, StringComparison.OrdinalIgnoreCase);
}
