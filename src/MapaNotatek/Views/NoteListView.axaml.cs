using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MapaNotatek.Models;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class NoteListView : UserControl
{
    private Point? _dragStart;
    private Note? _dragNote;
    private PointerPressedEventArgs? _dragPress;

    public NoteListView()
    {
        InitializeComponent();
        NotesList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        NotesList.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel);
        NotesList.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            _dragStart = null;
            _dragNote = null;
            _dragPress = null;
        }, RoutingStrategies.Tunnel);
    }

    public MainViewModel? ViewModel { get; set; }

    public event Action<Note>? NoteSelectedOnGraph;

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.Source, this) && NotesList.IsVisible)
        {
            NotesList.Focus();
        }
    }

    public void Bind()
    {
        if (ViewModel is null)
        {
            return;
        }

        NotesList.ItemsSource = ViewModel.VisibleNotes;
        NotesList.ContextMenu = BuildContextMenu();
        var count = ViewModel.VisibleNotes.Count;
        NotesCountText.Text = $"Notatki: {count}";
        EmptyState.IsVisible = count == 0;
        NotesList.IsVisible = count > 0;
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(NotesList).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = e.GetPosition(NotesList);
        _dragNote = NotesList.SelectedItem as Note;
        _dragPress = e;
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is null || _dragNote is null || _dragPress is null ||
            !e.GetCurrentPoint(NotesList).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pos = e.GetPosition(NotesList);
        var dx = pos.X - _dragStart.Value.X;
        var dy = pos.Y - _dragStart.Value.Y;
        if ((dx * dx) + (dy * dy) < 64)
        {
            return;
        }

        var note = _dragNote;
        var press = _dragPress;
        _dragStart = null;
        _dragNote = null;
        _dragPress = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText("note:" + note.Id));
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (NotesList.SelectedItem is not Note note || ViewModel is null)
            {
                return;
            }

            menu.Items.Add(Item("Otwórz", () => ViewModel.SelectNote(note, true, true)));
            menu.Items.Add(Item(ViewModel.IsPinned(note.Id) ? "Odepnij" : "Przypnij", () =>
            {
                ViewModel.SelectNote(note, ViewModel.IsEditorOpen, true);
                ViewModel.TogglePinSelected();
            }));
            menu.Items.Add(Item("Przenieś do kosza", () =>
            {
                ViewModel.SelectNote(note, false, true);
                ViewModel.TrashSelectedNote();
            }));
        };
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is Note note && ViewModel is not null)
        {
            ViewModel.SelectGraphNode(note.Id, isProject: false);
            NoteSelectedOnGraph?.Invoke(note);
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (NotesList.SelectedItem is Note note && ViewModel is not null)
        {
            ViewModel.SelectNote(note, openEditor: true, focusGraph: true);
            NoteSelectedOnGraph?.Invoke(note);
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
