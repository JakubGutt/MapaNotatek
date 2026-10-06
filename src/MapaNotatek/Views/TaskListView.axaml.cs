using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MapaNotatek.Models;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class TaskListView : UserControl
{
    private Point? _dragStart;
    private OpenTask? _dragCandidate;
    private OpenTask? _dropTarget;
    private bool _dropAfter;
    private bool _isReordering;
    private bool _bindingFilter;
    private string? _selectedSourceId;
    private List<OpenTask> _displayedTasks = [];

    public TaskListView()
    {
        InitializeComponent();
        TasksList.AddHandler(
            PointerMovedEvent,
            OnTaskPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        TasksList.AddHandler(
            PointerReleasedEvent,
            OnTaskPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        TasksList.AddHandler(
            PointerCaptureLostEvent,
            OnTaskPointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public MainViewModel? ViewModel { get; set; }

    public event Action<int, int>? TaskCountsChanged;

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.Source, this) && TasksList.IsVisible)
        {
            TasksList.Focus();
        }
    }

    public void Bind()
    {
        if (ViewModel is null)
        {
            return;
        }

        var sources = BuildSourceOptions();
        _bindingFilter = true;
        SourceFilter.ItemsSource = sources;
        SourceFilter.SelectedItem = sources.FirstOrDefault(option =>
            string.Equals(option.SourceId, _selectedSourceId, StringComparison.OrdinalIgnoreCase)) ?? sources[0];
        _selectedSourceId = (SourceFilter.SelectedItem as TaskSourceOption)?.SourceId;
        _bindingFilter = false;

        _displayedTasks = ViewModel.VisibleTasks
            .Where(task => _selectedSourceId is null ||
                           string.Equals(task.SourceId, _selectedSourceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        TasksList.ItemsSource = _displayedTasks;
        var openCount = _displayedTasks.Count;
        var totalCount = _selectedSourceId is null
            ? ViewModel.VisibleProjects.Sum(project => project.Checklist.Count) +
              ViewModel.VisibleNotes.Sum(note => note.Checklist.Count)
            : ViewModel.VisibleProjects.FirstOrDefault(project =>
                  string.Equals(project.Id, _selectedSourceId, StringComparison.OrdinalIgnoreCase))?.Checklist.Count ??
              ViewModel.VisibleNotes.FirstOrDefault(note =>
                  string.Equals(note.Id, _selectedSourceId, StringComparison.OrdinalIgnoreCase))?.Checklist.Count ?? 0;
        TasksCountText.Text = $"Otwarte: {openCount} · Wszystkie: {totalCount}";
        EmptyState.IsVisible = openCount == 0;
        TasksList.IsVisible = openCount > 0;
        TaskCountsChanged?.Invoke(openCount, totalCount);
    }

    private List<TaskSourceOption> BuildSourceOptions()
    {
        var options = new List<TaskSourceOption>
        {
            new(null, $"Wszystkie źródła ({ViewModel!.VisibleTasks.Count})")
        };
        options.AddRange(ViewModel.VisibleProjects
            .Where(project => project.Checklist.Count > 0)
            .Select(project => new TaskSourceOption(
                project.Id,
                $"{project.ItemType.Label()} · {project.Name} ({project.Checklist.Count(item => !item.IsDone)})")));
        options.AddRange(ViewModel.VisibleNotes
            .Where(note => note.Checklist.Count > 0)
            .Select(note => new TaskSourceOption(
                note.Id,
                $"Notatka · {note.Title} ({note.Checklist.Count(item => !item.IsDone)})")));
        return [options[0], .. options.Skip(1).OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)];
    }

    private void OnSourceFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_bindingFilter || SourceFilter.SelectedItem is not TaskSourceOption option)
        {
            return;
        }

        _selectedSourceId = option.SourceId;
        Bind();
    }

    private void OnTaskCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: OpenTask task } handle ||
            !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = e.GetPosition(TasksList);
        _dragCandidate = task;
        _dropTarget = null;
        _dropAfter = false;
        _isReordering = false;
        TasksList.SelectedItem = task;
        e.Pointer.Capture(TasksList);
        e.Handled = true;
    }

    private void OnTaskPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is null || _dragCandidate is null)
        {
            return;
        }

        var position = e.GetPosition(TasksList);
        var dx = position.X - _dragStart.Value.X;
        var dy = position.Y - _dragStart.Value.Y;
        if (!_isReordering && (dx * dx) + (dy * dy) < 36)
        {
            return;
        }

        if (!_isReordering)
        {
            _isReordering = true;
        }

        UpdateDropTarget(position);

        e.Handled = true;
    }

    private void OnTaskPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragCandidate is null)
        {
            return;
        }

        ClearDragState();
    }

    private void OnTaskPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // The list receives tunneled releases from every child control. Releasing
        // capture here when no task drag is active cancels the native CheckBox and
        // Button click sequences (task completion and the people picker).
        if (_dragStart is null || _dragCandidate is null)
        {
            return;
        }

        if (_dragStart is { } start && _dragCandidate is not null)
        {
            var position = e.GetPosition(TasksList);
            var dx = position.X - start.X;
            var dy = position.Y - start.Y;
            if ((dx * dx) + (dy * dy) >= 36)
            {
                _isReordering = true;
                UpdateDropTarget(position);
            }
        }

        var movedTask = _dragCandidate;
        var targetTask = _dropTarget;
        var placeAfter = _dropAfter;
        var shouldMove = _isReordering && movedTask is not null && targetTask is not null && ViewModel is not null;
        e.Pointer.Capture(null);
        ClearDragState();
        if (!shouldMove || movedTask is null || targetTask is null || ViewModel is null)
        {
            return;
        }

        if (ViewModel.MoveTask(movedTask, targetTask, placeAfter))
        {
            Bind();
            var moved = _displayedTasks.FirstOrDefault(task => ReferenceEquals(task.Item, movedTask.Item));
            if (moved is not null)
            {
                TasksList.SelectedItem = moved;
                TasksList.ScrollIntoView(moved);
            }
        }

        e.Handled = true;
    }

    private void UpdateDropTarget(Point position)
    {
        var targetContainer = FindTaskContainerAt(position);
        if (targetContainer?.DataContext is not OpenTask target ||
            _dragCandidate is null ||
            ReferenceEquals(_dragCandidate.Item, target.Item))
        {
            _dropTarget = null;
            return;
        }

        _dropTarget = target;
        var movedIndex = _displayedTasks.IndexOf(_dragCandidate);
        var targetIndex = _displayedTasks.IndexOf(target);
        _dropAfter = movedIndex >= 0 && targetIndex >= 0
            ? targetIndex > movedIndex
            : position.Y > targetContainer.Bounds.Center.Y;
        TasksList.SelectedItem = target;
    }

    private void ClearDragState()
    {
        _dragStart = null;
        _dragCandidate = null;
        _dropTarget = null;
        _dropAfter = false;
        _isReordering = false;
    }

    private ListBoxItem? FindTaskContainerAt(Point position)
    {
        return TasksList.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Select(item => new
            {
                Item = item,
                TopLeft = item.TranslatePoint(new Point(0, 0), TasksList)
            })
            .Where(candidate => candidate.TopLeft.HasValue)
            .OrderBy(candidate => Math.Abs(
                position.Y - (candidate.TopLeft!.Value.Y + candidate.Item.Bounds.Height / 2)))
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    private sealed record TaskSourceOption(string? SourceId, string Label)
    {
        public override string ToString() => Label;
    }

    private void OnTaskChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox check ||
            check.DataContext is not OpenTask task ||
            ViewModel is null ||
            check.IsChecked == task.Item.IsDone)
        {
            return;
        }

        CompleteTask(task);
    }

    private void OnTaskPeopleButtonLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OpenTask task } button && ViewModel is not null)
        {
            PersonPickerMenu.UpdateButton(button, ViewModel.People, task.Item.People);
        }
    }

    private void OnTaskPeopleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OpenTask task } button && ViewModel is not null)
        {
            PersonPickerMenu.Show(
                button,
                ViewModel.People,
                task.Item.People,
                selected => ViewModel.UpdateTaskPeople(task, string.Join(",", selected)));
        }
    }

    private void OnTaskLinksButtonLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OpenTask task } button)
        {
            ExternalLinksEditor.UpdateCompactButton(button, task.Item.ExternalLinks);
        }
    }

    private void OnTaskLinksClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OpenTask task } button && ViewModel is not null)
        {
            ExternalLinksEditor.ShowCompactMenu(
                button,
                task.Item.ExternalLinks,
                () => ViewModel.NotifyTaskLinksChanged(task),
                status => ViewModel.StatusText = status);
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        var task = TasksList.SelectedItem as OpenTask;
        if (task is null || ViewModel is null)
        {
            return;
        }

        OpenSource(task);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is Button or CheckBox ||
            ViewModel is null ||
            TasksList.SelectedItem is not OpenTask task)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            OpenSource(task);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && e.Source is not CheckBox)
        {
            CompleteTask(task);
            e.Handled = true;
        }
    }

    private void CompleteTask(OpenTask task)
    {
        if (ViewModel is null)
        {
            return;
        }

        var previousIndex = TasksList.SelectedIndex;
        if (previousIndex < 0)
        {
            previousIndex = ViewModel.VisibleTasks.IndexOf(task);
        }

        ViewModel.ToggleTask(task);
        Bind();
        if (TasksList.ItemCount > 0)
        {
            TasksList.SelectedIndex = Math.Clamp(previousIndex, 0, TasksList.ItemCount - 1);
            TasksList.Focus();
        }
        else
        {
            Focus();
        }
    }

    private void OpenSource(OpenTask task)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (task.IsProject)
        {
            var project = ViewModel.Projects.FirstOrDefault(p => p.Id == task.SourceId);
            if (project is not null)
            {
                ViewModel.SelectProject(project, openEditor: true, focusGraph: true);
            }
        }
        else
        {
            var note = ViewModel.Notes.FirstOrDefault(n => n.Id == task.SourceId);
            if (note is not null)
            {
                ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
            }
        }
    }
}
