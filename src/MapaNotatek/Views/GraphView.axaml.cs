using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MapaNotatek.Models;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class GraphView : UserControl
{
    private const double ProjectNodeWidth = 184;
    private const double ProjectNodeHeight = 76;
    private const double NoteNodeWidth = 166;
    private const double NoteNodeHeight = 58;
    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly Dictionary<string, Border> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GraphEdge> _edges = [];
    private string? _dragId;
    private Point _dragOffset;
    private Point _pressPoint;
    private bool _dragging;
    private double _zoom = 1;
    private Point? _lastContextCanvasPoint;
    private bool _panning;
    private bool _panMoved;
    private bool _spaceHeld;
    private bool _updatingProjectFilter;
    private Point _panPointerStart;
    private Vector _panScrollStart;

    public GraphView()
    {
        InitializeComponent();
        ZoomHost.LayoutTransform = _zoomTransform;
        GraphCanvas.ContextRequested += OnCanvasContextRequested;
    }

    public MainViewModel? ViewModel { get; set; }

    public event Action<double>? ZoomChanged;
    public event Action<Project>? DeleteProjectRequested;

    public double ZoomFactor => _zoom;

    public void SetZoom(double factor)
    {
        _zoom = Math.Clamp(factor, 0.25, 3);
        _zoomTransform.ScaleX = _zoom;
        _zoomTransform.ScaleY = _zoom;
        GraphZoomText.Content = $"{Math.Round(_zoom * 100):0}%";
        ZoomChanged?.Invoke(_zoom);
    }

    public Point GetViewportCenterInCanvas()
    {
        var viewport = GraphScroll.Viewport;
        var offset = GraphScroll.Offset;
        var cx = (offset.X + (viewport.Width / 2)) / Math.Max(_zoom, 0.01);
        var cy = (offset.Y + (viewport.Height / 2)) / Math.Max(_zoom, 0.01);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return new Point(LayoutService.CanvasWidth / 2, LayoutService.CanvasHeight / 2);
        }

        return new Point(cx, cy);
    }

    public void CenterOnNode(string id)
    {
        if (ViewModel is null)
        {
            return;
        }

        var pos = LayoutService.Get(ViewModel.State.NodePositions, id);
        Dispatcher.UIThread.Post(() =>
        {
            var viewport = GraphScroll.Viewport;
            if (viewport.Width <= 0 || viewport.Height <= 0)
            {
                return;
            }

            var ox = (pos.X * _zoom) - (viewport.Width / 2);
            var oy = (pos.Y * _zoom) - (viewport.Height / 2);
            GraphScroll.Offset = new Vector(Math.Max(0, ox), Math.Max(0, oy));
        }, DispatcherPriority.Background);
    }

    public void Refresh()
    {
        if (ViewModel is null)
        {
            return;
        }

        GraphCanvas.Children.Clear();
        _nodes.Clear();
        _edges.Clear();

        var projects = ViewModel.GraphProjects.ToList();
        var notes = ViewModel.GraphNotes.ToList();
        RefreshProjectFilter();
        LayoutService.ApplyMissingPositions(ViewModel.Projects, ViewModel.Notes, ViewModel.State.NodePositions);
        AddGrid();

        foreach (var child in projects.Where(p => !string.IsNullOrWhiteSpace(p.ParentId)))
        {
            var parent = projects.FirstOrDefault(p => p.Id == child.ParentId);
            if (parent is null)
            {
                continue;
            }

            var from = LayoutService.Get(ViewModel.State.NodePositions, parent.Id);
            var to = LayoutService.Get(ViewModel.State.NodePositions, child.Id);
            AddEdge(parent.Id, child.Id, from, to, EdgeKind.Hierarchy);
        }

        foreach (var note in notes)
        {
            foreach (var project in projects.Where(p => LayoutService.NoteLinksTo(note, p)))
            {
                var from = LayoutService.Get(ViewModel.State.NodePositions, project.Id);
                var to = LayoutService.Get(ViewModel.State.NodePositions, note.Id);
                AddEdge(project.Id, note.Id, from, to, EdgeKind.ProjectMembership);
            }
        }

        var notesByTitle = notes
            .Where(note => !string.IsNullOrWhiteSpace(note.Title))
            .GroupBy(note => note.Title.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.CurrentCultureIgnoreCase);
        var wikiEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in notes)
        {
            foreach (var linkedTitle in WikiLinkService.ExtractTitles(source.Body).Distinct(StringComparer.CurrentCultureIgnoreCase))
            {
                if (!notesByTitle.TryGetValue(linkedTitle, out var target) ||
                    string.Equals(source.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var edgeKey = string.Compare(source.Id, target.Id, StringComparison.OrdinalIgnoreCase) < 0
                    ? $"{source.Id}|{target.Id}"
                    : $"{target.Id}|{source.Id}";
                if (!wikiEdges.Add(edgeKey))
                {
                    continue;
                }

                var from = LayoutService.Get(ViewModel.State.NodePositions, source.Id);
                var to = LayoutService.Get(ViewModel.State.NodePositions, target.Id);
                AddEdge(source.Id, target.Id, from, to, EdgeKind.WikiLink);
            }
        }

        foreach (var project in projects)
        {
            var noteCount = notes.Count(note => LayoutService.NoteLinksTo(note, project));
            var childCount = projects.Count(child => string.Equals(child.ParentId, project.Id, StringComparison.OrdinalIgnoreCase));
            var openTasks = project.Checklist.Count(item => !item.IsDone);
            var meta = project.IsFolder
                ? $"Folder · {childCount} elementów"
                : $"Projekt · {noteCount} notatek · {openTasks} zadań";
            AddNode(project.Id, project.Name, meta, isProject: true, isFolder: project.IsFolder);
        }

        foreach (var note in notes)
        {
            var openTasks = note.Checklist.Count(item => !item.IsDone);
            var tagLabel = note.Tags.Count == 0 ? "bez tagów" : $"{note.Tags.Count} tagów";
            var meta = openTasks == 0 ? $"Notatka · {tagLabel}" : $"Notatka · {tagLabel} · {openTasks} zadań";
            AddNode(note.Id, note.Title, meta, isProject: false, isFolder: false);
        }

        var projectCount = projects.Count(project => !project.IsFolder);
        var folderCount = projects.Count(project => project.IsFolder);
        GraphSummaryText.Text = $"{projectCount} projektów · {folderCount} folderów · {notes.Count} notatek · {_edges.Count} relacji";
        HighlightSelection();
    }

    private void RefreshProjectFilter()
    {
        if (ViewModel is null)
        {
            return;
        }

        var options = new List<ProjectFilterOption>
        {
            new(null, "Wszystkie projekty")
        };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddBranch(Project project, int depth)
        {
            if (!visited.Add(project.Id))
            {
                return;
            }

            var branch = depth == 0 ? string.Empty : new string('·', Math.Min(depth, 4)) + " ";
            var kind = project.IsFolder ? "Folder" : "Projekt";
            options.Add(new ProjectFilterOption(project.Id, $"{branch}{kind} · {project.Name}"));
            foreach (var child in ViewModel.Projects
                         .Where(candidate => string.Equals(candidate.ParentId, project.Id, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(candidate => candidate.IsFolder ? 0 : 1)
                         .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                AddBranch(child, depth + 1);
            }
        }

        foreach (var root in ViewModel.Projects
                     .Where(project => string.IsNullOrWhiteSpace(project.ParentId) ||
                                       ViewModel.Projects.All(candidate => !string.Equals(candidate.Id, project.ParentId, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(project => project.IsFolder ? 0 : 1)
                     .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AddBranch(root, 0);
        }

        foreach (var orphan in ViewModel.Projects
                     .Where(project => !visited.Contains(project.Id))
                     .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AddBranch(orphan, 0);
        }

        _updatingProjectFilter = true;
        GraphProjectFilter.ItemsSource = options;
        GraphProjectFilter.SelectedItem = options.FirstOrDefault(option =>
            string.Equals(option.ProjectId, ViewModel.FocusedProjectId, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        _updatingProjectFilter = false;
    }

    private void OnGraphProjectFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingProjectFilter || ViewModel is null ||
            GraphProjectFilter.SelectedItem is not ProjectFilterOption option)
        {
            return;
        }

        ViewModel.ClearGraphSelection();
        if (option.ProjectId is null)
        {
            ViewModel.ShowAllProjects();
        }
        else
        {
            var project = ViewModel.Projects.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, option.ProjectId, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                ViewModel.ShowAllProjects();
            }
            else
            {
                ViewModel.FocusedProjectId = project.Id;
                ViewModel.StatusText = $"Graf ograniczony do: {project.Name}";
            }
        }

        Dispatcher.UIThread.Post(FitToContent, DispatcherPriority.Background);
    }

    private void AddGrid()
    {
        for (var coordinate = 0; coordinate <= LayoutService.CanvasWidth; coordinate += 100)
        {
            var major = coordinate % 500 == 0;
            var brush = new SolidColorBrush(major
                ? Color.FromArgb(26, 91, 141, 239)
                : Color.FromArgb(12, 91, 141, 239));
            GraphCanvas.Children.Add(new Line
            {
                StartPoint = new Point(coordinate, 0),
                EndPoint = new Point(coordinate, LayoutService.CanvasHeight),
                Stroke = brush,
                StrokeThickness = major ? 1.2 : 0.8,
                IsHitTestVisible = false
            });
            GraphCanvas.Children.Add(new Line
            {
                StartPoint = new Point(0, coordinate),
                EndPoint = new Point(LayoutService.CanvasWidth, coordinate),
                Stroke = brush,
                StrokeThickness = major ? 1.2 : 0.8,
                IsHitTestVisible = false
            });
        }
    }

    private void AddEdge(string fromId, string toId, GraphPosition from, GraphPosition to, EdgeKind kind)
    {
        var (color, thickness) = kind switch
        {
            EdgeKind.Hierarchy => (Color.FromArgb(220, 91, 141, 239), 2.4),
            EdgeKind.WikiLink => (Color.FromArgb(220, 168, 85, 247), 2.0),
            _ => (Color.FromArgb(180, 138, 148, 164), 1.7)
        };
        var path = new Avalonia.Controls.Shapes.Path
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            Opacity = 0.86,
            IsHitTestVisible = false,
            Data = BuildCurve(new Point(from.X, from.Y), new Point(to.X, to.Y))
        };
        var edge = new GraphEdge(fromId, toId, kind, path, thickness);
        _edges.Add(edge);
        GraphCanvas.Children.Add(path);
    }

    private static StreamGeometry BuildCurve(Point from, Point to)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(from, false);
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        Point control1;
        Point control2;
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            control1 = new Point(from.X + (dx * 0.42), from.Y);
            control2 = new Point(to.X - (dx * 0.42), to.Y);
        }
        else
        {
            control1 = new Point(from.X, from.Y + (dy * 0.42));
            control2 = new Point(to.X, to.Y - (dy * 0.42));
        }

        context.CubicBezierTo(control1, control2, to);
        return geometry;
    }

    private void AddNode(string id, string title, string meta, bool isProject, bool isFolder)
    {
        if (ViewModel is null)
        {
            return;
        }

        var position = LayoutService.Get(ViewModel.State.NodePositions, id);
        var width = isProject ? ProjectNodeWidth : NoteNodeWidth;
        var height = isProject ? ProjectNodeHeight : NoteNodeHeight;
        var icon = isFolder ? "F" : isProject ? "P" : "N";
        var iconBackground = isProject
            ? Color.FromArgb(45, 255, 255, 255)
            : Color.FromArgb(34, 40, 103, 214);

        var titleBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? "Bez tytułu" : title.Trim(),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width - 62
        };
        titleBlock.Classes.Add("graph-title");

        var metaBlock = new TextBlock
        {
            Text = meta,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width - 62
        };
        metaBlock.Classes.Add("graph-meta");

        var textStack = new StackPanel { Spacing = 2 };
        textStack.Children.Add(titleBlock);
        textStack.Children.Add(metaBlock);

        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(iconBackground),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = icon,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = isProject ? Brushes.White : new SolidColorBrush(Color.FromRgb(40, 103, 214))
            }
        };

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("32,10,*") };
        content.Children.Add(iconBorder);
        Grid.SetColumn(textStack, 2);
        content.Children.Add(textStack);

        var host = new Border
        {
            Width = width,
            Height = height,
            Child = content
        };
        host.Classes.Add("graph-node");
        host.Classes.Add(isFolder ? "graph-folder" : isProject ? "graph-project" : "graph-note");
        host.Tag = new NodeTag(id, isProject, isFolder, titleBlock.Text ?? "Bez tytułu", meta, width, height, host);
        host.PointerPressed += OnNodePressed;
        host.PointerMoved += OnNodeMoved;
        host.PointerReleased += OnNodeReleased;
        host.PointerCaptureLost += OnNodeCaptureLost;
        host.DoubleTapped += OnNodeDoubleTapped;
        host.ContextRequested += OnNodeContextRequested;

        Canvas.SetLeft(host, position.X - (width / 2));
        Canvas.SetTop(host, position.Y - (height / 2));
        GraphCanvas.Children.Add(host);
        _nodes[id] = host;
    }

    private void HighlightSelection()
    {
        if (ViewModel is null)
        {
            return;
        }

        var selectedId = ViewModel.SelectedGraphId;
        var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (selectedId is not null)
        {
            relatedIds.Add(selectedId);
            foreach (var edge in _edges.Where(edge => edge.Connects(selectedId)))
            {
                relatedIds.Add(edge.FromId);
                relatedIds.Add(edge.ToId);
            }
        }

        foreach (var pair in _nodes)
        {
            if (pair.Value.Tag is not NodeTag tag)
            {
                continue;
            }

            var selected = string.Equals(pair.Key, selectedId, StringComparison.OrdinalIgnoreCase);
            if (selected && !tag.Card.Classes.Contains("selected"))
            {
                tag.Card.Classes.Add("selected");
            }
            else if (!selected)
            {
                tag.Card.Classes.Remove("selected");
            }

            tag.Card.Opacity = selectedId is null || relatedIds.Contains(pair.Key) ? 1 : 0.28;
        }

        foreach (var edge in _edges)
        {
            var connected = selectedId is null || edge.Connects(selectedId);
            edge.Path.Opacity = connected ? 0.9 : 0.1;
            edge.Path.StrokeThickness = connected && selectedId is not null
                ? edge.BaseThickness + 1.2
                : edge.BaseThickness;
        }

        if (selectedId is not null && _nodes.TryGetValue(selectedId, out var selectedHost) && selectedHost.Tag is NodeTag selectedTag)
        {
            GraphSelectionKind.Text = selectedTag.IsFolder ? "FOLDER" : selectedTag.IsProject ? "PROJEKT" : "NOTATKA";
            GraphSelectionTitle.Text = selectedTag.Title;
            GraphSelectionMeta.Text = $"{selectedTag.Meta} · {Math.Max(0, relatedIds.Count - 1)} powiązań";
            GraphSelectionCard.IsVisible = true;
        }
        else
        {
            GraphSelectionCard.IsVisible = false;
        }
    }

    private void OnNodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border host || host.Tag is not NodeTag tag || ViewModel is null)
        {
            return;
        }

        // Only left button starts node drag / select.
        if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        ViewModel.SelectGraphNode(tag.Id, tag.IsProject);
        HighlightSelection();
        var point = e.GetPosition(GraphCanvas);
        var left = Canvas.GetLeft(host);
        var top = Canvas.GetTop(host);
        _dragId = tag.Id;
        _dragOffset = new Point(point.X - left, point.Y - top);
        _pressPoint = point;
        _dragging = false;
        _panning = false;
        e.Pointer.Capture(host);
        e.Handled = true;
    }

    private void OnNodeMoved(object? sender, PointerEventArgs e)
    {
        if (_dragId is null || sender is not Border host || ViewModel is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(GraphCanvas);
        var left = point.X - _dragOffset.X;
        var top = point.Y - _dragOffset.Y;
        Canvas.SetLeft(host, left);
        Canvas.SetTop(host, top);
        var dx = point.X - _pressPoint.X;
        var dy = point.Y - _pressPoint.Y;
        if ((dx * dx) + (dy * dy) > 16)
        {
            _dragging = true;
        }

        if (host.Tag is NodeTag tag)
        {
            UpdateEdges(tag.Id, left + (tag.Width / 2), top + (tag.Height / 2));
        }

        e.Handled = true;
    }

    private void OnNodeReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A single click explores the neighbourhood; double-click/Enter opens the item.
        FinishPointer(sender, openOnClick: false);
        e.Pointer.Capture(null);
    }

    private void OnNodeCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        FinishPointer(sender, openOnClick: false);

    private void FinishPointer(object? sender, bool openOnClick)
    {
        if (_dragId is null || ViewModel is null || sender is not Border host || host.Tag is not NodeTag tag)
        {
            _dragId = null;
            _dragging = false;
            return;
        }

        if (_dragging)
        {
            var x = Canvas.GetLeft(host) + (tag.Width / 2);
            var y = Canvas.GetTop(host) + (tag.Height / 2);
            ViewModel.MoveNode(tag.Id, x, y, recordUndo: true);
        }
        else if (openOnClick)
        {
            if (tag.IsProject)
            {
                var project = ViewModel.Projects.FirstOrDefault(p => p.Id == tag.Id);
                if (project is not null)
                {
                    ViewModel.SelectProject(project, openEditor: true, focusGraph: true);
                }
            }
            else
            {
                var note = ViewModel.Notes.FirstOrDefault(n => n.Id == tag.Id);
                if (note is not null)
                {
                    ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
                }
            }
        }

        _dragId = null;
        _dragging = false;
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { Tag: NodeTag tag } && ViewModel is not null)
        {
            ViewModel.SelectGraphNode(tag.Id, tag.IsProject);
            ViewModel.OpenSelectedGraphNode();
        }
    }

    private void OnCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        _lastContextCanvasPoint = e.GetPosition(GraphCanvas);

        // Only pan when the empty canvas is hit — never steal node drags.
        if (!ReferenceEquals(e.Source, GraphCanvas))
        {
            return;
        }

        var point = e.GetCurrentPoint(GraphScroll);
        var isMiddle = point.Properties.IsMiddleButtonPressed;
        var isLeft = point.Properties.IsLeftButtonPressed;
        if (isMiddle || (isLeft && (_spaceHeld || e.KeyModifiers.HasFlag(KeyModifiers.Control))))
        {
            BeginPan(e.GetPosition(GraphScroll));
            e.Pointer.Capture(GraphCanvas);
            e.Handled = true;
            return;
        }

        if (isLeft)
        {
            BeginPan(e.GetPosition(GraphScroll));
            e.Pointer.Capture(GraphCanvas);
            e.Handled = true;
        }
    }

    private void OnCanvasMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        var point = e.GetPosition(GraphScroll);
        var dx = point.X - _panPointerStart.X;
        var dy = point.Y - _panPointerStart.Y;
        if ((dx * dx) + (dy * dy) > 9)
        {
            _panMoved = true;
        }

        GraphScroll.Offset = new Vector(
            Math.Max(0, _panScrollStart.X - dx),
            Math.Max(0, _panScrollStart.Y - dy));
        e.Handled = true;
    }

    private void OnCanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndPan(clearSelectionOnClick: true);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnCanvasCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        EndPan(clearSelectionOnClick: false);

    private void BeginPan(Point pointerInScroll)
    {
        _panning = true;
        _panMoved = false;
        _panPointerStart = pointerInScroll;
        _panScrollStart = GraphScroll.Offset;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void EndPan(bool clearSelectionOnClick)
    {
        if (!_panning)
        {
            return;
        }

        var moved = _panMoved;
        _panning = false;
        _panMoved = false;
        Cursor = Cursor.Default;
        if (!moved && clearSelectionOnClick && ViewModel is not null)
        {
            ViewModel.ClearGraphSelection();
            HighlightSelection();
        }
    }

    private void OnNodeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Border host || host.Tag is not NodeTag tag || ViewModel is null)
        {
            return;
        }

        ViewModel.SelectGraphNode(tag.Id, tag.IsProject);
        HighlightSelection();
        host.ContextMenu = tag.IsProject ? BuildProjectMenu(tag.Id) : BuildNoteMenu(tag.Id);
        e.Handled = false;
    }

    private void OnCanvasContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        GraphCanvas.ContextMenu = BuildCanvasMenu();
    }

    private ContextMenu BuildProjectMenu(string id)
    {
        var project = ViewModel!.Projects.FirstOrDefault(p => p.Id == id);
        var menu = new ContextMenu();
        menu.Items.Add(Item("Otwórz / Edytuj", () =>
        {
            if (project is not null)
            {
                ViewModel.SelectProject(project, openEditor: true, focusGraph: true);
            }
        }));
        menu.Items.Add(Item("Nowa notatka w projekcie", () =>
        {
            if (project is not null)
            {
                ViewModel.SelectProject(project, openEditor: false, focusGraph: true, filterToProject: true);
            }

            var center = GetViewportCenterInCanvas();
            ViewModel.NewNote(center.X, center.Y);
        }));
        menu.Items.Add(Item("Nowy podprojekt", () =>
        {
            var center = GetViewportCenterInCanvas();
            ViewModel.NewProject(center.X + 40, center.Y + 40, parentId: id);
        }));
        menu.Items.Add(Item("Nowy folder wewnątrz", () =>
        {
            var center = GetViewportCenterInCanvas();
            ViewModel.NewFolder(center.X + 40, center.Y + 40, parentId: id);
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Filtruj tylko ten projekt", () =>
        {
            if (project is not null)
            {
                ViewModel.SelectProject(project, openEditor: false, focusGraph: true, filterToProject: true);
            }
        }));
        menu.Items.Add(Item("Wyśrodkuj", () => CenterOnNode(id)));
        menu.Items.Add(Item(ViewModel.IsPinned(id) ? "Odepnij" : "Przypnij", () =>
        {
            if (project is not null)
            {
                ViewModel.SelectProject(project, openEditor: ViewModel.IsEditorOpen, focusGraph: true);
            }

            ViewModel.TogglePinSelected();
        }));
        menu.Items.Add(Item("Przenieś do root", () =>
        {
            if (project is not null)
            {
                ViewModel.SetProjectParent(project, null);
            }
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(project?.IsFolder == true ? "Usuń folder…" : "Usuń projekt…", () =>
        {
            if (project is null)
            {
                return;
            }

            ViewModel.SelectProject(project, openEditor: false, focusGraph: true);
            DeleteProjectRequested?.Invoke(project);
        }));
        return menu;
    }

    private ContextMenu BuildNoteMenu(string id)
    {
        var note = ViewModel!.Notes.FirstOrDefault(n => n.Id == id);
        var menu = new ContextMenu();
        menu.Items.Add(Item("Otwórz / Edytuj", () =>
        {
            if (note is not null)
            {
                ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
            }
        }));
        menu.Items.Add(Item("Wyśrodkuj", () => CenterOnNode(id)));
        menu.Items.Add(Item(ViewModel.IsPinned(id) ? "Odepnij" : "Przypnij", () =>
        {
            if (note is not null)
            {
                ViewModel.SelectNote(note, openEditor: ViewModel.IsEditorOpen, focusGraph: true);
            }

            ViewModel.TogglePinSelected();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Przenieś do kosza", () =>
        {
            if (note is not null)
            {
                ViewModel.SelectNote(note, openEditor: false, focusGraph: true);
            }

            ViewModel.TrashSelectedNote();
        }));
        return menu;
    }

    private ContextMenu BuildCanvasMenu()
    {
        var point = _lastContextCanvasPoint ?? GetViewportCenterInCanvas();
        var menu = new ContextMenu();
        menu.Items.Add(Item("Nowa notatka tutaj", () => ViewModel!.NewNote(point.X, point.Y)));
        menu.Items.Add(Item("Nowy projekt tutaj", () => ViewModel!.NewProject(point.X, point.Y)));
        menu.Items.Add(Item("Nowy folder tutaj", () => ViewModel!.NewFolder(point.X, point.Y)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Pokaż wszystkie projekty", () => ViewModel!.ShowAllProjects()));
        menu.Items.Add(Item("Domyślne powiększenie", () =>
        {
            SetZoom(1);
            ViewModel!.SaveZoom(1);
        }));
        menu.Items.Add(Item("Dopasuj wszystkie elementy", FitToContent));
        if (ViewModel!.SelectedGraphId is not null)
        {
            menu.Items.Add(Item("Wyśrodkuj zaznaczenie", () => CenterOnNode(ViewModel.SelectedGraphId)));
        }

        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            _spaceHeld = true;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            MoveSelection(e.Key);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ViewModel.OpenSelectedGraphNode();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var step = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (Math.Abs(step) < 0.01)
        {
            return;
        }

        var delta = Math.Abs(step) >= 1
            ? (step > 0 ? 0.12 : -0.12)
            : (step > 0 ? 0.08 : -0.08);

        var pointerInScroll = e.GetPosition(GraphScroll);
        var oldZoom = _zoom;
        var offset = GraphScroll.Offset;
        var canvasX = (offset.X + pointerInScroll.X) / Math.Max(oldZoom, 0.01);
        var canvasY = (offset.Y + pointerInScroll.Y) / Math.Max(oldZoom, 0.01);

        ChangeZoom(delta);

        var newOx = (canvasX * _zoom) - pointerInScroll.X;
        var newOy = (canvasY * _zoom) - pointerInScroll.Y;
        GraphScroll.Offset = new Vector(Math.Max(0, newOx), Math.Max(0, newOy));
        e.Handled = true;
    }

    private void ChangeZoom(double delta) => SetZoom(_zoom + delta);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => ChangeZoom(-0.12);

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => ChangeZoom(0.12);

    private void OnZoomResetClick(object? sender, RoutedEventArgs e) => SetZoom(1);

    private void OnFitGraphClick(object? sender, RoutedEventArgs e) => FitToContent();

    public void FitToContent()
    {
        if (_nodes.Count == 0)
        {
            SetZoom(1);
            return;
        }

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var host in _nodes.Values)
        {
            if (host.Tag is not NodeTag tag)
            {
                continue;
            }

            var left = Canvas.GetLeft(host);
            var top = Canvas.GetTop(host);
            minX = Math.Min(minX, left);
            minY = Math.Min(minY, top);
            maxX = Math.Max(maxX, left + tag.Width);
            maxY = Math.Max(maxY, top + tag.Height);
        }

        var viewport = GraphScroll.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0 || minX == double.MaxValue)
        {
            return;
        }

        const double padding = 150;
        var contentWidth = Math.Max(1, maxX - minX);
        var contentHeight = Math.Max(1, maxY - minY);
        var targetZoom = Math.Min(
            (viewport.Width - padding) / contentWidth,
            (viewport.Height - padding) / contentHeight);
        SetZoom(Math.Clamp(targetZoom, 0.25, 1.35));

        var centerX = (minX + maxX) / 2;
        var centerY = (minY + maxY) / 2;
        Dispatcher.UIThread.Post(() =>
        {
            GraphScroll.Offset = new Vector(
                Math.Max(0, (centerX * _zoom) - (GraphScroll.Viewport.Width / 2)),
                Math.Max(0, (centerY * _zoom) - (GraphScroll.Viewport.Height / 2)));
        }, DispatcherPriority.Background);
    }

    private void MoveSelection(Key key)
    {
        if (ViewModel is null)
        {
            return;
        }

        var nodes = _nodes.Select(pair =>
        {
            var pos = LayoutService.Get(ViewModel.State.NodePositions, pair.Key);
            var isProject = pair.Value.Tag is NodeTag tag && tag.IsProject;
            return (pair.Key, pos.X, pos.Y, isProject);
        }).ToList();

        if (nodes.Count == 0)
        {
            return;
        }

        var currentId = ViewModel.SelectedGraphId ?? nodes[0].Key;
        var current = nodes.FirstOrDefault(n => n.Key == currentId);
        if (current.Key is null)
        {
            current = nodes[0];
        }

        var dirX = key == Key.Left ? -1 : key == Key.Right ? 1 : 0;
        var dirY = key == Key.Up ? -1 : key == Key.Down ? 1 : 0;
        string? best = null;
        var bestScore = double.MaxValue;
        foreach (var node in nodes)
        {
            if (node.Key == current.Key)
            {
                continue;
            }

            var vx = node.X - current.X;
            var vy = node.Y - current.Y;
            var dot = (vx * dirX) + (vy * dirY);
            if (dot <= 8)
            {
                continue;
            }

            var dist = Math.Sqrt((vx * vx) + (vy * vy));
            var score = dist / dot;
            if (score < bestScore)
            {
                bestScore = score;
                best = node.Key;
            }
        }

        if (best is null)
        {
            return;
        }

        var chosen = nodes.First(n => n.Key == best);
        ViewModel.SelectGraphNode(chosen.Key, chosen.isProject);
        HighlightSelection();
        CenterOnNode(chosen.Key);
    }

    private void UpdateEdges(string id, double x, double y)
    {
        foreach (var edge in _edges)
        {
            if (!edge.Connects(id))
            {
                continue;
            }

            var from = string.Equals(edge.FromId, id, StringComparison.OrdinalIgnoreCase)
                ? new Point(x, y)
                : CenterOf(edge.FromId);
            var to = string.Equals(edge.ToId, id, StringComparison.OrdinalIgnoreCase)
                ? new Point(x, y)
                : CenterOf(edge.ToId);
            edge.Path.Data = BuildCurve(from, to);
        }
    }

    private Point CenterOf(string id)
    {
        if (_nodes.TryGetValue(id, out var host) && host.Tag is NodeTag tag)
        {
            return new Point(
                Canvas.GetLeft(host) + (tag.Width / 2),
                Canvas.GetTop(host) + (tag.Height / 2));
        }

        if (ViewModel is not null)
        {
            var position = LayoutService.Get(ViewModel.State.NodePositions, id);
            return new Point(position.X, position.Y);
        }

        return default;
    }

    private enum EdgeKind
    {
        Hierarchy,
        ProjectMembership,
        WikiLink
    }

    private sealed record GraphEdge(
        string FromId,
        string ToId,
        EdgeKind Kind,
        Avalonia.Controls.Shapes.Path Path,
        double BaseThickness)
    {
        public bool Connects(string id) =>
            string.Equals(FromId, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ToId, id, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record NodeTag(
        string Id,
        bool IsProject,
        bool IsFolder,
        string Title,
        string Meta,
        double Width,
        double Height,
        Border Card);

    private sealed record ProjectFilterOption(string? ProjectId, string Label)
    {
        public override string ToString() => Label;
    }
}
