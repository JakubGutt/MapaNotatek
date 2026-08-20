using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MapaNotatek.Models;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public sealed partial class EditorPanel : UserControl
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
        EmptyPanel.Visibility = Visibility.Collapsed;
        ProjectPanel.Visibility = Visibility.Collapsed;
        NotePanel.Visibility = Visibility.Collapsed;

        if (!ViewModel.IsEditorOpen)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            _suppress = false;
            return;
        }

        if (ViewModel.SelectedProject is { } project)
        {
            ProjectPanel.Visibility = Visibility.Visible;
            ProjectNameBox.Text = project.Name;
            ProjectDescriptionBox.Text = project.Description;
            FillChecklist(ProjectChecklistHost, project.Checklist, () => ViewModel.ScheduleSaveProject(project));
            RelatedNotesList.Items.Clear();
            foreach (var note in ViewModel.RelatedNotes)
            {
                RelatedNotesList.Items.Add(note.Title);
            }

            ProjectOpenTasksHost.Children.Clear();
            foreach (var task in ViewModel.ProjectNoteTasks)
            {
                ProjectOpenTasksHost.Children.Add(CreateOpenTaskRow(task));
            }
        }
        else if (ViewModel.SelectedNote is { } note)
        {
            NotePanel.Visibility = Visibility.Visible;
            NoteTitleBox.Text = note.Title;
            NoteBodyBox.Text = note.Body;
            NoteTagsBox.Text = string.Join(", ", note.Tags);
            NoteDatesText.Text = $"Utworzono: {note.Created:g}\nZmieniono: {note.Modified:g}";
            FillChecklist(NoteChecklistHost, note.Checklist, () => ViewModel.ScheduleSaveNote(note));
        }
        else
        {
            EmptyPanel.Visibility = Visibility.Visible;
        }

        _suppress = false;
    }

    public UIElement DefaultFocusTarget()
    {
        if (ProjectPanel.Visibility == Visibility.Visible)
        {
            return ProjectNameBox;
        }

        if (NotePanel.Visibility == Visibility.Visible)
        {
            return NoteTitleBox;
        }

        return this;
    }

    private void OnProjectChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || ViewModel?.SelectedProject is not { } project)
        {
            return;
        }

        project.Name = ProjectNameBox.Text;
        project.Description = ProjectDescriptionBox.Text;
        ViewModel.ScheduleSaveProject(project);
    }

    private void OnNoteChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        note.Title = NoteTitleBox.Text;
        note.Body = NoteBodyBox.Text;
        note.Tags = FrontMatter.SplitTags(NoteTagsBox.Text);
        ViewModel.ScheduleSaveNote(note);
    }

    private void OnAddProjectTask(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProject is not { } project)
        {
            return;
        }

        project.Checklist.Add(new ChecklistItem { Text = "Nowe zadanie" });
        ViewModel.ScheduleSaveProject(project);
        Refresh();
    }

    private void OnAddNoteTask(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNote is not { } note)
        {
            return;
        }

        note.Checklist.Add(new ChecklistItem { Text = "Nowe zadanie" });
        ViewModel.ScheduleSaveNote(note);
        Refresh();
    }

    private void OnArchiveProject(object sender, RoutedEventArgs e) => ViewModel?.ToggleArchiveSelectedProject();

    private void OnTrashNote(object sender, RoutedEventArgs e) => ViewModel?.TrashSelectedNote();

    private void OnRelatedNoteClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel is null || e.ClickedItem is not string title)
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

            check.Click += (_, _) =>
            {
                item.IsDone = check.IsChecked == true;
                changed();
            };
            text.TextChanged += (_, _) =>
            {
                item.Text = text.Text;
                changed();
            };
            delete.Click += (_, _) =>
            {
                items.Remove(item);
                changed();
                Refresh();
            };

            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 1);
            Grid.SetColumn(delete, 2);
            row.Children.Add(check);
            row.Children.Add(text);
            row.Children.Add(delete);
            host.Children.Add(row);
        }
    }

    private UIElement CreateOpenTaskRow(OpenTask task)
    {
        var check = new CheckBox { IsChecked = false, Content = $"{task.Text} ({task.SourceTitle})", MinWidth = 32 };
        check.Click += (_, _) =>
        {
            ViewModel?.ToggleTask(task);
            Refresh();
        };
        return check;
    }
}
