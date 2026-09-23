using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using MapaNotatek.Models;

namespace MapaNotatek.Views;

public partial class CommandPaletteDialog : UserControl
{
    private List<AppCommand> _all = [];
    private Window? _host;

    public CommandPaletteDialog()
    {
        InitializeComponent();
    }

    public AppCommand? Chosen { get; private set; }

    public void SetCommands(IEnumerable<AppCommand> commands)
    {
        _all = commands.ToList();
        ApplyFilter();
    }

    public async Task<AppCommand?> ShowAsync(Window owner)
    {
        Chosen = null;
        var closeButton = new Button
        {
            Content = "Anuluj",
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var runButton = new Button
        {
            Content = "Uruchom",
            MinWidth = 80,
            IsDefault = true,
            Margin = new Thickness(0, 12, 8, 0)
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { runButton, closeButton }
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(this);

        _host = new Window
        {
            Title = "Polecenia",
            Width = 480,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root
        };
        closeButton.Click += (_, _) => _host.Close();
        runButton.Click += (_, _) =>
        {
            Confirm();
            _host.Close();
        };
        await _host.ShowDialog(owner);
        return Chosen;
    }

    public void Confirm() => Chosen = SelectedCommand();

    private void OnFilterChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

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

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        Confirm();
        _host?.Close();
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
