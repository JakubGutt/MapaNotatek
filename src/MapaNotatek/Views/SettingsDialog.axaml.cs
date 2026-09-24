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
            try
            {
                ViewModel.ChangeDataFolder(folders[0].Path.LocalPath);
                Bind();
            }
            catch (Exception ex)
            {
                ViewModel.StatusText = "Nie otwarto biblioteki: " + ex.Message;
                await ShowMessageAsync("Nie można otworzyć biblioteki", ex.Message);
            }
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

        if (!ViewModel.FlushPendingSaves())
        {
            await ShowMessageAsync(
                "Kopia nie została utworzona",
                "Nie udało się zapisać wszystkich bieżących zmian. Sprawdź uprawnienia i wolne miejsce, a następnie spróbuj ponownie.");
            return;
        }

        var dest = Path.Combine(folders[0].Path.LocalPath, $"MapaNotatek-{DateTime.Now:yyyyMMdd-HHmmss}");
        try
        {
            SetBusy(true);
            ViewModel.StatusText = "Tworzenie i sprawdzanie kopii…";
            await Task.Run(() => BackupService.ExportCopy(ViewModel.DataFolder, dest));
            ViewModel.StatusText = $"Utworzono zweryfikowaną kopię: {dest}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = "Nie udało się utworzyć kopii: " + ex.Message;
            await ShowMessageAsync("Kopia nie została utworzona", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is null)
        {
            return;
        }

        var backups = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz kopię MapaNotatek",
            AllowMultiple = false
        });
        if (backups.Count == 0)
        {
            return;
        }

        var backupPath = backups[0].Path.LocalPath;
        BackupValidationResult validation;
        try
        {
            SetBusy(true);
            ViewModel.StatusText = "Sprawdzanie kopii…";
            validation = await Task.Run(() => BackupService.Validate(backupPath));
        }
        finally
        {
            SetBusy(false);
        }

        if (!validation.IsValid)
        {
            var details = validation.Errors.Count == 0
                ? "Kopia nie przeszła weryfikacji."
                : string.Join(Environment.NewLine, validation.Errors.Take(8));
            ViewModel.StatusText = "Wybrana kopia jest uszkodzona";
            await ShowMessageAsync("Nieprawidłowa kopia", details);
            return;
        }

        var destinations = await HostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz nowy albo pusty folder docelowy",
            AllowMultiple = false
        });
        if (destinations.Count == 0)
        {
            return;
        }

        var destination = destinations[0].Path.LocalPath;
        try
        {
            SetBusy(true);
            ViewModel.StatusText = "Przywracanie zweryfikowanej kopii…";
            await Task.Run(() => BackupService.RestoreCopy(backupPath, destination));
            ViewModel.ChangeDataFolder(destination, loadLibraryState: true);
            Bind();
            ViewModel.StatusText = "Kopia została przywrócona i otwarta";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = "Nie udało się przywrócić kopii: " + ex.Message;
            await ShowMessageAsync("Przywracanie nie powiodło się", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnExportDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || HostWindow is null)
        {
            return;
        }

        var file = await HostWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz lokalny raport diagnostyczny",
            SuggestedFileName = $"MapaNotatek-diagnostyka-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExtension = "txt",
            FileTypeChoices =
            [
                new FilePickerFileType("Plik tekstowy")
                {
                    Patterns = ["*.txt"],
                    MimeTypes = ["text/plain"]
                }
            ]
        });
        if (file is null)
        {
            return;
        }

        try
        {
            var report = LocalDiagnosticsService.BuildReport(
                ViewModel.DataFolder,
                ViewModel.Notes.Count,
                ViewModel.Projects.Count,
                ViewModel.PendingSaveCount,
                ViewModel.StorageIssues,
                ViewModel.LastSaveErrorMessage);
            await Task.Run(() => LocalDiagnosticsService.WriteReport(file.Path.LocalPath, report));
            ViewModel.StatusText = "Zapisano lokalny raport diagnostyczny";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = "Nie udało się zapisać diagnostyki: " + ex.Message;
            await ShowMessageAsync("Nie udało się zapisać raportu", ex.Message);
        }
    }

    private void SetBusy(bool busy)
    {
        SettingsRoot.IsEnabled = !busy;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window ?? HostWindow;
        if (owner is null)
        {
            return;
        }

        var close = new Button
        {
            Content = "OK",
            IsDefault = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            MinWidth = 88
        };
        var dialog = new Window
        {
            Title = title,
            Width = 480,
            MinHeight = 180,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(18),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
