using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MapaNotatek.Models;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class GraphView : UserControl
{
    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly Dictionary<string, Border> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Line> _edges = [];
    private string? _dragId;
    private Point _dragOffset;
    private Point _pressPoint;
    private bool _dragging;
    private double _zoom = 1;
    private Point? _lastContextCanvasPoint;
    private bool _panning;
    private bool _panMoved;
    private bool _spaceHeld;
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
        LayoutService.ApplyMissingPositions(ViewModel.Projects, ViewModel.Notes, ViewModel.State.NodePositions);

        foreach (var child in projects.Where(p => !string.IsNullOrWhiteSpace(p.ParentId)))
        {
            var parent = projects.FirstOrDefault(p => p.Id == child.ParentId) ??
                         ViewModel.Projects.FirstOrDefault(p => p.Id == child.ParentId);
            if (parent is null)
            {
                continue;
            }

            var from = LayoutService.Get(ViewModel.State.NodePositions, parent.Id);
            var to = LayoutService.Get(ViewModel.State.NodePositions, child.Id);
            AddEdge(parent.Id, child.Id, from, to, Color.FromArgb(255, 100, 149, 237), thickness: 2);
        }

        foreach (var note in notes)
        {
            foreach (var project in projects.Where(p => LayoutService.NoteLinksTo(note, p)))
            {
                var from = LayoutService.Get(ViewModel.State.NodePositions, project.Id);
                var to = LayoutService.Get(ViewModel.State.NodePositions, note.Id);
                AddEdge(project.Id, note.Id, from, to, Color.FromArgb(255, 128, 128, 128), thickness: 1.5);
            }
        }

        foreach (var project in projects)
        {
            AddNode(project.Id, project.IsFolder ? $"📁 {project.Name}" : project.Name, isProject: true, alwaysShowLabel: true, isFolder: project.IsFolder);
        }

        foreach (var note in notes)
        {
            AddNode(note.Id, note.Title, isProject: false, alwaysShowLabel: false, isFolder: false);
        }

        HighlightSelection();
    }

    private void AddEdge(string fromId, string toId, GraphPosition from, GraphPosition to, Color color, double thickness)
    {
        var line = new Line
        {
            StartPoint = new Point(from.X, from.Y),
            EndPoint = new Point(to.X, to.Y),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            Tag = $"{fromId}|{toId}"
        };
        _edges.Add(line);
        GraphCanvas.Children.Add(line);
    }

    private void AddNode(string id, string title, bool isProject, bool alwaysShowLabel, bool isFolder)
    {
        if (ViewModel is null)
        {
            return;
        }

        var position = LayoutService.Get(ViewModel.State.NodePositions, id);
        var size = isProject ? LayoutService.ProjectDiameter : LayoutService.NoteDiameter;
        var fill = isFolder
            ? new SolidColorBrush(Color.FromArgb(255, 196, 154, 70))
            : isProject
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 212))
                : new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
        var stroke = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160));

        Shape shape = isFolder
            ? new Avalonia.Controls.Shapes.Rectangle
            {
                Width = size + 10,
                Height = size - 4,
                RadiusX = 6,
                RadiusY = 6,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 2
            }
            : new Ellipse
            {
                Width = size,
                Height = size,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 2
            };

        var label = new TextBlock
        {
            Text = isFolder ? title.Replace("📁 ", string.Empty) : title,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 140,
            FontSize = isProject ? 12 : 11,
            FontWeight = isFolder ? FontWeight.SemiBold : FontWeight.Normal,
            IsVisible = alwaysShowLabel
        };

        var stack = new StackPanel
        {
            Width = 150,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        if (isFolder)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "📁",
                FontSize = 16,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, -2)
            });
        }

        stack.Children.Add(shape);
        stack.Children.Add(label);

        var host = new Border
        {
            Child = stack,
            Background = Brushes.Transparent,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            Tag = new NodeTag(id, isProject, label, alwaysShowLabel, shape, isFolder)
        };
        host.PointerPressed += OnNodePressed;
        host.PointerMoved += OnNodeMoved;
        host.PointerReleased += OnNodeReleased;
        host.PointerCaptureLost += OnNodeCaptureLost;
        host.PointerEntered += OnNodeEntered;
        host.PointerExited += OnNodeExited;
        host.DoubleTapped += OnNodeDoubleTapped;
        host.ContextRequested += OnNodeContextRequested;

        Canvas.SetLeft(host, position.X - 79);
        Canvas.SetTop(host, position.Y - (size / 2) - 4);
        GraphCanvas.Children.Add(host);
        _nodes[id] = host;
    }

    private void HighlightSelection()
    {
        if (ViewModel is null)
        {
            return;
        }

        foreach (var pair in _nodes)
        {
            if (pair.Value.Tag is not NodeTag tag)
            {
                continue;
            }

            var selected = string.Equals(pair.Key, ViewModel.SelectedGraphId, StringComparison.OrdinalIgnoreCase);
            tag.Shape.StrokeThickness = selected ? 4 : 2;
            tag.Shape.Stroke = selected
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 212))
                : new SolidColorBrush(Color.FromArgb(255, 160, 160, 160));
            if (!tag.AlwaysShowLabel)
            {
                tag.Label.IsVisible = selected;
            }
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
            var size = tag.IsProject ? LayoutService.ProjectDiameter : LayoutService.NoteDiameter;
            UpdateEdges(tag.Id, left + 79, top + (size / 2) + 4);
        }

        e.Handled = true;
    }

    private void OnNodeReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishPointer(sender, openOnClick: true);
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
            var size = tag.IsProject ? LayoutService.ProjectDiameter : LayoutService.NoteDiameter;
            var x = Canvas.GetLeft(host) + 79;
            var y = Canvas.GetTop(host) + (size / 2) + 4;
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

    private void OnNodeEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border { Tag: NodeTag tag } && !tag.AlwaysShowLabel)
        {
            tag.Label.IsVisible = true;
        }
    }

    private void OnNodeExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border { Tag: NodeTag tag } && !tag.AlwaysShowLabel)
        {
            var selected = ViewModel is not null &&
                           string.Equals(tag.Id, ViewModel.SelectedGraphId, StringComparison.OrdinalIgnoreCase);
            tag.Label.IsVisible = selected;
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
        EndPan(clearFilterOnClick: true);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnCanvasCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        EndPan(clearFilterOnClick: false);

    private void BeginPan(Point pointerInScroll)
    {
        _panning = true;
        _panMoved = false;
        _panPointerStart = pointerInScroll;
        _panScrollStart = GraphScroll.Offset;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void EndPan(bool clearFilterOnClick)
    {
        if (!_panning)
        {
            return;
        }

        var moved = _panMoved;
        _panning = false;
        _panMoved = false;
        Cursor = Cursor.Default;
        if (!moved && clearFilterOnClick && ViewModel is not null)
        {
            ViewModel.ShowAllProjects();
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
        foreach (var line in _edges)
        {
            var parts = Convert.ToString(line.Tag)?.Split('|');
            if (parts is not { Length: 2 })
            {
                continue;
            }

            if (parts[0] == id)
            {
                line.StartPoint = new Point(x, y);
            }

            if (parts[1] == id)
            {
                line.EndPoint = new Point(x, y);
            }
        }
    }

    private sealed record NodeTag(string Id, bool IsProject, TextBlock Label, bool AlwaysShowLabel, Shape Shape, bool IsFolder);
}
