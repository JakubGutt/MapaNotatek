using Avalonia.Controls;
using MapaNotatek.Models;
using MapaNotatek.Services;

namespace MapaNotatek.Views;

public partial class ShortcutsDialog : UserControl
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        SetShortcuts(ShortcutCatalog.Contextual);
    }

    public void SetShortcuts(IEnumerable<ShortcutInfo> shortcuts) =>
        ShortcutsList.ItemsSource = shortcuts.Select(s => $"{s.Keys}  {s.Action}").ToList();
}
