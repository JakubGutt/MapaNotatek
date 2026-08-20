using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MapaNotatek.Models;

namespace MapaNotatek.Views;

public sealed partial class CommandPaletteDialog : ContentDialog
{
    private List<AppCommand> _all = [];

    public CommandPaletteDialog()
    {
        InitializeComponent();
        CommandsList.SelectionMode = ListViewSelectionMode.Single;
        PrimaryButtonClick += OnPrimary;
    }

    public AppCommand? Chosen { get; private set; }

    public void SetCommands(IEnumerable<AppCommand> commands)
    {
        _all = commands.ToList();
        ApplyFilter();
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = FilterBox.Text?.Trim() ?? string.Empty;
        var items = _all
            .Where(c => string.IsNullOrEmpty(query) ||
                        c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        c.Shortcut.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(c => $"{c.Name}    {c.Shortcut}")
            .ToList();
        CommandsList.ItemsSource = items;
        if (items.Count > 0)
        {
            CommandsList.SelectedIndex = 0;
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        Chosen = SelectedCommand();
        Hide();
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Chosen = SelectedCommand();
    }

    private AppCommand? SelectedCommand()
    {
        if (CommandsList.SelectedItem is not string text)
        {
            return null;
        }

        return _all.FirstOrDefault(c => text.StartsWith(c.Name, StringComparison.Ordinal));
    }
}
