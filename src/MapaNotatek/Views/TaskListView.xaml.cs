using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MapaNotatek.Models;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public sealed partial class TaskListView : UserControl
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

        TasksList.Items.Clear();
        foreach (var task in ViewModel.VisibleTasks)
        {
            TasksList.Items.Add(CreateRow(task));
        }
    }

    private Grid CreateRow(OpenTask task)
    {
        var check = new CheckBox
        {
            IsChecked = task.Item.IsDone,
            MinWidth = 32,
            VerticalAlignment = VerticalAlignment.Top
        };
        check.Click += (_, _) =>
        {
            if (ViewModel is not null)
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

        var grid = new Grid { Tag = task, Padding = new Thickness(4, 6, 4, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(stack, 1);
        grid.Children.Add(check);
        grid.Children.Add(stack);
        return grid;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        var task = (e.ClickedItem as FrameworkElement)?.Tag as OpenTask;
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
