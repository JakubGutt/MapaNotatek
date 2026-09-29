using Avalonia.Controls;
using Avalonia.Layout;
using MapaNotatek.Models;

namespace MapaNotatek.Views;

public static class PersonPickerMenu
{
    public static Button CreateButton(
        IEnumerable<Person> people,
        IEnumerable<string>? selectedSlugs,
        Action<IReadOnlyList<string>> selectionChanged,
        double fontSize = 12)
    {
        var currentSelection = selectedSlugs?.ToList() ?? [];
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 32,
            Padding = new Avalonia.Thickness(9, 5),
            FontSize = fontSize
        };
        UpdateButton(button, people, currentSelection);
        button.Click += (_, _) => Show(button, people, currentSelection, selected =>
        {
            currentSelection = selected.ToList();
            selectionChanged(selected);
        });
        return button;
    }

    public static void Show(
        Button anchor,
        IEnumerable<Person> peopleSource,
        IEnumerable<string>? selectedSlugs,
        Action<IReadOnlyList<string>> selectionChanged)
    {
        var people = peopleSource
            .OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var selected = RegisteredSelection(people, selectedSlugs);
        var menu = new ContextMenu();

        if (people.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Brak osób — dodaj je najpierw w panelu Osoby",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var person in people)
            {
                var item = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(person.Role)
                        ? person.Name
                        : $"{person.Name}  ·  {person.Role}",
                    Tag = person.Slug,
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = selected.Contains(person.Slug),
                    StaysOpenOnClick = true
                };
                item.PropertyChanged += (_, args) =>
                {
                    if (args.Property != MenuItem.IsCheckedProperty || item.Tag is not string slug)
                    {
                        return;
                    }

                    if (item.IsChecked)
                    {
                        selected.Add(slug);
                    }
                    else
                    {
                        selected.Remove(slug);
                    }

                    var ordered = people
                        .Where(candidate => selected.Contains(candidate.Slug))
                        .Select(candidate => candidate.Slug)
                        .ToList();
                    UpdateButton(anchor, people, ordered);
                    selectionChanged(ordered);
                };
                menu.Items.Add(item);
            }
        }

        anchor.ContextMenu = menu;
        menu.Open(anchor);
    }

    public static void UpdateButton(
        Button button,
        IEnumerable<Person> peopleSource,
        IEnumerable<string>? selectedSlugs)
    {
        var people = peopleSource.ToList();
        var selected = RegisteredSelection(people, selectedSlugs);
        var names = people
            .Where(person => selected.Contains(person.Slug))
            .OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(person => person.Name)
            .ToList();

        button.Content = names.Count switch
        {
            0 when people.Count == 0 => "Brak osób — dodaj w panelu Osoby",
            0 => "Wybierz osoby…",
            1 => names[0],
            2 => string.Join(", ", names),
            _ => $"{names[0]}, {names[1]}  +{names.Count - 2}"
        };
        ToolTip.SetTip(button, names.Count == 0
            ? "Można wybrać wyłącznie osoby istniejące w panelu Osoby"
            : string.Join("\n", names));
    }

    private static HashSet<string> RegisteredSelection(
        IReadOnlyCollection<Person> people,
        IEnumerable<string>? selectedSlugs)
    {
        var known = people.Select(person => person.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (selectedSlugs ?? [])
            .Where(known.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
