using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutInfo> All { get; } =
    [
        new() { Keys = "Ctrl+N", Action = "Nowa notatka" },
        new() { Keys = "Ctrl+Shift+N", Action = "Nowy projekt" },
        new() { Keys = "Ctrl+S", Action = "Zapisz natychmiast" },
        new() { Keys = "Ctrl+F", Action = "Przejdź do wyszukiwarki" },
        new() { Keys = "Ctrl+Z", Action = "Cofnij" },
        new() { Keys = "Ctrl+Y", Action = "Ponów" },
        new() { Keys = "Ctrl+1", Action = "Widok grafu" },
        new() { Keys = "Ctrl+2", Action = "Lista notatek" },
        new() { Keys = "Ctrl+3", Action = "Lista otwartych zadań" },
        new() { Keys = "F6", Action = "Następny panel" },
        new() { Keys = "Shift+F6", Action = "Poprzedni panel" },
        new() { Keys = "Strzałki", Action = "Przechodzenie między kulkami (graf)" },
        new() { Keys = "Enter", Action = "Otwórz zaznaczoną kulkę" },
        new() { Keys = "Esc", Action = "Zamknij prawy panel" },
        new() { Keys = "F2", Action = "Zmień nazwę" },
        new() { Keys = "Ctrl++", Action = "Powiększ graf" },
        new() { Keys = "Ctrl+-", Action = "Pomniejsz graf" },
        new() { Keys = "Ctrl+0", Action = "Domyślne powiększenie" },
        new() { Keys = "Ctrl+Shift+P", Action = "Lista poleceń" },
        new() { Keys = "Ctrl+/", Action = "Lista skrótów" },
        new() { Keys = "Ctrl+,", Action = "Ustawienia" },
        new() { Keys = "Ctrl+W", Action = "Zamknij panel edycji" },
        new() { Keys = "Delete", Action = "Przenieś notatkę do kosza" }
    ];
}
