using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MapaNotatek.Models;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class TaskListView : UserControl
{
    private Point? _dragStart;
    private OpenTask? _dragCandidate;
    private OpenTask? _activeDragTask;
    private PointerPressedEventArgs? _dragPress;

    public TaskListView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(TasksList, true);
        DragDrop.AddDragOverHandler(TasksList, OnTaskDragOver);
        DragDrop.AddDropHandler(TasksList, OnTaskDrop);
        TasksList.AddHandler(PointerPressedEvent, OnTaskPointerPressed, RoutingStrategies.Tunnel);
        TasksList.AddHandler(PointerMovedEvent, OnTaskPointerMoved, RoutingStrategies.Tunnel);
        TasksList.AddHandler(PointerReleasedEvent, (_, _) => ClearDragCandidate(), RoutingStrategies.Tunnel);
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

    private void OnTaskPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(TasksList).Properties.IsLeftButtonPressed || IsTaskControl(e.Source as Control))
        {
            return;
        }

        var container = FindTaskContainer(e.Source as Control);
        if (container?.DataContext is not OpenTask task)
        {
            return;
        }

        _dragStart = e.GetPosition(TasksList);
        _dragCandidate = task;
        _dragPress = e;
    }

    private async void OnTaskPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is null || _dragCandidate is null || _dragPress is null ||
            !e.GetCurrentPoint(TasksList).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(TasksList);
        var dx = position.X - _dragStart.Value.X;
        var dy = position.Y - _dragStart.Value.Y;
        if ((dx * dx) + (dy * dy) < 64)
        {
            return;
        }

        _activeDragTask = _dragCandidate;
        var press = _dragPress;
        ClearDragCandidate();
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText("task-priority"));
        try
        {
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
        }
        finally
        {
            _activeDragTask = null;
        }
    }

    private void OnTaskDragOver(object? sender, DragEventArgs e)
    {
        var target = FindTaskContainer(e.Source as Control)?.DataContext as OpenTask;
        var isTaskMove = string.Equals(e.DataTransfer.TryGetText(), "task-priority", StringComparison.Ordinal);
        var canDrop = isTaskMove && _activeDragTask is not null && target is not null &&
                      !ReferenceEquals(_activeDragTask.Item, target.Item);
        e.DragEffects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        if (canDrop)
        {
            TasksList.SelectedItem = target;
        }
    }

    private void OnTaskDrop(object? sender, DragEventArgs e)
    {
        var container = FindTaskContainer(e.Source as Control);
        if (_activeDragTask is null ||
            container?.DataContext is not OpenTask target ||
            ViewModel is null ||
            ReferenceEquals(_activeDragTask.Item, target.Item))
        {
            return;
        }

        var placeAfter = e.GetPosition(container).Y > container.Bounds.Height / 2;
        if (ViewModel.MoveTask(_activeDragTask, target, placeAfter))
        {
            Bind();
            var moved = ViewModel.VisibleTasks.FirstOrDefault(task => ReferenceEquals(task.Item, _activeDragTask.Item));
            if (moved is not null)
            {
                TasksList.SelectedItem = moved;
                TasksList.ScrollIntoView(moved);
            }
        }

        e.Handled = true;
    }

    private void ClearDragCandidate()
    {
        _dragStart = null;
        _dragCandidate = null;
        _dragPress = null;
    }

    private static ListBoxItem? FindTaskContainer(Control? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is ListBoxItem item && item.DataContext is OpenTask)
            {
                return item;
            }

            current = current.Parent as Control;
        }

        return null;
    }

    private static bool IsTaskControl(Control? source)
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

            current = current.Parent as Control;
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
