using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using MapaNotatek.Models;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public sealed partial class NoteListView : UserControl
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

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Note note && ViewModel is not null)
        {
            ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter && NotesList.SelectedItem is Note note)
        {
            ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Delete && NotesList.SelectedItem is Note toDelete)
        {
            ViewModel.SelectNote(toDelete, openEditor: false, focusGraph: true);
            ViewModel.TrashSelectedNote();
            e.Handled = true;
        }
    }
}
