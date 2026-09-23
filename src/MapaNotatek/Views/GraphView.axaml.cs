using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
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

    public GraphView()
    {
        InitializeComponent();
        ZoomHost.LayoutTransform = _zoomTransform;
    }

    public MainViewModel? ViewModel { get; set; }

    public event Action<double>? ZoomChanged;

    public double ZoomFactor => _zoom;

    public void SetZoom(double factor)
    {
        _zoom = Math.Clamp(factor, 0.25, 3);
        _zoomTransform.ScaleX = _zoom;
        _zoomTransform.ScaleY = _zoom;
        ZoomChanged?.Invoke(_zoom);
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

        foreach (var note in notes)
        {
            foreach (var project in projects.Where(p => LayoutService.NoteLinksTo(note, p)))
            {
                var from = LayoutService.Get(ViewModel.State.NodePositions, project.Id);
                var to = LayoutService.Get(ViewModel.State.NodePositions, note.Id);
                var line = new Line
                {
                    StartPoint = new Point(from.X, from.Y),
                    EndPoint = new Point(to.X, to.Y),
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)),
                    StrokeThickness = 1.5,
                    Tag = $"{project.Id}|{note.Id}"
                };
                _edges.Add(line);
                GraphCanvas.Children.Add(line);
            }
        }

        foreach (var project in projects)
        {
            AddNode(project.Id, project.Name, isProject: true, alwaysShowLabel: true);
        }

        foreach (var note in notes)
        {
            AddNode(note.Id, note.Title, isProject: false, alwaysShowLabel: false);
        }

        HighlightSelection();
    }

    private void AddNode(string id, string title, bool isProject, bool alwaysShowLabel)
    {
        if (ViewModel is null)
        {
            return;
        }

        var position = LayoutService.Get(ViewModel.State.NodePositions, id);
        var size = isProject ? LayoutService.ProjectDiameter : LayoutService.NoteDiameter;
        var fill = isProject
            ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 212))
            : new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
        var stroke = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160));

        var ellipse = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 2
        };

        var label = new TextBlock
        {
            Text = title,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 140,
            FontSize = isProject ? 12 : 11,
            IsVisible = alwaysShowLabel
        };

        var stack = new StackPanel
        {
            Width = 150,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        stack.Children.Add(ellipse);
        stack.Children.Add(label);

        var host = new Border
        {
            Child = stack,
            Background = Brushes.Transparent,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            Tag = new NodeTag(id, isProject, label, alwaysShowLabel, ellipse)
        };
        host.PointerPressed += OnNodePressed;
        host.PointerMoved += OnNodeMoved;
        host.PointerReleased += OnNodeReleased;
        host.PointerCaptureLost += OnNodeCaptureLost;
        host.PointerEntered += OnNodeEntered;
        host.PointerExited += OnNodeExited;
        host.DoubleTapped += OnNodeDoubleTapped;

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
            tag.Ellipse.StrokeThickness = selected ? 4 : 2;
            tag.Ellipse.Stroke = selected
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
        e.Pointer.Capture(host);
        e.Handled = true;
    }

    private void OnNodeMoved(object? sender, PointerEventArgs e)
    {
        if (_dragId is null || sender is not Border host || ViewModel is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(GraphCanvas).Properties.IsLeftButtonPressed)
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

    private void OnCanvasPressed(object? sender, PointerPressedEventArgs e) => Focus();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null)
        {
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

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!PlatformKeys.IsCommand(e.KeyModifiers))
        {
            return;
        }

        ChangeZoom(e.Delta.Y > 0 ? 0.1 : -0.1);
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

    private sealed record NodeTag(string Id, bool IsProject, TextBlock Label, bool AlwaysShowLabel, Ellipse Ellipse);
}
