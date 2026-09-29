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
    }

    public MainViewModel? ViewModel { get; set; }

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

        TasksList.ItemsSource = ViewModel.VisibleTasks;
        var count = ViewModel.VisibleTasks.Count;
        TasksCountText.Text = $"Otwarte: {count}";
        EmptyState.IsVisible = count == 0;
        TasksList.IsVisible = count > 0;
    }

    private void OnTaskCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: OpenTask task } card ||
            !e.GetCurrentPoint(card).Properties.IsLeftButtonPressed ||
            IsTaskControl(e.Source as Visual))
        {
            return;
        }

        _dragStart = e.GetPosition(TasksList);
        _dragCandidate = task;
        _dropTarget = null;
        _dropAfter = false;
        _isReordering = false;
    }

    private void OnTaskPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is null || _dragCandidate is null ||
            !e.GetCurrentPoint(TasksList).Properties.IsLeftButtonPressed)
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
            e.Pointer.Capture(TasksList);
        }

        var targetContainer = FindTaskContainerAt(position);
        if (targetContainer?.DataContext is OpenTask target &&
            !ReferenceEquals(_dragCandidate.Item, target.Item))
        {
            _dropTarget = target;
            _dropAfter = e.GetPosition(targetContainer).Y > targetContainer.Bounds.Height / 2;
            TasksList.SelectedItem = target;
        }
        else
        {
            _dropTarget = null;
        }

        e.Handled = true;
    }

    private void OnTaskPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
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
            var moved = ViewModel.VisibleTasks.FirstOrDefault(task => ReferenceEquals(task.Item, movedTask.Item));
            if (moved is not null)
            {
                TasksList.SelectedItem = moved;
                TasksList.ScrollIntoView(moved);
            }
        }

        e.Handled = true;
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
        return FindTaskContainer(TasksList.InputHitTest(position) as Visual);
    }

    private static ListBoxItem? FindTaskContainer(Visual? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is ListBoxItem item && item.DataContext is OpenTask)
            {
                return item;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static bool IsTaskControl(Visual? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Button or CheckBox)
            {
                return true;
            }

            if (current is ListBoxItem)
            {
                return false;
            }

            current = current.GetVisualParent();
        }

        return false;
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
        if (ViewModel is null || TasksList.SelectedItem is not OpenTask task)
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
