using Avalonia.Controls;
using Avalonia.Input;
using MapaNotatek.Models;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class NoteListView : UserControl
{
    public NoteListView()
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

        NotesList.ItemsSource = ViewModel.VisibleNotes;
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (NotesList.SelectedItem is Note note && ViewModel is not null)
        {
            ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (e.Key == Key.Enter && NotesList.SelectedItem is Note note)
        {
            ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
            e.Handled = true;
        }
        else if ((e.Key is Key.Delete or Key.Back) && NotesList.SelectedItem is Note toDelete)
        {
            ViewModel.SelectNote(toDelete, openEditor: false, focusGraph: true);
            ViewModel.TrashSelectedNote();
            e.Handled = true;
        }
    }
}
