using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MapaNotatek.Models;
using MapaNotatek.Services;

namespace MapaNotatek.Views;

public static class ExternalLinksEditor
{
    public static void Bind(
        StackPanel host,
        IList<ExternalLink> links,
        Action changed,
        Action<string>? statusChanged = null)
    {
        host.Children.Clear();
        if (links.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "Brak linków. Adresy pozostają lokalne i można je tylko kopiować.",
                FontSize = 11,
                Opacity = 0.62,
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var link in links.ToList())
        {
            var label = new TextBlock
            {
                Text = link.Label,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var url = new TextBlock
            {
                Text = link.Url,
                FontSize = 11,
                Opacity = 0.64,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(label);
            text.Children.Add(url);

            var copy = new Button { Content = "Kopiuj", MinWidth = 66, Padding = new Thickness(8, 4) };
            copy.Click += async (_, _) =>
            {
                await CopyAsync(copy, link.Url);
                statusChanged?.Invoke($"Skopiowano link: {link.Label}");
            };
            var edit = new Button { Content = "Edytuj", MinWidth = 62, Padding = new Thickness(8, 4) };
            edit.Click += async (_, _) =>
            {
                if (await EditAsync(edit, link))
                {
                    changed();
                    Bind(host, links, changed, statusChanged);
                }
            };
            var remove = new Button { Content = "Usuń", MinWidth = 56, Padding = new Thickness(8, 4) };
            remove.Click += (_, _) =>
            {
                links.Remove(link);
                changed();
                Bind(host, links, changed, statusChanged);
            };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            actions.Children.Add(copy);
            actions.Children.Add(edit);
            actions.Children.Add(remove);
            var row = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                RowSpacing = 7,
                Margin = new Thickness(0, 0, 0, 5)
            };
            row.Children.Add(text);
            Grid.SetRow(actions, 1);
            row.Children.Add(actions);
            host.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 100, 116, 139)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 7),
                Child = row
            });
        }

        var add = new Button
        {
            Content = "+ Dodaj link",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 0)
        };
        add.Click += async (_, _) =>
        {
            var link = ExternalLinkService.Create("Nowy link", "https://example.com/");
            if (!await EditAsync(add, link, isNew: true))
            {
                return;
            }

            links.Add(link);
            changed();
            Bind(host, links, changed, statusChanged);
        };
        host.Children.Add(add);
    }

    public static void UpdateCompactButton(Button button, ICollection<ExternalLink> links)
    {
        button.Content = links.Count == 0 ? "Linki…" : $"Linki ({links.Count})";
        ToolTip.SetTip(button, links.Count == 0
            ? "Dodaj nazwany adres. MapaNotatek nie otwiera stron."
            : string.Join("\n", links.Select(link => $"{link.Label}: {link.Url}")));
    }

    public static void ShowCompactMenu(
        Button anchor,
        IList<ExternalLink> links,
        Action changed,
        Action<string>? statusChanged = null)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = "MapaNotatek nie otwiera stron — link można tylko skopiować",
            IsEnabled = false
        });
        if (links.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        foreach (var link in links.ToList())
        {
            var submenu = new MenuItem { Header = link.Label };
            var copy = new MenuItem { Header = "Kopiuj adres" };
            copy.Click += async (_, _) =>
            {
                await CopyAsync(anchor, link.Url);
                statusChanged?.Invoke($"Skopiowano link: {link.Label}");
            };
            var edit = new MenuItem { Header = "Edytuj…" };
            edit.Click += async (_, _) =>
            {
                if (await EditAsync(anchor, link))
                {
                    changed();
                    UpdateCompactButton(anchor, links);
                }
            };
            var remove = new MenuItem { Header = "Usuń" };
            remove.Click += (_, _) =>
            {
                links.Remove(link);
                changed();
                UpdateCompactButton(anchor, links);
            };
            submenu.Items.Add(copy);
            submenu.Items.Add(edit);
            submenu.Items.Add(remove);
            menu.Items.Add(submenu);
        }

        menu.Items.Add(new Separator());
        var add = new MenuItem { Header = "+ Dodaj link…" };
        add.Click += async (_, _) =>
        {
            var link = ExternalLinkService.Create("Nowy link", "https://example.com/");
            if (await EditAsync(anchor, link, isNew: true))
            {
                links.Add(link);
                changed();
                UpdateCompactButton(anchor, links);
            }
        };
        menu.Items.Add(add);
        anchor.ContextMenu = menu;
        menu.Open(anchor);
    }

    private static async Task CopyAsync(Control anchor, string value)
    {
        var clipboard = TopLevel.GetTopLevel(anchor)?.Clipboard;
        if (clipboard is not null)
        {
            var item = new Avalonia.Input.DataTransferItem();
            item.SetText(value);
            var transfer = new Avalonia.Input.DataTransfer();
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer);
        }
    }

    private static async Task<bool> EditAsync(Control anchor, ExternalLink link, bool isNew = false)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
        {
            return false;
        }

        var nameBox = new TextBox { Text = isNew ? string.Empty : link.Label, PlaceholderText = "Nazwa, np. Zadanie Jira" };
        var urlBox = new TextBox { Text = isNew ? string.Empty : link.Url, PlaceholderText = "https://…" };
        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(190, 24, 93)),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        var cancel = new Button { Content = "Anuluj", IsCancel = true, MinWidth = 82 };
        var save = new Button { Content = "Zapisz", IsDefault = true, MinWidth = 82 };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        var content = new StackPanel { Margin = new Thickness(18), Spacing = 9 };
        content.Children.Add(new TextBlock { Text = "NAZWA", FontSize = 10, FontWeight = FontWeight.SemiBold, Opacity = 0.62 });
        content.Children.Add(nameBox);
        content.Children.Add(new TextBlock { Text = "ADRES HTTP/HTTPS", FontSize = 10, FontWeight = FontWeight.SemiBold, Opacity = 0.62 });
        content.Children.Add(urlBox);
        content.Children.Add(new TextBlock
        {
            Text = "Adres zostanie zapisany lokalnie. Aplikacja pozwoli go tylko skopiować.",
            FontSize = 11,
            Opacity = 0.62,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(error);
        content.Children.Add(actions);
        var dialog = new Window
        {
            Title = isNew ? "Dodaj link" : "Edytuj link",
            Width = 500,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };
        var accepted = false;
        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) =>
        {
            try
            {
                var normalized = ExternalLinkService.Create(nameBox.Text, urlBox.Text);
                link.Label = normalized.Label;
                link.Url = normalized.Url;
                accepted = true;
                dialog.Close();
            }
            catch (FormatException ex)
            {
                error.Text = ex.Message;
                error.IsVisible = true;
                urlBox.Focus();
                urlBox.SelectAll();
            }
        };
        await dialog.ShowDialog(owner);
        return accepted;
    }
}
