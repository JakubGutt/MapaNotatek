using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MapaNotatek.Models;

namespace MapaNotatek.Views;

public sealed partial class CommandPaletteDialog : UserControl
{
    private List<AppCommand> _all = [];

    public CommandPaletteDialog()
    {
        InitializeComponent();
        CommandsList.SelectionMode = ListViewSelectionMode.Single;
    }

    public AppCommand? Chosen { get; private set; }
    public Action? CloseRequested { get; set; }

    public void SetCommands(IEnumerable<AppCommand> commands)
    {
        _all = commands.ToList();
        ApplyFilter();
    }

    public void Confirm() => Chosen = SelectedCommand();

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
        Confirm();
        CloseRequested?.Invoke();
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
