using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MapaNotatek.Models;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;
using System.Text;

namespace MapaNotatek.Views;

public partial class EditorPanel : UserControl
{
    private bool _suppress;
    private bool _metadataVisible = true;
    private bool _focusMode;
    private string? _findNoteId;
    private string? _visualNoteId;
    private string _visualBodySnapshot = string.Empty;
    private readonly List<DocumentBlock> _visualBlocks = [];
    private readonly Dictionary<string, TextBox> _visualEditors = new(StringComparer.Ordinal);
    private TextBox? _activeVisualEditor;
    private DocumentBlock? _activeVisualBlock;
    private RememberedSelection? _rememberedSelection;
    private string _editorFontName = "Inter";
    private double _editorBodyFontSize = 16;
    private bool _appearanceLoaded;
    private bool? _projectCompactLayout;

    public EditorPanel()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel { get; set; }

    public event Action<Project>? DeleteProjectRequested;

    public event Action? NewProjectNoteRequested;

    public event Action<bool>? FocusModeChanged;

    public bool IsFocusModeEnabled => _focusMode;

    public void Refresh()
    {
        if (ViewModel is null)
        {
            return;
        }

        _metadataVisible = ViewModel.State.EditorDetailsVisible;
        _suppress = true;
        EmptyPanel.IsVisible = false;
        ProjectPanel.IsVisible = false;
        NotePanel.IsVisible = false;

        if (!ViewModel.IsEditorOpen)
        {
            EmptyPanel.IsVisible = true;
            _suppress = false;
            return;
        }

        if (ViewModel.SelectedProject is { } project)
        {
            ProjectPanel.IsVisible = true;
            ProjectKindLabel.Text = project.ItemType.Label().ToUpperInvariant();
            ProjectWorkspaceTitleText.Text = project.ItemType.Label() + " · szczegóły";
            ProjectHeaderContextText.Text = project.ItemType.ContextDescription();
            ProjectOverviewLabel.Text = project.ItemType switch
            {
                ProjectItemType.System => "O SYSTEMIE",
                ProjectItemType.Product => "O PRODUKCIE",
                ProjectItemType.Subsystem => "O PODSYSTEMIE",
                ProjectItemType.Component => "O KOMPONENCIE",
                ProjectItemType.Folder => "O FOLDERZE",
                _ => "O PROJEKCIE OGÓLNYM"
            };
            ProjectNameBox.Text = project.Name;
            ProjectDescriptionBox.Text = project.Description;
            ProjectPeopleBox.Text = PersonTagService.Format(project.People);
            ProjectMetaText.Text = $"Utworzono {project.Created:g}  •  Zmieniono {project.Modified:g}";
            ProjectChecklistSection.IsVisible = !project.IsFolder;
            ProjectTasksSection.IsVisible = !project.IsFolder;
            if (!project.IsFolder)
            {
                Action checklistChanged = () =>
                {
                    ViewModel.ScheduleSaveProject(project);
                    UpdateProjectChecklistSummary(project);
                };
                FillChecklist(
                    ProjectChecklistHost,
                    project.Checklist,
                    checklistChanged,
                    () => AddChecklistItem(project.Checklist, checklistChanged, ProjectChecklistHost));
                UpdateProjectChecklistSummary(project);
            }

            var relatedNoteTitles = ViewModel.RelatedNotes.Select(n => n.Title).ToList();
            RelatedNotesList.ItemsSource = relatedNoteTitles;
            ProjectNotesCountText.Text = FormatPolishCount(relatedNoteTitles.Count, "notatka", "notatki", "notatek");
            RelatedNotesList.IsVisible = relatedNoteTitles.Count > 0;
            ProjectNotesHintText.IsVisible = relatedNoteTitles.Count > 0;
            ProjectNotesEmptyText.IsVisible = relatedNoteTitles.Count == 0;

            ProjectOpenTasksHost.Children.Clear();
            foreach (var task in ViewModel.ProjectNoteTasks)
            {
                ProjectOpenTasksHost.Children.Add(CreateOpenTaskRow(task));
            }

            var taskCount = ViewModel.ProjectNoteTasks.Count;
            ProjectTasksCountText.Text = FormatPolishCount(taskCount, "zadanie", "zadania", "zadań");
            ProjectTasksEmptyText.IsVisible = taskCount == 0;
        }
        else if (ViewModel.SelectedNote is { } note)
        {
            NotePanel.IsVisible = true;
            LoadEditorAppearance();
            if (!string.Equals(_findNoteId, note.Id, StringComparison.Ordinal))
            {
                FindPanel.IsVisible = false;
                FindTextBox.Text = string.Empty;
                ReplaceTextBox.Text = string.Empty;
                _findNoteId = note.Id;
            }

            NoteTitleBox.Text = note.Title;
            NoteBodyBox.Text = note.Body;
            LoadVisualDocument(note, force: !string.Equals(_visualNoteId, note.Id, StringComparison.Ordinal));
            NoteTagsBox.Text = string.Join(", ", note.Tags);
            NotePeopleBox.Text = PersonTagService.Format(note.People);
            NoteDatesText.Text = $"Utworzono: {note.Created:g}\nZmieniono: {note.Modified:g}";
            FillChecklist(
                NoteChecklistHost,
                note.Checklist,
                () => ViewModel.ScheduleSaveNote(note),
                () => AddChecklistItem(note.Checklist, () => ViewModel.ScheduleSaveNote(note), NoteChecklistHost));
            UpdatePreviewAndLinks(note);
            UpdateDocumentCount(note.Body);
            ApplyNoteMode();
            ApplyEditorChrome();
        }
        else
        {
            EmptyPanel.IsVisible = true;
        }

        _suppress = false;
    }

    public Control DefaultFocusTarget()
    {
        if (ProjectPanel.IsVisible)
        {
            return ProjectNameBox;
        }

        if (NotePanel.IsVisible)
        {
            return NoteTitleBox;
        }

        return this;
    }

    public bool IsFindPanelOpen => FindPanel.IsVisible;

    public void ShowFindPanel()
    {
        if (!NotePanel.IsVisible || ViewModel?.SelectedNote is null)
        {
            return;
        }

        EditModeRadio.IsChecked = true;
        FindPanel.IsVisible = true;
        UpdateFindCount();
        Dispatcher.UIThread.Post(() =>
        {
            FindTextBox.Focus();
            FindTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    public void CloseFindPanel()
    {
        FindPanel.IsVisible = false;
        (_activeVisualEditor ?? _visualEditors.Values.FirstOrDefault())?.Focus();
    }

    private void OnProjectChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || ViewModel?.SelectedProject is not { } project)
        {
            return;
        }

        project.Name = ProjectNameBox.Text ?? string.Empty;
        project.Description = ProjectDescriptionBox.Text ?? string.Empty;
        if (ReferenceEquals(sender, ProjectPeopleBox))
        {
            ViewModel.UpdateProjectPeople(project, ProjectPeopleBox.Text);
        }
        else
        {
            ViewModel.ScheduleSaveProject(project);
        }
    }

    private void OnProjectPanelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 820;
        if (_projectCompactLayout == compact)
        {
            return;
        }

        _projectCompactLayout = compact;
        ProjectWorkspaceGrid.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "3*,2*");
        ProjectWorkspaceGrid.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto,Auto,Auto" : "Auto,Auto");
        ProjectWorkspaceGrid.Margin = compact
            ? new Thickness(18, 16, 18, 24)
            : new Thickness(28, 24, 28, 32);

        Grid.SetColumn(ProjectOverviewCard, 0);
        Grid.SetRow(ProjectOverviewCard, 0);
        Grid.SetColumn(ProjectChecklistSection, 0);
        Grid.SetRow(ProjectChecklistSection, 1);

        Grid.SetColumn(ProjectNotesSection, compact ? 0 : 1);
        Grid.SetRow(ProjectNotesSection, compact ? 2 : 0);
        Grid.SetColumn(ProjectTasksSection, compact ? 0 : 1);
        Grid.SetRow(ProjectTasksSection, compact ? 3 : 1);
    }

    private void UpdateProjectChecklistSummary(Project project)
    {
        var openCount = project.Checklist.Count(item => !item.IsDone);
        var totalCount = project.Checklist.Count;
        ProjectChecklistCountText.Text = totalCount == 0
            ? "0 zadań"
            : $"{openCount} otwarte  •  {totalCount} łącznie";
        ProjectChecklistEmptyText.IsVisible = totalCount == 0;
    }

    private static string FormatPolishCount(int count, string singular, string plural, string genitivePlural)
    {
        var lastTwoDigits = count % 100;
        var lastDigit = count % 10;
        var form = count == 1
            ? singular
            : lastTwoDigits is >= 12 and <= 14 || lastDigit is < 2 or > 4
                ? genitivePlural
                : plural;
        return $"{count} {form}";
    }

    private void OnNoteChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        note.Title = NoteTitleBox.Text ?? string.Empty;
        note.Body = NoteBodyBox.Text ?? string.Empty;
        if (ReferenceEquals(sender, NoteBodyBox))
        {
            ForgetStaleSelection(NoteBodyBox);
        }
        note.Tags = FrontMatter.SplitTags(NoteTagsBox.Text);
        if (ReferenceEquals(sender, NotePeopleBox))
        {
            ViewModel.UpdateNotePeople(note, NotePeopleBox.Text);
        }
        else
        {
            ViewModel.ScheduleSaveNote(note);
        }
        UpdatePreviewAndLinks(note);
        UpdateDocumentCount(note.Body);
        EditorSaveStatusText.Text = "Zapisywanie…";
    }

    private void OnNoteModeChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        ApplyNoteMode();
        if (PreviewModeRadio.IsChecked == true && ViewModel?.SelectedNote is { } note)
        {
            UpdatePreviewAndLinks(note);
        }
    }

    private void ApplyNoteMode()
    {
        var preview = PreviewModeRadio.IsChecked == true;
        var visual = !preview;
        if (visual && ViewModel?.SelectedNote is { } note &&
            !string.Equals(_visualBodySnapshot, note.Body, StringComparison.Ordinal))
        {
            LoadVisualDocument(note, force: true);
        }

        VisualEditorScroll.IsVisible = visual;
        NoteBodyBox.IsVisible = false;
        PreviewHost.IsVisible = preview;
        FormatToolbar.IsEnabled = !preview;
    }

    private void LoadEditorAppearance()
    {
        if (ViewModel is null)
        {
            return;
        }

        var font = ViewModel.State.EditorFont is "Inter" or "Szeryfowa" or "Monospace"
            ? ViewModel.State.EditorFont
            : "Inter";
        var size = ViewModel.State.EditorFontSize switch
        {
            < 15 => 14,
            > 17 => 18,
            _ => 16
        };
        if (_appearanceLoaded && string.Equals(_editorFontName, font, StringComparison.Ordinal) &&
            Math.Abs(_editorBodyFontSize - size) < 0.1)
        {
            return;
        }

        _editorFontName = font;
        _editorBodyFontSize = size;
        _appearanceLoaded = true;
        var previousSuppress = _suppress;
        _suppress = true;
        EditorFontPicker.SelectedIndex = font switch
        {
            "Szeryfowa" => 1,
            "Monospace" => 2,
            _ => 0
        };
        EditorFontSizePicker.SelectedIndex = size switch
        {
            <= 14 => 0,
            >= 18 => 2,
            _ => 1
        };
        _suppress = previousSuppress;
        ApplyEditorAppearance(rerender: false, persist: false);
    }

    private void OnEditorFontChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress || NoteTitleBox is null || EditorFontPicker.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _editorFontName = Convert.ToString(item.Content) ?? "Inter";
        ApplyEditorAppearance(rerender: true, persist: true);
    }

    private void OnEditorFontSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress || NoteTitleBox is null || EditorFontSizePicker.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(Convert.ToString(item.Content), out var size))
        {
            return;
        }

        _editorBodyFontSize = Math.Clamp(size, 14, 18);
        ApplyEditorAppearance(rerender: true, persist: true);
    }

    private void ApplyEditorAppearance(bool rerender, bool persist)
    {
        NoteTitleBox.FontFamily = EditorFontFamily;
        NoteBodyBox.FontFamily = EditorFontFamily;
        NoteBodyBox.FontSize = _editorBodyFontSize;
        NoteBodyBox.LineHeight = _editorBodyFontSize + 9;

        if (persist)
        {
            ViewModel?.SaveEditorAppearance(_editorFontName, _editorBodyFontSize);
        }

        if (!rerender || ViewModel?.SelectedNote is not { } note || _visualBlocks.Count == 0)
        {
            return;
        }

        var activeId = _activeVisualBlock?.RuntimeId;
        var caret = _activeVisualEditor?.CaretIndex;
        var previousSuppress = _suppress;
        _suppress = true;
        RenderVisualEditor(note);
        _suppress = previousSuppress;
        if (activeId is not null)
        {
            FocusVisualBlock(activeId, caret);
        }

        UpdatePreviewAndLinks(note);
    }

    private void LoadVisualDocument(Note note, bool force = false)
    {
        if (!force &&
            string.Equals(_visualNoteId, note.Id, StringComparison.Ordinal) &&
            string.Equals(_visualBodySnapshot, note.Body, StringComparison.Ordinal))
        {
            return;
        }

        var previousSuppress = _suppress;
        _suppress = true;
        try
        {
            _visualBlocks.Clear();
            _visualBlocks.AddRange(VisualDocumentService.Parse(note.Body));
            _visualNoteId = note.Id;
            _visualBodySnapshot = note.Body ?? string.Empty;
            RenderVisualEditor(note);
        }
        finally
        {
            _suppress = previousSuppress;
        }
    }

    private void RenderVisualEditor(Note note)
    {
        VisualBlocksHost.Children.Clear();
        _visualEditors.Clear();
        _activeVisualEditor = null;
        _activeVisualBlock = null;
        _rememberedSelection = null;

        var numberedIndex = 0;
        foreach (var block in _visualBlocks)
        {
            if (block.Kind == DocumentBlockKind.Numbered)
            {
                numberedIndex++;
            }
            else
            {
                numberedIndex = 0;
            }

            VisualBlocksHost.Children.Add(CreateVisualBlockControl(note, block, numberedIndex));
        }

        UpdateOutline();
    }

    private Control CreateVisualBlockControl(Note note, DocumentBlock block, int numberedIndex)
    {
        var cell = new Border();
        cell.Classes.Add("EditorCell");
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };

        var header = new Border
        {
            Padding = new Thickness(8, 5),
            Background = new SolidColorBrush(Color.FromArgb(12, 100, 116, 139)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 128, 128, 128))
        };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 7 };
        var menuButton = new Button
        {
            Content = BlockGlyph(block.Kind),
            Width = 29,
            Height = 26,
            Padding = new Thickness(2),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(menuButton, "Zmień typ, przesuń lub usuń blok");
        menuButton.Click += (_, _) => OpenVisualBlockMenu(menuButton, block);
        headerGrid.Children.Add(menuButton);
        var typeLabel = new TextBlock
        {
            Text = BlockLabel(block.Kind),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.58,
            LetterSpacing = 0.55,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(typeLabel, 1);
        headerGrid.Children.Add(typeLabel);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        actions.Children.Add(CellAction("↑", "Przesuń komórkę w górę", () => MoveVisualBlock(block, -1)));
        actions.Children.Add(CellAction("↓", "Przesuń komórkę w dół", () => MoveVisualBlock(block, 1)));
        actions.Children.Add(CellAction("+", "Dodaj komórkę tekstową poniżej", () =>
            InsertVisualBlockAfter(block, VisualDocumentService.NewParagraph())));
        actions.Children.Add(CellAction("⋯", "Więcej działań", () => OpenVisualBlockMenu(actions, block)));
        Grid.SetColumn(actions, 2);
        headerGrid.Children.Add(actions);
        header.Child = headerGrid;
        layout.Children.Add(header);

        Control content = block.Kind switch
        {
            DocumentBlockKind.Table => CreateVisualTable(block),
            DocumentBlockKind.Image => CreateVisualImage(note, block),
            DocumentBlockKind.Rule => new Border
            {
                Height = 1,
                Margin = new Thickness(0, 15),
                Background = new SolidColorBrush(Color.FromArgb(100, 128, 128, 128))
            },
            _ => CreateVisualTextBlock(block, numberedIndex)
        };
        var contentHost = new Border { Padding = new Thickness(13, 9), Child = content };
        Grid.SetRow(contentHost, 1);
        layout.Children.Add(contentHost);
        cell.Child = layout;
        return cell;
    }

    private Control CreateVisualTextBlock(DocumentBlock block, int numberedIndex)
    {
        var bodyFontSize = block.Kind switch
        {
            DocumentBlockKind.Heading1 => _editorBodyFontSize + 14,
            DocumentBlockKind.Heading2 => _editorBodyFontSize + 8,
            DocumentBlockKind.Heading3 => _editorBodyFontSize + 3,
            _ => _editorBodyFontSize
        };
        var editor = new TextBox
        {
            Text = block.Text,
            AcceptsReturn = true,
            AcceptsTab = block.Kind == DocumentBlockKind.Code,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(3, 4),
            MinWidth = 0,
            FontSize = bodyFontSize,
            FontWeight = block.Kind is DocumentBlockKind.Heading1 or DocumentBlockKind.Heading2 or DocumentBlockKind.Heading3
                ? FontWeight.SemiBold
                : FontWeight.Normal,
            FontFamily = block.Kind == DocumentBlockKind.Code
                ? new FontFamily("Cascadia Mono, Consolas, Menlo, monospace")
                : EditorFontFamily,
            LineHeight = block.Kind == DocumentBlockKind.Code ? _editorBodyFontSize + 6 : _editorBodyFontSize + 9,
            PlaceholderText = block.Kind switch
            {
                DocumentBlockKind.Heading1 => "Nagłówek 1",
                DocumentBlockKind.Heading2 => "Nagłówek 2",
                DocumentBlockKind.Heading3 => "Nagłówek 3",
                DocumentBlockKind.Checklist => "Zadanie",
                DocumentBlockKind.Quote => "Cytat",
                DocumentBlockKind.Code => "Kod",
                _ => "Wpisz tekst lub /, aby dodać blok"
            }
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _visualEditors[block.RuntimeId] = editor;
        editor.GotFocus += (_, _) =>
        {
            _activeVisualEditor = editor;
            _activeVisualBlock = block;
            RememberSelection(editor);
        };
        TrackSelection(editor);

        var outputText = CreateInlinePreviewText(block.Text, bodyFontSize, block.Kind);
        var outputHost = new Border
        {
            IsVisible = block.Kind != DocumentBlockKind.Code && HasInlineFormatting(block.Text),
            Margin = new Thickness(3, 7, 3, 1),
            Padding = new Thickness(11, 8),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(18, 40, 103, 214)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 40, 103, 214)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = "PODGLĄD FORMATOWANIA",
                        FontSize = 9,
                        FontWeight = FontWeight.SemiBold,
                        Opacity = 0.5,
                        LetterSpacing = 0.5
                    },
                    outputText
                }
            }
        };
        editor.TextChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            block.Text = editor.Text ?? string.Empty;
            ForgetStaleSelection(editor);
            RefreshInlinePreview(outputHost, outputText, block.Text, bodyFontSize, block.Kind);
            if (block.Kind == DocumentBlockKind.Paragraph && string.Equals(block.Text, "/", StringComparison.Ordinal))
            {
                var previousSuppress = _suppress;
                _suppress = true;
                editor.Text = string.Empty;
                block.Text = string.Empty;
                _suppress = previousSuppress;
                SyncVisualToNote();
                Dispatcher.UIThread.Post(() => OpenVisualBlockMenu(editor, block), DispatcherPriority.Input);
                return;
            }

            SyncVisualToNote();
        };
        // TextBox consumes Enter for a newline. Intercept it during tunnelling so
        // list/checklist cells can create the next item first.
        editor.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) => OnVisualEditorKeyDown(block, editor, args),
            RoutingStrategies.Tunnel);

        var editorWithOutput = new StackPanel { Spacing = 2 };
        editorWithOutput.Children.Add(editor);
        editorWithOutput.Children.Add(outputHost);
        if (block.Kind == DocumentBlockKind.Checklist)
        {
            var peopleBox = new TextBox
            {
                Text = PersonTagService.Format(block.People),
                PlaceholderText = "Osoby: Anna Kowalska, piotr-nowak",
                FontSize = 11,
                MinHeight = 30,
                Margin = new Thickness(3, 3, 0, 0)
            };
            peopleBox.TextChanged += (_, _) =>
            {
                if (_suppress)
                {
                    return;
                }

                block.People = ViewModel?.ResolvePeopleAssignments(peopleBox.Text) ?? PersonTagService.Parse(peopleBox.Text);
                SyncVisualToNote(updateOutline: false);
                ViewModel?.NotifyPeopleAssignmentsChanged();
            };
            editorWithOutput.Children.Add(peopleBox);
        }
        Control body = editorWithOutput;
        if (block.Kind is DocumentBlockKind.Bullet or DocumentBlockKind.Numbered or DocumentBlockKind.Checklist)
        {
            var prefix = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 7
            };
            Control marker;
            if (block.Kind == DocumentBlockKind.Checklist)
            {
                var check = new CheckBox
                {
                    IsChecked = block.IsChecked,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 7, 0, 0),
                    MinWidth = 24
                };
                check.IsCheckedChanged += (_, _) =>
                {
                    if (_suppress)
                    {
                        return;
                    }

                    block.IsChecked = check.IsChecked == true;
                    SyncVisualToNote();
                };
                marker = check;
            }
            else
            {
                marker = new TextBlock
                {
                    Text = block.Kind == DocumentBlockKind.Bullet ? "•" : $"{Math.Max(1, numberedIndex)}.",
                    FontSize = 16,
                    Margin = new Thickness(3, 7, 0, 0),
                    MinWidth = 20,
                    TextAlignment = TextAlignment.Right
                };
            }

            prefix.Children.Add(marker);
            Grid.SetColumn(editorWithOutput, 1);
            prefix.Children.Add(editorWithOutput);
            body = prefix;
        }

        if (block.Kind == DocumentBlockKind.Quote)
        {
            body = new Border
            {
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 73, 112, 181)),
                Background = new SolidColorBrush(Color.FromArgb(16, 73, 112, 181)),
                Padding = new Thickness(12, 3),
                Child = body
            };
        }
        else if (block.Kind == DocumentBlockKind.Code)
        {
            var languageBox = new TextBox
            {
                Text = block.Language,
                PlaceholderText = "język, np. sql lub csharp",
                Width = 180,
                MinHeight = 28,
                Padding = new Thickness(7, 3),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            languageBox.TextChanged += (_, _) =>
            {
                if (_suppress)
                {
                    return;
                }

                block.Language = (languageBox.Text ?? string.Empty).Trim();
                SyncVisualToNote(updateOutline: false);
            };
            var codeContent = new StackPanel { Spacing = 7 };
            var languageRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            languageRow.Children.Add(new TextBlock
            {
                Text = "JĘZYK",
                FontSize = 9,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            languageRow.Children.Add(languageBox);
            codeContent.Children.Add(languageRow);
            codeContent.Children.Add(body);
            body = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(22, 100, 116, 139)),
                Padding = new Thickness(10, 7),
                Child = codeContent
            };
        }

        return body;
    }

    private Button CellAction(string label, string tooltip, Action action)
    {
        var button = new Button { Content = label };
        button.Classes.Add("CellAction");
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private FontFamily EditorFontFamily => _editorFontName switch
    {
        "Szeryfowa" => new FontFamily("Georgia, Times New Roman, serif"),
        "Monospace" => new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
        _ => FontFamily.Default
    };

    private TextBlock CreateInlinePreviewText(string text, double fontSize, DocumentBlockKind kind)
    {
        var preview = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = kind == DocumentBlockKind.Code
                ? new FontFamily("Cascadia Mono, Consolas, Menlo, monospace")
                : EditorFontFamily,
            FontSize = fontSize,
            FontWeight = kind is DocumentBlockKind.Heading1 or DocumentBlockKind.Heading2 or DocumentBlockKind.Heading3
                ? FontWeight.SemiBold
                : FontWeight.Normal,
            LineHeight = fontSize + 8
        };
        PopulateInlinePreview(preview, text);
        return preview;
    }

    private static void PopulateInlinePreview(TextBlock preview, string text)
    {
        preview.Inlines!.Clear();
        foreach (var (spanText, bold, italic, code, strike, wiki) in WikiLinkService.ParseInlineSpans(text))
        {
            var run = new Run(spanText);
            if (bold || wiki)
            {
                run.FontWeight = FontWeight.Bold;
            }

            if (italic)
            {
                run.FontStyle = FontStyle.Italic;
            }

            if (code)
            {
                run.FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace");
                run.Background = new SolidColorBrush(Color.FromArgb(24, 100, 116, 139));
            }

            if (wiki)
            {
                run.Foreground = new SolidColorBrush(Color.FromRgb(40, 103, 214));
            }

            if (strike)
            {
                run.TextDecorations = TextDecorations.Strikethrough;
            }

            preview.Inlines.Add(run);
        }
    }

    private static bool HasInlineFormatting(string text) =>
        WikiLinkService.ParseInlineSpans(text).Any(span =>
            span.Bold || span.Italic || span.Code || span.Strike || span.Wiki);

    private void RefreshInlinePreview(
        Border host,
        TextBlock preview,
        string text,
        double fontSize,
        DocumentBlockKind kind)
    {
        host.IsVisible = kind != DocumentBlockKind.Code && HasInlineFormatting(text);
        preview.FontFamily = EditorFontFamily;
        preview.FontSize = fontSize;
        PopulateInlinePreview(preview, text);
    }

    private Control CreateVisualTable(DocumentBlock block)
    {
        VisualDocumentService.NormalizeTable(block);
        var panel = new StackPanel { Spacing = 7, MinWidth = 0 };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        actions.Children.Add(TableAction("+ wiersz", () =>
        {
            block.Cells.Add(Enumerable.Repeat(string.Empty, block.Cells[0].Count).ToList());
            RenderAndSyncVisualDocument(block.RuntimeId);
        }));
        actions.Children.Add(TableAction("− wiersz", () =>
        {
            if (block.Cells.Count > 1)
            {
                block.Cells.RemoveAt(block.Cells.Count - 1);
                RenderAndSyncVisualDocument(block.RuntimeId);
            }
        }));
        actions.Children.Add(TableAction("+ kolumna", () =>
        {
            if (block.Cells[0].Count < 20)
            {
                foreach (var cells in block.Cells)
                {
                    cells.Add(string.Empty);
                }

                block.Cells[0][^1] = $"Kolumna {block.Cells[0].Count}";
                RenderAndSyncVisualDocument(block.RuntimeId);
            }
        }));
        actions.Children.Add(TableAction("− kolumna", () =>
        {
            if (block.Cells[0].Count > 1)
            {
                foreach (var cells in block.Cells)
                {
                    cells.RemoveAt(cells.Count - 1);
                }

                RenderAndSyncVisualDocument(block.RuntimeId);
            }
        }));
        panel.Children.Add(actions);

        var grid = new Grid();
        var columns = block.Cells[0].Count;
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        }

        for (var rowIndex = 0; rowIndex < block.Cells.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                var capturedRow = rowIndex;
                var capturedColumn = columnIndex;
                var box = new TextBox
                {
                    Text = block.Cells[rowIndex][columnIndex],
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinWidth = 120,
                    MinHeight = 38,
                    Padding = new Thickness(8, 6),
                    BorderThickness = new Thickness(0.5),
                    FontFamily = EditorFontFamily,
                    FontSize = _editorBodyFontSize - 1,
                    FontWeight = rowIndex == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                    Background = rowIndex == 0
                        ? new SolidColorBrush(Color.FromArgb(22, 100, 116, 139))
                        : Brushes.Transparent
                };
                box.GotFocus += (_, _) =>
                {
                    _activeVisualEditor = box;
                    _activeVisualBlock = block;
                    RememberSelection(box);
                };
                TrackSelection(box);
                box.TextChanged += (_, _) =>
                {
                    if (_suppress)
                    {
                        return;
                    }

                    block.Cells[capturedRow][capturedColumn] = box.Text ?? string.Empty;
                    ForgetStaleSelection(box);
                    SyncVisualToNote(updateOutline: false);
                };
                Grid.SetRow(box, rowIndex);
                Grid.SetColumn(box, columnIndex);
                grid.Children.Add(box);
            }
        }

        panel.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grid
        });
        return panel;
    }

    private static Button TableAction(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(7, 3), FontSize = 11 };
        button.Click += (_, _) => action();
        return button;
    }

    private Control CreateVisualImage(Note note, DocumentBlock block)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 7) };
        var preview = TryCreateImagePreview(note, $"![{block.Text}]({block.ImagePath})");
        panel.Children.Add(preview ?? new Border
        {
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
            Child = new TextBlock
            {
                Text = "Nie można wyświetlić lokalnego obrazu: " + block.ImagePath,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.68
            }
        });
        var caption = new TextBox
        {
            Text = block.Text,
            PlaceholderText = "Podpis / tekst alternatywny",
            FontStyle = FontStyle.Italic,
            FontFamily = EditorFontFamily,
            FontSize = Math.Max(12, _editorBodyFontSize - 3),
            TextWrapping = TextWrapping.Wrap
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(caption, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        caption.GotFocus += (_, _) =>
        {
            _activeVisualEditor = caption;
            _activeVisualBlock = block;
            RememberSelection(caption);
        };
        TrackSelection(caption);
        caption.TextChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            block.Text = caption.Text ?? string.Empty;
            ForgetStaleSelection(caption);
            SyncVisualToNote();
        };
        panel.Children.Add(caption);
        return panel;
    }

    private void OnVisualEditorKeyDown(DocumentBlock block, TextBox editor, KeyEventArgs e)
    {
        if (PlatformKeys.IsCommand(e.KeyModifiers) && e.Key == Key.B)
        {
            ApplyInlineFormattingToEditor(editor, "**", "**", "pogrubienie");
            e.Handled = true;
            return;
        }

        if (PlatformKeys.IsCommand(e.KeyModifiers) && e.Key == Key.I)
        {
            ApplyInlineFormattingToEditor(editor, "*", "*", "kursywę");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && block.Kind != DocumentBlockKind.Code && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SplitVisualBlock(block, editor);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Back || editor.CaretIndex != 0)
        {
            return;
        }

        var index = _visualBlocks.IndexOf(block);
        if (block.Kind != DocumentBlockKind.Paragraph)
        {
            block.Kind = DocumentBlockKind.Paragraph;
            RenderAndSyncVisualDocument(block.RuntimeId, 0);
            e.Handled = true;
            return;
        }

        if (index <= 0)
        {
            return;
        }

        var previous = _visualBlocks[index - 1];
        if (previous.Kind is DocumentBlockKind.Table or DocumentBlockKind.Image or DocumentBlockKind.Rule)
        {
            return;
        }

        var previousLength = previous.Text.Length;
        previous.Text += block.Text;
        _visualBlocks.RemoveAt(index);
        RenderAndSyncVisualDocument(previous.RuntimeId, previousLength);
        e.Handled = true;
    }

    private void SplitVisualBlock(DocumentBlock block, TextBox editor)
    {
        var text = editor.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        var edit = ListEditingService.SplitBlock(block, text, start, end);
        block.Kind = edit.CurrentKind;
        block.Text = edit.CurrentText;
        block.IsChecked = edit.CurrentIsChecked;
        if (edit.FollowingBlock is null)
        {
            RenderAndSyncVisualDocument(block.RuntimeId, 0);
            return;
        }

        var index = _visualBlocks.IndexOf(block);
        _visualBlocks.Insert(index + 1, edit.FollowingBlock);
        RenderAndSyncVisualDocument(edit.FollowingBlock.RuntimeId, 0);
    }

    private void OpenVisualBlockMenu(Control anchor, DocumentBlock block)
    {
        var menu = new ContextMenu();
        AddBlockTypeItem(menu, "Akapit", block, DocumentBlockKind.Paragraph);
        AddBlockTypeItem(menu, "Nagłówek 1", block, DocumentBlockKind.Heading1);
        AddBlockTypeItem(menu, "Nagłówek 2", block, DocumentBlockKind.Heading2);
        AddBlockTypeItem(menu, "Nagłówek 3", block, DocumentBlockKind.Heading3);
        AddBlockTypeItem(menu, "Lista punktowana", block, DocumentBlockKind.Bullet);
        AddBlockTypeItem(menu, "Lista numerowana", block, DocumentBlockKind.Numbered);
        AddBlockTypeItem(menu, "Zadanie", block, DocumentBlockKind.Checklist);
        AddBlockTypeItem(menu, "Cytat", block, DocumentBlockKind.Quote);
        AddBlockTypeItem(menu, "Blok kodu", block, DocumentBlockKind.Code);
        menu.Items.Add(new Separator());
        menu.Items.Add(BlockMenuAction("Wstaw akapit poniżej", () => InsertVisualBlockAfter(block, VisualDocumentService.NewParagraph())));
        menu.Items.Add(BlockMenuAction("Wstaw tabelę poniżej", () => InsertVisualBlockAfter(block, VisualDocumentService.NewTable())));
        menu.Items.Add(BlockMenuAction("Wstaw separator poniżej", () => InsertVisualBlockAfter(block, new DocumentBlock { Kind = DocumentBlockKind.Rule })));
        menu.Items.Add(new Separator());
        menu.Items.Add(BlockMenuAction("Przesuń w górę", () => MoveVisualBlock(block, -1)));
        menu.Items.Add(BlockMenuAction("Przesuń w dół", () => MoveVisualBlock(block, 1)));
        menu.Items.Add(BlockMenuAction("Usuń blok", () => RemoveVisualBlock(block)));
        anchor.ContextMenu = menu;
        Dispatcher.UIThread.Post(() => menu.Open(anchor), DispatcherPriority.Input);
    }

    private void AddBlockTypeItem(ContextMenu menu, string label, DocumentBlock block, DocumentBlockKind kind) =>
        menu.Items.Add(BlockMenuAction(label, () => ChangeVisualBlockKind(block, kind)));

    private static MenuItem BlockMenuAction(string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => action();
        return item;
    }

    private void ChangeVisualBlockKind(DocumentBlock block, DocumentBlockKind kind)
    {
        if (block.Kind == DocumentBlockKind.Table && kind != DocumentBlockKind.Table)
        {
            block.Text = VisualDocumentService.Serialize([block]);
        }
        else if (block.Kind == DocumentBlockKind.Image && kind != DocumentBlockKind.Image)
        {
            block.Text = string.IsNullOrWhiteSpace(block.Text)
                ? block.ImagePath
                : $"{block.Text} ({block.ImagePath})";
        }

        block.Kind = kind;
        if (kind == DocumentBlockKind.Table && block.Cells.Count == 0)
        {
            var previousText = block.Text;
            block.Cells = VisualDocumentService.NewTable().Cells;
            if (!string.IsNullOrWhiteSpace(previousText) && block.Cells.Count > 1)
            {
                block.Cells[1][0] = previousText;
            }
        }

        RenderAndSyncVisualDocument(block.RuntimeId);
    }

    private void InsertVisualBlockAfter(DocumentBlock anchor, DocumentBlock block)
    {
        var index = _visualBlocks.IndexOf(anchor);
        _visualBlocks.Insert(index < 0 ? _visualBlocks.Count : index + 1, block);
        RenderAndSyncVisualDocument(block.RuntimeId, 0);
    }

    private void MoveVisualBlock(DocumentBlock block, int offset)
    {
        var current = _visualBlocks.IndexOf(block);
        var target = Math.Clamp(current + offset, 0, _visualBlocks.Count - 1);
        if (current < 0 || current == target)
        {
            return;
        }

        _visualBlocks.RemoveAt(current);
        _visualBlocks.Insert(target, block);
        RenderAndSyncVisualDocument(block.RuntimeId);
    }

    private void RemoveVisualBlock(DocumentBlock block)
    {
        var index = _visualBlocks.IndexOf(block);
        if (index < 0)
        {
            return;
        }

        _visualBlocks.RemoveAt(index);
        if (_visualBlocks.Count == 0)
        {
            _visualBlocks.Add(VisualDocumentService.NewParagraph());
        }

        var focus = _visualBlocks[Math.Clamp(index, 0, _visualBlocks.Count - 1)];
        RenderAndSyncVisualDocument(focus.RuntimeId);
    }

    private void RenderAndSyncVisualDocument(string? focusBlockId = null, int? caret = null)
    {
        if (ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        var previousSuppress = _suppress;
        _suppress = true;
        RenderVisualEditor(note);
        _suppress = previousSuppress;
        SyncVisualToNote();
        if (focusBlockId is not null)
        {
            FocusVisualBlock(focusBlockId, caret);
        }
    }

    private void FocusVisualBlock(string blockId, int? caret = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_visualEditors.TryGetValue(blockId, out var editor))
            {
                return;
            }

            editor.Focus();
            if (caret is not null)
            {
                editor.CaretIndex = Math.Clamp(caret.Value, 0, (editor.Text ?? string.Empty).Length);
            }

            editor.BringIntoView();
        }, DispatcherPriority.Input);
    }

    private void SyncVisualToNote(bool updateOutline = true)
    {
        if (_suppress || ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        var body = VisualDocumentService.Serialize(_visualBlocks);
        _visualBodySnapshot = body;
        note.Body = body;
        note.Checklist = FrontMatter.Parse(body).Checklist;
        var previousSuppress = _suppress;
        _suppress = true;
        NoteBodyBox.Text = body;
        _suppress = previousSuppress;
        ViewModel.ScheduleSaveNote(note);
        UpdatePreviewAndLinks(note);
        UpdateDocumentCount(body);
        if (updateOutline)
        {
            UpdateOutline();
        }

        EditorSaveStatusText.Text = "Zapisywanie…";
    }

    private void UpdateOutline()
    {
        OutlineList.ItemsSource = VisualDocumentService.BuildOutline(_visualBlocks)
            .Select(entry => new OutlineListItem(entry.BlockId, entry.Title, entry.Level))
            .ToList();
    }

    private static string BlockGlyph(DocumentBlockKind kind) => kind switch
    {
        DocumentBlockKind.Heading1 => "H1",
        DocumentBlockKind.Heading2 => "H2",
        DocumentBlockKind.Heading3 => "H3",
        DocumentBlockKind.Bullet => "•",
        DocumentBlockKind.Numbered => "1.",
        DocumentBlockKind.Checklist => "☐",
        DocumentBlockKind.Quote => "❯",
        DocumentBlockKind.Code => "{}",
        DocumentBlockKind.Rule => "—",
        DocumentBlockKind.Image => "IMG",
        DocumentBlockKind.Table => "TBL",
        _ => "¶"
    };

    private static string BlockLabel(DocumentBlockKind kind) => kind switch
    {
        DocumentBlockKind.Heading1 => "NAGŁÓWEK 1",
        DocumentBlockKind.Heading2 => "NAGŁÓWEK 2",
        DocumentBlockKind.Heading3 => "NAGŁÓWEK 3",
        DocumentBlockKind.Bullet => "LISTA PUNKTOWANA",
        DocumentBlockKind.Numbered => "LISTA NUMEROWANA",
        DocumentBlockKind.Checklist => "ZADANIE",
        DocumentBlockKind.Quote => "CYTAT",
        DocumentBlockKind.Code => "KOD",
        DocumentBlockKind.Rule => "SEPARATOR",
        DocumentBlockKind.Image => "OBRAZ",
        DocumentBlockKind.Table => "TABELA",
        _ => "TEKST"
    };

    private sealed class OutlineListItem(string blockId, string title, int level)
    {
        public string BlockId { get; } = blockId;
        public string DisplayTitle { get; } = new string(' ', Math.Max(0, level - 1) * 3) + title;
        public override string ToString() => DisplayTitle;
    }

    private sealed record RememberedSelection(TextBox Editor, string TextSnapshot, int Start, int End);

    public void UpdateSaveStatus(string? status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            EditorSaveStatusText.Text = status;
        }
    }

    public void ExitFocusMode()
    {
        if (_focusMode)
        {
            SetFocusMode(false);
        }
    }

    private void OnFindTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateFindCount();
        if (!string.IsNullOrEmpty(FindTextBox.Text))
        {
            SelectMatch(backwards: false, restart: true);
        }
    }

    private void OnFindOptionsChanged(object? sender, RoutedEventArgs e)
    {
        UpdateFindCount();
        if (!string.IsNullOrEmpty(FindTextBox.Text))
        {
            SelectMatch(backwards: false, restart: true);
        }
    }

    private void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SelectMatch(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
    }

    private void OnReplaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReplaceCurrent();
            e.Handled = true;
        }
    }

    private void OnFindPrevious(object? sender, RoutedEventArgs e) => SelectMatch(backwards: true);
    private void OnFindNext(object? sender, RoutedEventArgs e) => SelectMatch(backwards: false);
    private void OnReplaceCurrent(object? sender, RoutedEventArgs e) => ReplaceCurrent();
    private void OnReplaceAll(object? sender, RoutedEventArgs e) => ReplaceAll();
    private void OnCloseFind(object? sender, RoutedEventArgs e) => CloseFindPanel();

    private StringComparison FindComparison => FindCaseSensitiveBox.IsChecked == true
        ? StringComparison.CurrentCulture
        : StringComparison.CurrentCultureIgnoreCase;

    private void SelectMatch(bool backwards, bool restart = false)
    {
        var query = FindTextBox.Text ?? string.Empty;
        var body = NoteBodyBox.Text ?? string.Empty;
        if (query.Length == 0 || body.Length == 0)
        {
            UpdateFindCount();
            return;
        }

        int index;
        if (backwards)
        {
            var start = restart ? body.Length - 1 : Math.Min(NoteBodyBox.SelectionStart - 1, body.Length - 1);
            index = start >= 0 ? body.LastIndexOf(query, start, FindComparison) : -1;
            if (index < 0)
            {
                index = body.LastIndexOf(query, FindComparison);
            }
        }
        else
        {
            var start = restart ? 0 : Math.Clamp(NoteBodyBox.SelectionEnd, 0, body.Length);
            index = body.IndexOf(query, start, FindComparison);
            if (index < 0 && start > 0)
            {
                index = body.IndexOf(query, 0, FindComparison);
            }
        }

        if (index >= 0)
        {
            NoteBodyBox.SelectionStart = index;
            NoteBodyBox.SelectionEnd = index + query.Length;
        }

        UpdateFindCount();
    }

    private void ReplaceCurrent()
    {
        var query = FindTextBox.Text ?? string.Empty;
        if (query.Length == 0)
        {
            return;
        }

        var body = NoteBodyBox.Text ?? string.Empty;
        var start = Math.Min(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
        var end = Math.Max(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
        var selectionMatches = end - start == query.Length &&
                               start >= 0 && end <= body.Length &&
                               string.Equals(body[start..end], query, FindComparison);
        if (!selectionMatches)
        {
            SelectMatch(backwards: false);
            start = Math.Min(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
            end = Math.Max(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
            selectionMatches = end - start == query.Length &&
                               start >= 0 && end <= body.Length &&
                               string.Equals(body[start..end], query, FindComparison);
        }

        if (!selectionMatches)
        {
            return;
        }

        var replacement = ReplaceTextBox.Text ?? string.Empty;
        var next = body[..start] + replacement + body[end..];
        _suppress = true;
        NoteBodyBox.Text = next;
        _suppress = false;
        NoteBodyBox.SelectionStart = start;
        NoteBodyBox.SelectionEnd = start + replacement.Length;
        SyncBodyToNote();
        UpdateFindCount();
    }

    private void ReplaceAll()
    {
        var query = FindTextBox.Text ?? string.Empty;
        var body = NoteBodyBox.Text ?? string.Empty;
        if (query.Length == 0 || body.Length == 0)
        {
            return;
        }

        var builder = new StringBuilder(body.Length);
        var offset = 0;
        var replacements = 0;
        while (offset < body.Length)
        {
            var index = body.IndexOf(query, offset, FindComparison);
            if (index < 0)
            {
                builder.Append(body, offset, body.Length - offset);
                break;
            }

            builder.Append(body, offset, index - offset);
            builder.Append(ReplaceTextBox.Text ?? string.Empty);
            offset = index + query.Length;
            replacements++;
        }

        if (replacements == 0)
        {
            return;
        }

        _suppress = true;
        NoteBodyBox.Text = builder.ToString();
        _suppress = false;
        SyncBodyToNote();
        UpdateFindCount();
        EditorSaveStatusText.Text = $"Zamieniono: {replacements}";
    }

    private void UpdateFindCount()
    {
        var query = FindTextBox.Text ?? string.Empty;
        var body = NoteBodyBox.Text ?? string.Empty;
        if (query.Length == 0 || body.Length == 0)
        {
            FindCountText.Text = "0 wyników";
            return;
        }

        var positions = new List<int>();
        var offset = 0;
        while (offset <= body.Length - query.Length)
        {
            var index = body.IndexOf(query, offset, FindComparison);
            if (index < 0)
            {
                break;
            }

            positions.Add(index);
            offset = index + Math.Max(1, query.Length);
        }

        var selected = Math.Min(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
        var current = positions.FindIndex(position => position == selected);
        FindCountText.Text = current >= 0
            ? $"{current + 1} z {positions.Count}"
            : $"{positions.Count} wyników";
    }

    private void OnFormatBold(object? sender, RoutedEventArgs e) => ApplyInlineFormatting("**", "**", "pogrubienie");
    private void OnFormatItalic(object? sender, RoutedEventArgs e) => ApplyInlineFormatting("*", "*", "kursywę");
    private void OnFormatStrike(object? sender, RoutedEventArgs e) => ApplyInlineFormatting("~~", "~~", "przekreślenie");
    private void OnFormatCode(object? sender, RoutedEventArgs e) => ApplyInlineFormatting("`", "`", "kod w tekście");
    private void OnFormatWiki(object? sender, RoutedEventArgs e) => ApplyInlineFormatting("[[", "]]", "wikilink");
    private void OnFormatLink(object? sender, RoutedEventArgs e) => ApplyInlineFormatting("[", "](https://)", "link");
    private void OnFormatH1(object? sender, RoutedEventArgs e) => ApplyBlockKind(DocumentBlockKind.Heading1);
    private void OnFormatH2(object? sender, RoutedEventArgs e) => ApplyBlockKind(DocumentBlockKind.Heading2);
    private void OnFormatH3(object? sender, RoutedEventArgs e) => ApplyBlockKind(DocumentBlockKind.Heading3);
    private void OnFormatBullet(object? sender, RoutedEventArgs e) => ApplyBlockKind(DocumentBlockKind.Bullet);
    private void OnFormatNumbered(object? sender, RoutedEventArgs e) => ApplyBlockKind(DocumentBlockKind.Numbered);
    private void OnFormatCheck(object? sender, RoutedEventArgs e) => ApplyBlockKind(DocumentBlockKind.Checklist);
    private void OnFormatQuote(object? sender, RoutedEventArgs e) => ApplyBlockKind(DocumentBlockKind.Quote);
    private void OnFormatRule(object? sender, RoutedEventArgs e)
        => InsertVisualNearActive(new DocumentBlock { Kind = DocumentBlockKind.Rule });

    private void OnFormatTable(object? sender, RoutedEventArgs e)
        => InsertVisualNearActive(VisualDocumentService.NewTable());

    private async void OnInsertImage(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNote is not { } note ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz obraz do notatki",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Obrazy")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"]
                }
            ]
        });
        if (files.Count == 0)
        {
            return;
        }

        var sourcePath = files[0].Path.LocalPath;
        try
        {
            EditorSaveStatusText.Text = "Kopiowanie obrazu…";
            var store = new AttachmentStore(ViewModel.DataFolder);
            var imported = await Task.Run(() => store.ImportImage(note.Id, sourcePath));
            var alt = Path.GetFileNameWithoutExtension(sourcePath);
            InsertVisualNearActive(new DocumentBlock
            {
                Kind = DocumentBlockKind.Image,
                Text = alt,
                ImagePath = imported.MarkdownPath
            });
            ViewModel.StatusText = $"Dodano obraz: {Path.GetFileName(imported.FullPath)}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = "Nie udało się dodać obrazu: " + ex.Message;
            EditorSaveStatusText.Text = "Błąd dodawania obrazu";
        }
    }

    private void ApplyInlineFormatting(string before, string after, string label)
    {
        var editor = _activeVisualEditor ?? _visualEditors.Values.FirstOrDefault();
        if (editor is not null)
        {
            ApplyInlineFormattingToEditor(editor, before, after, label);
        }
    }

    private void ApplyInlineFormattingToEditor(TextBox editor, string before, string after, string label)
    {
        var text = editor.Text ?? string.Empty;
        var (start, end) = ResolveSelection(editor);
        var edit = InlineFormattingService.Toggle(text, start, end, before, after);
        if (!edit.Changed)
        {
            EditorSaveStatusText.Text = $"Zaznacz tekst, aby zastosować {label}.";
            editor.Focus();
            return;
        }

        editor.Text = edit.Text;
        editor.SelectionStart = edit.SelectionStart;
        editor.SelectionEnd = edit.SelectionEnd;
        RememberSelection(editor);
        editor.Focus();
    }

    private void RememberSelection(TextBox editor)
    {
        var text = editor.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        if (end > start)
        {
            _rememberedSelection = new RememberedSelection(editor, text, start, end);
        }
    }

    private void TrackSelection(TextBox editor)
    {
        editor.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.SelectionStartProperty ||
                args.Property == TextBox.SelectionEndProperty)
            {
                RememberSelection(editor);
            }
        };
    }

    private (int Start, int End) ResolveSelection(TextBox editor)
    {
        var text = editor.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        if (end > start)
        {
            return (start, end);
        }

        if (_rememberedSelection is { } remembered &&
            ReferenceEquals(remembered.Editor, editor) &&
            string.Equals(remembered.TextSnapshot, text, StringComparison.Ordinal))
        {
            return (remembered.Start, remembered.End);
        }

        return (start, end);
    }

    private void ForgetStaleSelection(TextBox editor)
    {
        if (_rememberedSelection is { } remembered &&
            ReferenceEquals(remembered.Editor, editor) &&
            !string.Equals(remembered.TextSnapshot, editor.Text ?? string.Empty, StringComparison.Ordinal))
        {
            _rememberedSelection = null;
        }
    }

    private void ApplyBlockKind(DocumentBlockKind kind)
    {
        var block = _activeVisualBlock ?? _visualBlocks.FirstOrDefault();
        if (block is not null)
        {
            ChangeVisualBlockKind(block, kind);
        }
    }

    private void InsertVisualNearActive(DocumentBlock block)
    {
        if (_activeVisualBlock is { } active)
        {
            InsertVisualBlockAfter(active, block);
            return;
        }

        _visualBlocks.Add(block);
        RenderAndSyncVisualDocument(block.RuntimeId, 0);
    }

    private void OnAddParagraphBlock(object? sender, RoutedEventArgs e) =>
        InsertVisualNearActive(VisualDocumentService.NewParagraph());

    private void OnAddHeadingBlock(object? sender, RoutedEventArgs e) =>
        InsertVisualNearActive(new DocumentBlock { Kind = DocumentBlockKind.Heading2 });

    private void OnAddListBlock(object? sender, RoutedEventArgs e) =>
        InsertVisualNearActive(new DocumentBlock { Kind = DocumentBlockKind.Bullet });

    private void OnAddChecklistBlock(object? sender, RoutedEventArgs e) =>
        InsertVisualNearActive(new DocumentBlock { Kind = DocumentBlockKind.Checklist });

    private void OnAddCodeBlock(object? sender, RoutedEventArgs e) =>
        InsertVisualNearActive(new DocumentBlock { Kind = DocumentBlockKind.Code, Language = "text" });

    private void OnAddTableBlock(object? sender, RoutedEventArgs e) =>
        InsertVisualNearActive(VisualDocumentService.NewTable());

    private void SyncBodyToNote()
    {
        if (ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        note.Body = NoteBodyBox.Text ?? string.Empty;
        _visualBodySnapshot = string.Empty;
        ViewModel.ScheduleSaveNote(note);
        UpdatePreviewAndLinks(note);
        UpdateDocumentCount(note.Body);
        EditorSaveStatusText.Text = "Zapisywanie…";
    }

    private void OnToggleMetadata(object? sender, RoutedEventArgs e)
        => ToggleMetadataPanel();

    public void ToggleMetadataPanel()
    {
        _metadataVisible = !_metadataVisible;
        ViewModel?.SaveEditorDetailsVisibility(_metadataVisible);
        ApplyEditorChrome();
    }

    private void OnToggleFocusMode(object? sender, RoutedEventArgs e) => SetFocusMode(!_focusMode);

    private void SetFocusMode(bool enabled)
    {
        _focusMode = enabled;
        ApplyEditorChrome();
        FocusModeChanged?.Invoke(enabled);
        if (enabled)
        {
            (_activeVisualEditor ?? _visualEditors.Values.FirstOrDefault())?.Focus();
        }
    }

    private void ApplyEditorChrome()
    {
        MetadataPanel.IsVisible = _metadataVisible && !_focusMode;
        MetadataToggleButton.Content = MetadataPanel.IsVisible ? "Ukryj szczegóły" : "Pokaż szczegóły";
        FocusModeButton.Content = _focusMode ? "Zakończ skupienie" : "Tryb skupienia";
    }

    private void UpdateDocumentCount(string? body)
    {
        var text = body ?? string.Empty;
        var words = string.IsNullOrWhiteSpace(text)
            ? 0
            : System.Text.RegularExpressions.Regex.Matches(text, @"\S+").Count;
        DocumentCountText.Text = $"{words} {(words == 1 ? "słowo" : "słów")} • {text.Length} znaków";
    }

    private void OnExportMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNote is null)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(ExportItem("Markdown (.md)", () => _ = ExportNoteAsync("md")));
        menu.Items.Add(ExportItem("HTML (.html)", () => _ = ExportNoteAsync("html")));
        menu.Items.Add(ExportItem("Word / Docs (.docx)", () => _ = ExportNoteAsync("docx")));
        menu.Items.Add(ExportItem("PDF (.pdf)", () => _ = ExportNoteAsync("pdf")));
        ExportMenuButton.ContextMenu = menu;
        Dispatcher.UIThread.Post(() => menu.Open(ExportMenuButton), DispatcherPriority.Input);
    }

    private async void OnCopyForConfluence(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNote is not { } note || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        try
        {
            note.Body = VisualDocumentService.Serialize(_visualBlocks);

            note.Title = NoteTitleBox.Text ?? note.Title;
            note.Tags = FrontMatter.SplitTags(NoteTagsBox.Text);
            await ConfluenceClipboardService.CopyAsync(top, note);
            EditorSaveStatusText.Text = "Skopiowano rich text";
            ViewModel.StatusText = "Treść jest w schowku — wklej ją do Confluence albo innego edytora";
        }
        catch (Exception ex)
        {
            EditorSaveStatusText.Text = "Błąd schowka";
            ViewModel.StatusText = "Nie udało się skopiować treści: " + ex.Message;
        }
    }

    private async void OnShowHistory(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNote is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var revisions = ViewModel.GetSelectedNoteRevisions();
        if (revisions.Count == 0)
        {
            await ShowEditorMessageAsync(
                owner,
                "Historia wersji",
                "Historia pojawi się po kolejnych zapisach tej notatki. Wszystkie wersje są przechowywane wyłącznie lokalnie.");
            return;
        }

        var list = new ListBox
        {
            ItemsSource = revisions,
            MinWidth = 250,
            SelectionMode = SelectionMode.Single
        };
        var preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 320,
            FontSize = 14
        };
        var status = new TextBlock
        {
            Text = "Wybierz wersję, aby zobaczyć podgląd.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68
        };
        var restore = new Button
        {
            Content = "Przywróć wybraną wersję",
            IsEnabled = false,
            MinWidth = 190
        };
        var close = new Button
        {
            Content = "Zamknij",
            IsCancel = true,
            MinWidth = 100
        };

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("260,*"),
            ColumnSpacing = 14,
            RowSpacing = 12,
            Margin = new Thickness(20)
        };
        var explanation = new TextBlock
        {
            Text = "Przywrócenie nie usuwa bieżącej treści — przed zmianą zostanie ona zachowana jako kolejna lokalna wersja.",
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumnSpan(explanation, 2);
        content.Children.Add(explanation);

        Grid.SetRow(list, 1);
        content.Children.Add(list);
        Grid.SetRow(preview, 1);
        Grid.SetColumn(preview, 1);
        content.Children.Add(preview);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        footer.Children.Add(status);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { close, restore }
        };
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        Grid.SetColumnSpan(footer, 2);
        content.Children.Add(footer);

        var dialog = new Window
        {
            Title = "Lokalna historia notatki",
            Width = 850,
            Height = 570,
            MinWidth = 680,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not RevisionInfo revision)
            {
                restore.IsEnabled = false;
                return;
            }

            try
            {
                preview.Text = ViewModel.GetNoteRevisionPreview(revision);
                status.Text = $"Wersja z {revision.TimestampUtc.ToLocalTime():g}";
                restore.IsEnabled = true;
            }
            catch (Exception ex)
            {
                preview.Text = string.Empty;
                status.Text = "Nie można odczytać tej wersji: " + ex.Message;
                restore.IsEnabled = false;
            }
        };
        close.Click += (_, _) => dialog.Close();
        restore.Click += (_, _) =>
        {
            if (list.SelectedItem is not RevisionInfo revision)
            {
                return;
            }

            try
            {
                ViewModel.RestoreSelectedNoteRevision(revision);
                Refresh();
                dialog.Close();
            }
            catch (Exception ex)
            {
                status.Text = "Nie udało się przywrócić wersji: " + ex.Message;
            }
        };

        list.SelectedIndex = 0;
        await dialog.ShowDialog(owner);
    }

    private static async Task ShowEditorMessageAsync(Window owner, string title, string message)
    {
        var close = new Button
        {
            Content = "OK",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 90
        };
        var dialog = new Window
        {
            Title = title,
            Width = 470,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    private static MenuItem ExportItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private async Task ExportNoteAsync(string format)
    {
        if (ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        note.Title = NoteTitleBox.Text ?? note.Title;
        note.Body = VisualDocumentService.Serialize(_visualBlocks);
        note.Tags = FrontMatter.SplitTags(NoteTagsBox.Text);
        ViewModel.FlushPendingSaves();

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            return;
        }

        var safeName = SlugHelper.FromName(string.IsNullOrWhiteSpace(note.Title) ? "notatka" : note.Title);
        var (ext, mime) = format switch
        {
            "html" => ("html", "text/html"),
            "docx" => ("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            "pdf" => ("pdf", "application/pdf"),
            _ => ("md", "text/markdown")
        };

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Eksportuj notatkę",
            SuggestedFileName = $"{safeName}.{ext}",
            DefaultExtension = ext,
            FileTypeChoices =
            [
                new FilePickerFileType(ext.ToUpperInvariant())
                {
                    Patterns = [$"*.{ext}"],
                    MimeTypes = [mime]
                }
            ]
        });

        if (file is null)
        {
            return;
        }

        var path = file.Path.LocalPath;
        try
        {
            switch (format)
            {
                case "html":
                    await File.WriteAllTextAsync(path, NoteExportService.BuildHtmlDocument(note));
                    break;
                case "docx":
                    NoteExportService.WriteDocx(note, path);
                    break;
                case "pdf":
                    NoteExportService.WritePdf(note, path);
                    break;
                default:
                    await File.WriteAllTextAsync(path, NoteExportService.BuildMarkdown(note));
                    break;
            }

            ViewModel.StatusText = $"Wyeksportowano: {path}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = "Eksport nieudany: " + ex.Message;
        }
    }

    private void UpdatePreviewAndLinks(Note note)
    {
        if (PreviewModeRadio.IsChecked == true)
        {
            PreviewContent.Children.Clear();
            var lines = WikiLinkService.ToPreviewLines(note.Body);
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                if (line.Kind == PreviewLineKind.TableRow &&
                    index + 1 < lines.Count &&
                    lines[index + 1].Kind == PreviewLineKind.TableSeparator)
                {
                    var rows = new List<string[]> { SplitPreviewTableRow(line.Text) };
                    index += 2;
                    while (index < lines.Count && lines[index].Kind == PreviewLineKind.TableRow)
                    {
                        rows.Add(SplitPreviewTableRow(lines[index].Text));
                        index++;
                    }

                    index--;
                    PreviewContent.Children.Add(CreatePreviewTable(rows));
                    continue;
                }

                if (line.Kind != PreviewLineKind.TableSeparator)
                {
                    PreviewContent.Children.Add(CreatePreviewLine(note, line));
                }
            }
        }

        WikiLinksList.ItemsSource = WikiLinkService.ExtractTitles(note.Body)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private Control CreatePreviewLine(Note note, PreviewLine line)
    {
        if (line.Kind == PreviewLineKind.Blank)
        {
            return new TextBlock { Height = 8 };
        }

        if (line.Kind == PreviewLineKind.Rule)
        {
            return new Border
            {
                Height = 1,
                Margin = new Thickness(0, 12),
                Background = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128))
            };
        }

        var image = TryCreateImagePreview(note, line.Text);
        if (image is not null)
        {
            return image;
        }

        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = EditorFontFamily,
            Opacity = line.Kind == PreviewLineKind.Muted ? 0.6 : 1
        };
        block.FontSize = line.Kind switch
        {
            PreviewLineKind.H1 => _editorBodyFontSize + 14,
            PreviewLineKind.H2 => _editorBodyFontSize + 8,
            PreviewLineKind.H3 => _editorBodyFontSize + 3,
            _ => _editorBodyFontSize
        };
        block.FontWeight = line.Kind is PreviewLineKind.H1 or PreviewLineKind.H2 or PreviewLineKind.H3
            ? FontWeight.SemiBold
            : FontWeight.Normal;

        var text = line.Kind switch
        {
            PreviewLineKind.Bullet => $"• {line.Text}",
            PreviewLineKind.ChecklistOpen => $"☐  {line.Text}",
            PreviewLineKind.ChecklistDone => $"☑  {line.Text}",
            _ => line.Text
        };
        foreach (var (spanText, bold, italic, code, strike, wiki) in WikiLinkService.ParseInlineSpans(text))
        {
            var run = new Run(spanText);
            if (bold || wiki)
            {
                run.FontWeight = FontWeight.Bold;
            }

            if (italic)
            {
                run.FontStyle = FontStyle.Italic;
            }

            if (code)
            {
                run.FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace");
            }

            if (wiki)
            {
                run.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
            }

            if (strike)
            {
                run.TextDecorations = TextDecorations.Strikethrough;
            }

            block.Inlines!.Add(run);
        }

        if (line.Kind == PreviewLineKind.ChecklistDone)
        {
            block.Opacity = 0.58;
        }

        if (line.Kind == PreviewLineKind.Quote)
        {
            return new Border
            {
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 73, 112, 181)),
                Padding = new Thickness(14, 7),
                Margin = new Thickness(0, 4),
                Background = new SolidColorBrush(Color.FromArgb(18, 73, 112, 181)),
                Child = block
            };
        }

        return block;
    }

    private static string[] SplitPreviewTableRow(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

    private Control CreatePreviewTable(IReadOnlyList<string[]> rows)
    {
        var columns = Math.Max(1, rows.Max(row => row.Length));
        var grid = new Grid { Margin = new Thickness(0, 10) };
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = 110
            });
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                var value = columnIndex < rows[rowIndex].Length ? rows[rowIndex][columnIndex] : string.Empty;
                var cell = new Border
                {
                    BorderThickness = new Thickness(0.5),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
                    Background = rowIndex == 0
                        ? new SolidColorBrush(Color.FromArgb(20, 100, 116, 139))
                        : Brushes.Transparent,
                    Padding = new Thickness(10, 8),
                    Child = new TextBlock
                    {
                        Text = value,
                        FontFamily = EditorFontFamily,
                        FontSize = _editorBodyFontSize - 1,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = rowIndex == 0 ? FontWeight.SemiBold : FontWeight.Normal
                    }
                };
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, columnIndex);
                grid.Children.Add(cell);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grid
        };
    }

    private Control? TryCreateImagePreview(Note note, string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text.Trim(),
            @"^!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)$");
        if (!match.Success || ViewModel is null)
        {
            return null;
        }

        try
        {
            var relative = Uri.UnescapeDataString(match.Groups["path"].Value);
            if (!relative.StartsWith("../Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            var notesFolder = string.IsNullOrWhiteSpace(note.FilePath)
                ? Path.Combine(ViewModel.DataFolder, "Notes")
                : Path.GetDirectoryName(note.FilePath) ?? Path.Combine(ViewModel.DataFolder, "Notes");
            var fullPath = Path.GetFullPath(Path.Combine(
                notesFolder,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            var assetsRoot = Path.GetFullPath(Path.Combine(ViewModel.DataFolder, "Assets"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                return null;
            }

            var alt = match.Groups["alt"].Value;
            var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8) };
            panel.Children.Add(new Image
            {
                Source = new Bitmap(fullPath),
                MaxHeight = 480,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            if (!string.IsNullOrWhiteSpace(alt))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = alt,
                    FontSize = 12,
                    Opacity = 0.62,
                    FontStyle = FontStyle.Italic
                });
            }

            return panel;
        }
        catch
        {
            return null;
        }
    }

    private void OnWikiLinkDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (WikiLinksList.SelectedItem is string title)
        {
            ViewModel?.OpenWikiLink(title);
        }
    }

    private void OnOutlineSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (OutlineList.SelectedItem is not OutlineListItem item)
        {
            return;
        }

        EditModeRadio.IsChecked = true;
        FocusVisualBlock(item.BlockId, 0);
    }

    private void OnTogglePin(object? sender, RoutedEventArgs e) => ViewModel?.TogglePinSelected();

    private void OnDeleteProject(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProject is { } project)
        {
            DeleteProjectRequested?.Invoke(project);
        }
    }

    private void OnNewProjectNote(object? sender, RoutedEventArgs e) => NewProjectNoteRequested?.Invoke();

    private void OnAddProjectTask(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProject is not { } project)
        {
            return;
        }

        Action changed = () =>
        {
            ViewModel.ScheduleSaveProject(project);
            UpdateProjectChecklistSummary(project);
        };
        AddChecklistItem(project.Checklist, changed, ProjectChecklistHost);
    }

    private void OnAddNoteTask(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        AddChecklistItem(note.Checklist, () => ViewModel.ScheduleSaveNote(note), NoteChecklistHost);
    }

    private void AddChecklistItem(List<ChecklistItem> items, Action changed, StackPanel host)
    {
        items.Add(new ChecklistItem { Text = string.Empty });
        changed();
        FillChecklist(host, items, changed, () => AddChecklistItem(items, changed, host));
        Dispatcher.UIThread.Post(() =>
        {
            if (host.Children.LastOrDefault() is Grid row)
            {
                var box = row.Children.OfType<TextBox>().FirstOrDefault();
                box?.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void OnTrashNote(object? sender, RoutedEventArgs e) => ViewModel?.TrashSelectedNote();

    private void OnRelatedNoteDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null || RelatedNotesList.SelectedItem is not string title)
        {
            return;
        }

        var note = ViewModel.RelatedNotes.FirstOrDefault(n => n.Title == title);
        if (note is not null)
        {
            ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
        }
    }

    private void FillChecklist(StackPanel host, List<ChecklistItem> items, Action changed, Action addNext)
    {
        host.Children.Clear();
        foreach (var item in items.ToList())
        {
            var check = new CheckBox { IsChecked = item.IsDone, MinWidth = 32, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBox { Text = item.Text, HorizontalAlignment = HorizontalAlignment.Stretch };
            var delete = new Button { Content = "Usuń" };

            check.IsCheckedChanged += (_, _) =>
            {
                item.IsDone = check.IsChecked == true;
                changed();
            };
            text.TextChanged += (_, _) =>
            {
                item.Text = text.Text ?? string.Empty;
                changed();
            };
            text.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                e.Handled = true;
                changed();
                if (string.IsNullOrWhiteSpace(text.Text) && ReferenceEquals(item, items[^1]))
                {
                    return;
                }

                addNext();
            };
            delete.Click += (_, _) =>
            {
                items.Remove(item);
                changed();
                FillChecklist(host, items, changed, addNext);
            };

            var people = new TextBox
            {
                Text = PersonTagService.Format(item.People),
                PlaceholderText = "Osoby: Anna Kowalska, piotr-nowak",
                FontSize = 11,
                MinHeight = 30
            };
            people.TextChanged += (_, _) =>
            {
                item.People = ViewModel?.ResolvePeopleAssignments(people.Text) ?? PersonTagService.Parse(people.Text);
                changed();
                ViewModel?.NotifyPeopleAssignmentsChanged();
            };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                ColumnSpacing = 6,
                RowSpacing = 4
            };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(delete, 2);
            Grid.SetRow(people, 1);
            Grid.SetColumn(people, 1);
            Grid.SetColumnSpan(people, 2);
            row.Children.Add(check);
            row.Children.Add(text);
            row.Children.Add(delete);
            row.Children.Add(people);
            host.Children.Add(row);
        }
    }

    private Control CreateOpenTaskRow(OpenTask task)
    {
        var check = new CheckBox { IsChecked = false, Content = $"{task.Text} ({task.SourceTitle})", MinWidth = 32 };
        check.IsCheckedChanged += (_, _) =>
        {
            if (check.IsChecked == true)
            {
                ViewModel?.ToggleTask(task);
                Refresh();
            }
        };
        return check;
    }
}
