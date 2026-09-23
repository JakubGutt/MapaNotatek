using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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

    public void Bind()
    {
        if (ViewModel is null)
        {
            return;
        }

        var rows = ViewModel.VisibleTasks.Select(CreateRow).ToList();
        TasksList.ItemsSource = rows;
    }

    private Border CreateRow(OpenTask task)
    {
        var check = new CheckBox
        {
            IsChecked = task.Item.IsDone,
            MinWidth = 32,
            VerticalAlignment = VerticalAlignment.Top
        };
        check.IsCheckedChanged += (_, _) =>
        {
            if (ViewModel is not null && check.IsChecked != task.Item.IsDone)
            {
                ViewModel.ToggleTask(task);
                Bind();
            }
        };

        var text = new TextBlock { Text = task.Text, TextWrapping = TextWrapping.Wrap };
        var source = new TextBlock { Text = task.SourceTitle, Opacity = 0.6, FontSize = 12 };
        var stack = new StackPanel();
        stack.Children.Add(text);
        stack.Children.Add(source);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        Grid.SetColumn(stack, 1);
        grid.Children.Add(check);
        grid.Children.Add(stack);

        return new Border
        {
            Child = grid,
            Tag = task,
            Padding = new Thickness(4, 6, 4, 6)
        };
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        var task = (TasksList.SelectedItem as Control)?.Tag as OpenTask;
        if (task is null || ViewModel is null)
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
