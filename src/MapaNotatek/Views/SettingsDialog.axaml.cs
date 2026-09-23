using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class SettingsDialog : UserControl
{
    public SettingsDialog()
    {
        InitializeComponent();
        ShortcutsList.ItemsSource = ShortcutCatalog.All.Select(s => $"{s.Keys} — {s.Action}").ToList();
    }

    public MainViewModel? ViewModel { get; set; }
    public Window? HostWindow { get; set; }

    public void Bind()
    {
        if (ViewModel is null)
        {
            return;
        }

        DataFolderBox.Text = ViewModel.DataFolder;
        ThemeText.Text = $"Motyw: {ViewModel.ThemeName}";
    }

    private async void OnChangeFolder(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is null)
        {
            return;
        }

        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder danych",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            ViewModel.ChangeDataFolder(folders[0].Path.LocalPath);
            Bind();
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is null)
        {
            return;
        }

        var folders = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder na kopię",
            AllowMultiple = false
        });
        if (folders.Count == 0)
        {
            return;
        }

        var dest = Path.Combine(folders[0].Path.LocalPath, $"MapaNotatek-{DateTime.Now:yyyyMMdd-HHmmss}");
        BackupService.ExportCopy(ViewModel.DataFolder, dest);
        ViewModel.StatusText = $"Wyeksportowano kopię do {dest}";
    }
}
