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

    public EditorPanel()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel { get; set; }

    public event Action<Project>? DeleteProjectRequested;

    public event Action<bool>? FocusModeChanged;

    public bool IsFocusModeEnabled => _focusMode;

    public void Refresh()
    {
        if (ViewModel is null)
        {
            return;
        }

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
            ProjectKindLabel.Text = project.IsFolder ? "FOLDER" : "PROJEKT";
            ProjectNameBox.Text = project.Name;
            ProjectDescriptionBox.Text = project.Description;
            ProjectChecklistSection.IsVisible = !project.IsFolder;
            ProjectTasksSection.IsVisible = !project.IsFolder;
            if (!project.IsFolder)
            {
                FillChecklist(
                    ProjectChecklistHost,
                    project.Checklist,
                    () => ViewModel.ScheduleSaveProject(project),
                    () => AddChecklistItem(project.Checklist, () => ViewModel.ScheduleSaveProject(project), ProjectChecklistHost));
            }

            RelatedNotesList.ItemsSource = ViewModel.RelatedNotes.Select(n => n.Title).ToList();

            ProjectOpenTasksHost.Children.Clear();
            foreach (var task in ViewModel.ProjectNoteTasks)
            {
                ProjectOpenTasksHost.Children.Add(CreateOpenTaskRow(task));
            }
        }
        else if (ViewModel.SelectedNote is { } note)
        {
            NotePanel.IsVisible = true;
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

        SourceModeRadio.IsChecked = true;
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
        if (EditModeRadio.IsChecked == true)
        {
            (_activeVisualEditor ?? _visualEditors.Values.FirstOrDefault())?.Focus();
        }
        else
        {
            NoteBodyBox.Focus();
        }
    }

    private void OnProjectChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || ViewModel?.SelectedProject is not { } project)
        {
            return;
        }

        project.Name = ProjectNameBox.Text ?? string.Empty;
        project.Description = ProjectDescriptionBox.Text ?? string.Empty;
        ViewModel.ScheduleSaveProject(project);
    }

    private void OnNoteChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        note.Title = NoteTitleBox.Text ?? string.Empty;
        note.Body = NoteBodyBox.Text ?? string.Empty;
        if (SourceModeRadio.IsChecked == true)
        {
            _visualBodySnapshot = string.Empty;
        }
        note.Tags = FrontMatter.SplitTags(NoteTagsBox.Text);
        ViewModel.ScheduleSaveNote(note);
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
        var visual = EditModeRadio.IsChecked == true;
        var source = SourceModeRadio.IsChecked == true;
        var preview = PreviewModeRadio.IsChecked == true;
        if (visual && ViewModel?.SelectedNote is { } note &&
            !string.Equals(_visualBodySnapshot, note.Body, StringComparison.Ordinal))
        {
            LoadVisualDocument(note, force: true);
        }

        VisualEditorScroll.IsVisible = visual;
        NoteBodyBox.IsVisible = source;
        PreviewHost.IsVisible = preview;
        FormatToolbar.IsEnabled = !preview;
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
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("34,*"),
            ColumnSpacing = 5,
            Margin = new Thickness(0, 1),
            MinWidth = 0
        };
        var menuButton = new Button
        {
            Content = BlockGlyph(block.Kind),
            Width = 30,
            Height = 30,
            Padding = new Thickness(2),
            Opacity = 0.56,
            VerticalAlignment = VerticalAlignment.Top
        };
        ToolTip.SetTip(menuButton, "Zmień typ, przesuń lub usuń blok");
        menuButton.Click += (_, _) => OpenVisualBlockMenu(menuButton, block);
        row.Children.Add(menuButton);

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
        Grid.SetColumn(content, 1);
        row.Children.Add(content);
        return row;
    }

    private Control CreateVisualTextBlock(DocumentBlock block, int numberedIndex)
    {
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
            FontSize = block.Kind switch
            {
                DocumentBlockKind.Heading1 => 30,
                DocumentBlockKind.Heading2 => 24,
                DocumentBlockKind.Heading3 => 19,
                _ => 16
            },
            FontWeight = block.Kind is DocumentBlockKind.Heading1 or DocumentBlockKind.Heading2 or DocumentBlockKind.Heading3
                ? FontWeight.SemiBold
                : FontWeight.Normal,
            FontFamily = block.Kind == DocumentBlockKind.Code
                ? new FontFamily("Cascadia Mono, Consolas, Menlo, monospace")
                : FontFamily.Default,
            LineHeight = block.Kind == DocumentBlockKind.Code ? 22 : 25,
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
        };
        editor.TextChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            block.Text = editor.Text ?? string.Empty;
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
        editor.KeyDown += (_, args) => OnVisualEditorKeyDown(block, editor, args);

        Control body = editor;
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
            Grid.SetColumn(editor, 1);
            prefix.Children.Add(editor);
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
            body = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(22, 100, 116, 139)),
                Padding = new Thickness(10, 7),
                Child = body
            };
        }

        return body;
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
                    FontWeight = rowIndex == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                    Background = rowIndex == 0
                        ? new SolidColorBrush(Color.FromArgb(22, 100, 116, 139))
                        : Brushes.Transparent
                };
                box.GotFocus += (_, _) =>
                {
                    _activeVisualEditor = box;
                    _activeVisualBlock = block;
                };
                box.TextChanged += (_, _) =>
                {
                    if (_suppress)
                    {
                        return;
                    }

                    block.Cells[capturedRow][capturedColumn] = box.Text ?? string.Empty;
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
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(caption, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        caption.GotFocus += (_, _) =>
        {
            _activeVisualEditor = caption;
            _activeVisualBlock = block;
        };
        caption.TextChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            block.Text = caption.Text ?? string.Empty;
            SyncVisualToNote();
        };
        panel.Children.Add(caption);
        return panel;
    }

    private void OnVisualEditorKeyDown(DocumentBlock block, TextBox editor, KeyEventArgs e)
    {
        if (PlatformKeys.IsCommand(e.KeyModifiers) && e.Key == Key.B)
        {
            WrapSelectionIn(editor, "**", "**", "pogrubienie");
            e.Handled = true;
            return;
        }

        if (PlatformKeys.IsCommand(e.KeyModifiers) && e.Key == Key.I)
        {
            WrapSelectionIn(editor, "*", "*", "kursywa");
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
        block.Text = text[..start];
        var nextKind = block.Kind is DocumentBlockKind.Bullet or DocumentBlockKind.Numbered or DocumentBlockKind.Checklist
            ? block.Kind
            : DocumentBlockKind.Paragraph;
        var next = new DocumentBlock
        {
            Kind = nextKind,
            Text = text[end..]
        };
        var index = _visualBlocks.IndexOf(block);
        _visualBlocks.Insert(index + 1, next);
        RenderAndSyncVisualDocument(next.RuntimeId, 0);
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
        menu.Open(anchor);
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

    private sealed class OutlineListItem(string blockId, string title, int level)
    {
        public string BlockId { get; } = blockId;
        public string DisplayTitle { get; } = new string(' ', Math.Max(0, level - 1) * 3) + title;
        public override string ToString() => DisplayTitle;
    }

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

    private void OnBodyKeyDown(object? sender, KeyEventArgs e)
    {
        if (!PlatformKeys.IsCommand(e.KeyModifiers))
        {
            return;
        }

        if (e.Key == Key.B)
        {
            WrapSelection("**", "**", "pogrubienie");
            e.Handled = true;
        }
        else if (e.Key == Key.I)
        {
            WrapSelection("*", "*", "kursywa");
            e.Handled = true;
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

    private void OnFormatBold(object? sender, RoutedEventArgs e) => WrapSelection("**", "**", "pogrubienie");
    private void OnFormatItalic(object? sender, RoutedEventArgs e) => WrapSelection("*", "*", "kursywa");
    private void OnFormatStrike(object? sender, RoutedEventArgs e) => WrapSelection("~~", "~~", "przekreślenie");
    private void OnFormatCode(object? sender, RoutedEventArgs e) => WrapSelection("`", "`", "kod");
    private void OnFormatWiki(object? sender, RoutedEventArgs e) => WrapSelection("[[", "]]", "wikilink");
    private void OnFormatLink(object? sender, RoutedEventArgs e) => WrapSelection("[", "](https://)", "tekst linku");
    private void OnFormatH1(object? sender, RoutedEventArgs e) => ApplyBlockKindOrPrefix(DocumentBlockKind.Heading1, "# ");
    private void OnFormatH2(object? sender, RoutedEventArgs e) => ApplyBlockKindOrPrefix(DocumentBlockKind.Heading2, "## ");
    private void OnFormatH3(object? sender, RoutedEventArgs e) => ApplyBlockKindOrPrefix(DocumentBlockKind.Heading3, "### ");
    private void OnFormatBullet(object? sender, RoutedEventArgs e) => ApplyBlockKindOrPrefix(DocumentBlockKind.Bullet, "- ");
    private void OnFormatNumbered(object? sender, RoutedEventArgs e) => ApplyBlockKindOrPrefix(DocumentBlockKind.Numbered, "1. ");
    private void OnFormatCheck(object? sender, RoutedEventArgs e) => ApplyBlockKindOrPrefix(DocumentBlockKind.Checklist, "- [ ] ");
    private void OnFormatQuote(object? sender, RoutedEventArgs e) => ApplyBlockKindOrPrefix(DocumentBlockKind.Quote, "> ");
    private void OnFormatDash(object? sender, RoutedEventArgs e) => InsertAtCaret(" — ");
    private void OnFormatRule(object? sender, RoutedEventArgs e)
    {
        if (EditModeRadio.IsChecked == true)
        {
            InsertVisualNearActive(new DocumentBlock { Kind = DocumentBlockKind.Rule });
        }
        else
        {
            InsertBlock("---");
        }
    }

    private void OnFormatTable(object? sender, RoutedEventArgs e)
    {
        if (EditModeRadio.IsChecked == true)
        {
            InsertVisualNearActive(VisualDocumentService.NewTable());
        }
        else
        {
            InsertBlock("| Kolumna 1 | Kolumna 2 |\n| --- | --- |\n| Wartość | Wartość |");
        }
    }

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
            if (EditModeRadio.IsChecked == true)
            {
                InsertVisualNearActive(new DocumentBlock
                {
                    Kind = DocumentBlockKind.Image,
                    Text = alt,
                    ImagePath = imported.MarkdownPath
                });
            }
            else
            {
                InsertBlock(AttachmentStore.BuildMarkdownImage(alt, imported.MarkdownPath));
            }
            ViewModel.StatusText = $"Dodano obraz: {Path.GetFileName(imported.FullPath)}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = "Nie udało się dodać obrazu: " + ex.Message;
            EditorSaveStatusText.Text = "Błąd dodawania obrazu";
        }
    }

    private void WrapSelection(string before, string after, string placeholder)
    {
        if (EditModeRadio.IsChecked == true)
        {
            var editor = _activeVisualEditor ?? _visualEditors.Values.FirstOrDefault();
            if (editor is not null)
            {
                WrapSelectionIn(editor, before, after, placeholder);
            }

            return;
        }

        NoteBodyBox.Focus();
        var text = NoteBodyBox.Text ?? string.Empty;
        var start = Math.Min(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
        var end = Math.Max(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        var selected = end > start ? text[start..end] : placeholder;
        var next = text[..start] + before + selected + after + text[end..];
        _suppress = true;
        NoteBodyBox.Text = next;
        _suppress = false;
        NoteBodyBox.SelectionStart = start + before.Length;
        NoteBodyBox.SelectionEnd = start + before.Length + selected.Length;
        SyncBodyToNote();
    }

    private void WrapSelectionIn(TextBox editor, string before, string after, string placeholder)
    {
        editor.Focus();
        var text = editor.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        var selected = end > start ? text[start..end] : placeholder;
        editor.Text = text[..start] + before + selected + after + text[end..];
        editor.SelectionStart = start + before.Length;
        editor.SelectionEnd = start + before.Length + selected.Length;
    }

    private void ApplyBlockKindOrPrefix(DocumentBlockKind kind, string sourcePrefix)
    {
        if (EditModeRadio.IsChecked == true)
        {
            var block = _activeVisualBlock ?? _visualBlocks.FirstOrDefault();
            if (block is not null)
            {
                ChangeVisualBlockKind(block, kind);
            }

            return;
        }

        PrefixLines(sourcePrefix);
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

    private void PrefixLines(string prefix)
    {
        NoteBodyBox.Focus();
        var text = NoteBodyBox.Text ?? string.Empty;
        var start = Math.Min(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
        var end = Math.Max(NoteBodyBox.SelectionStart, NoteBodyBox.SelectionEnd);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);

        if (end == start)
        {
            var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
            var lineEnd = text.IndexOf('\n', start);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            start = lineStart;
            end = lineEnd;
        }

        var block = text[start..end];
        var lines = block.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (lines.Length == 1)
                {
                    lines[i] = prefix;
                }

                continue;
            }

            var stripped = line;
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"^#{1,3}\s+", "");
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"^>\s+", "");
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"^[-*]\s+(\[[ xX]\]\s+)?", "");
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"^\d+\.\s+", "");
            lines[i] = prefix + stripped;
        }

        var replaced = string.Join("\n", lines);
        var next = text[..start] + replaced + text[end..];
        _suppress = true;
        NoteBodyBox.Text = next;
        _suppress = false;
        NoteBodyBox.SelectionStart = start;
        NoteBodyBox.SelectionEnd = start + replaced.Length;
        SyncBodyToNote();
    }

    private void InsertAtCaret(string snippet)
    {
        NoteBodyBox.Focus();
        var text = NoteBodyBox.Text ?? string.Empty;
        var caret = Math.Clamp(NoteBodyBox.CaretIndex, 0, text.Length);
        var next = text[..caret] + snippet + text[caret..];
        _suppress = true;
        NoteBodyBox.Text = next;
        _suppress = false;
        NoteBodyBox.CaretIndex = caret + snippet.Length;
        SyncBodyToNote();
    }

    private void InsertBlock(string block)
    {
        var text = NoteBodyBox.Text ?? string.Empty;
        var caret = Math.Clamp(NoteBodyBox.CaretIndex, 0, text.Length);
        var before = caret > 0 && text[caret - 1] != '\n' ? "\n\n" : string.Empty;
        var after = caret < text.Length && text[caret] != '\n' ? "\n\n" : "\n";
        InsertAtCaret(before + block + after);
    }

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
    {
        _metadataVisible = !_metadataVisible;
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
            if (EditModeRadio.IsChecked == true)
            {
                (_activeVisualEditor ?? _visualEditors.Values.FirstOrDefault())?.Focus();
            }
            else
            {
                NoteBodyBox.Focus();
            }
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
        menu.Open(ExportMenuButton);
    }

    private async void OnCopyForConfluence(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNote is not { } note || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        try
        {
            if (EditModeRadio.IsChecked == true)
            {
                note.Body = VisualDocumentService.Serialize(_visualBlocks);
            }
            else
            {
                note.Body = NoteBodyBox.Text ?? note.Body;
            }

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
        note.Body = NoteBodyBox.Text ?? note.Body;
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
            Opacity = line.Kind == PreviewLineKind.Muted ? 0.6 : 1
        };
        block.FontSize = line.Kind switch
        {
            PreviewLineKind.H1 => 30,
            PreviewLineKind.H2 => 24,
            PreviewLineKind.H3 => 19,
            _ => 16
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

    private static Control CreatePreviewTable(IReadOnlyList<string[]> rows)
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

    private void OnAddProjectTask(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProject is not { } project)
        {
            return;
        }

        AddChecklistItem(project.Checklist, () => ViewModel.ScheduleSaveProject(project), ProjectChecklistHost);
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

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 6
            };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(delete, 2);
            row.Children.Add(check);
            row.Children.Add(text);
            row.Children.Add(delete);
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
