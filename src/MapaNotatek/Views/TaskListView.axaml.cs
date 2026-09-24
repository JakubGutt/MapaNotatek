using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MapaNotatek.Models;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class TaskListView : UserControl
{
    public TaskListView()
    {
        InitializeComponent();
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
