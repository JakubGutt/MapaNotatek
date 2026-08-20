using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public sealed partial class SettingsDialog : UserControl
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

    private async void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is null)
        {
            return;
        }

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(HostWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.ChangeDataFolder(folder.Path);
            Bind();
        }
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is null)
        {
            return;
        }

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(HostWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        var dest = Path.Combine(folder.Path, $"MapaNotatek-{DateTime.Now:yyyyMMdd-HHmmss}");
        BackupService.ExportCopy(ViewModel.DataFolder, dest);
        ViewModel.StatusText = $"Wyeksportowano kopię do {dest}";
    }
}
