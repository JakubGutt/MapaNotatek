using Avalonia.Controls;
using MapaNotatek.Services;

namespace MapaNotatek.Views;

public partial class ShortcutsDialog : UserControl
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        ShortcutsList.ItemsSource = ShortcutCatalog.All.Select(s => $"{s.Keys}  {s.Action}").ToList();
    }
}
