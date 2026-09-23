using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using MapaNotatek.Models;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class EditorPanel : UserControl
{
    private bool _suppress;

    public EditorPanel()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel { get; set; }

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
            ProjectNameBox.Text = project.Name;
            ProjectDescriptionBox.Text = project.Description;
            FillChecklist(ProjectChecklistHost, project.Checklist, () => ViewModel.ScheduleSaveProject(project));
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
            NoteTitleBox.Text = note.Title;
            NoteBodyBox.Text = note.Body;
            NoteTagsBox.Text = string.Join(", ", note.Tags);
            NoteDatesText.Text = $"Utworzono: {note.Created:g}\nZmieniono: {note.Modified:g}";
            FillChecklist(NoteChecklistHost, note.Checklist, () => ViewModel.ScheduleSaveNote(note));
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
        note.Tags = FrontMatter.SplitTags(NoteTagsBox.Text);
        ViewModel.ScheduleSaveNote(note);
    }

    private void OnAddProjectTask(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProject is not { } project)
        {
            return;
        }

        project.Checklist.Add(new ChecklistItem { Text = "Nowe zadanie" });
        ViewModel.ScheduleSaveProject(project);
        Refresh();
    }

    private void OnAddNoteTask(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        note.Checklist.Add(new ChecklistItem { Text = "Nowe zadanie" });
        ViewModel.ScheduleSaveNote(note);
        Refresh();
    }

    private void OnArchiveProject(object? sender, RoutedEventArgs e) => ViewModel?.ToggleArchiveSelectedProject();

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

    private void FillChecklist(StackPanel host, List<ChecklistItem> items, Action changed)
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
            delete.Click += (_, _) =>
            {
                items.Remove(item);
                changed();
                Refresh();
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
