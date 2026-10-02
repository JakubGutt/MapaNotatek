using Avalonia.Controls;
using Avalonia.Layout;
using MapaNotatek.Models;

namespace MapaNotatek.Views;

public static class SystemPickerMenu
{
    public static void Show(
        Button anchor,
        IEnumerable<Project> systemsSource,
        IEnumerable<string>? selectedIds,
        Action<IReadOnlyList<string>> selectionChanged)
    {
        var systems = systemsSource
            .Where(project => project.ItemType == ProjectItemType.System)
            .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var known = systems.Select(system => system.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = (selectedIds ?? []).Where(known.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var menu = new ContextMenu();
        if (systems.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Brak systemów — utwórz najpierw system", IsEnabled = false });
        }
        else
        {
            menu.Items.Add(new MenuItem { Header = "Wybierz jeden lub kilka systemów", IsEnabled = false });
            menu.Items.Add(new Separator());
            foreach (var system in systems)
            {
                var item = new MenuItem
                {
                    Header = system.Name,
                    Tag = system.Id,
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = selected.Contains(system.Id),
                    StaysOpenOnClick = true
                };
                item.PropertyChanged += (_, args) =>
                {
                    if (args.Property != MenuItem.IsCheckedProperty || item.Tag is not string id)
                    {
                        return;
                    }

                    if (item.IsChecked)
                    {
                        selected.Add(id);
                    }
                    else
                    {
                        selected.Remove(id);
                    }

                    var ordered = systems.Where(system => selected.Contains(system.Id)).Select(system => system.Id).ToList();
                    UpdateButton(anchor, systems, ordered);
                    selectionChanged(ordered);
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var done = new MenuItem { Header = "Gotowe" };
            done.Click += (_, _) => menu.Close();
            menu.Items.Add(done);
        }

        anchor.ContextMenu = menu;
        menu.Open(anchor);
    }

    public static void UpdateButton(Button button, IEnumerable<Project> systemsSource, IEnumerable<string>? selectedIds)
    {
        var systems = systemsSource.Where(project => project.ItemType == ProjectItemType.System).ToList();
        var selected = (selectedIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = systems.Where(system => selected.Contains(system.Id))
            .OrderBy(system => system.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(system => system.Name)
            .ToList();
        button.Content = names.Count switch
        {
            0 when systems.Count == 0 => "Brak systemów — utwórz system",
            0 => "Wybierz systemy…",
            1 => names[0],
            2 => string.Join(", ", names),
            _ => $"{names[0]}, {names[1]}  +{names.Count - 2}"
        };
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(button, names.Count == 0 ? "Każde przypisanie jest jawne i niezależne" : string.Join("\n", names));
    }
}
