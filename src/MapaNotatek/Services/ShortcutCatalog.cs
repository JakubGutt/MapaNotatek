using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutInfo> All { get; } =
    [
        new() { Keys = PlatformKeys.Chord("N"), Action = "Nowa notatka" },
        new() { Keys = PlatformKeys.ChordShift("N"), Action = "Nowy projekt ogólny" },
        new() { Keys = "Enter (checklist)", Action = "Zatwierdź zadanie i dodaj kolejne" },
        new() { Keys = "PPM", Action = "Menu kontekstowe na grafie / listach" },
        new() { Keys = PlatformKeys.Chord("S"), Action = "Zapisz natychmiast" },
        new() { Keys = PlatformKeys.Chord("F"), Action = "Znajdź i zamień w otwartym dokumencie" },
        new() { Keys = PlatformKeys.ChordShift("F"), Action = "Szukaj w całej bibliotece" },
        new() { Keys = PlatformKeys.Chord("Z"), Action = "Cofnij" },
        new() { Keys = PlatformKeys.RedoLabel, Action = "Ponów" },
        new() { Keys = PlatformKeys.Chord("1"), Action = "Widok grafu" },
        new() { Keys = PlatformKeys.Chord("2"), Action = "Lista notatek" },
        new() { Keys = PlatformKeys.Chord("3"), Action = "Lista otwartych zadań" },
        new() { Keys = "F6", Action = "Następny panel" },
        new() { Keys = "Shift+F6", Action = "Poprzedni panel" },
        new() { Keys = "Strzałki", Action = "Przechodzenie między kulkami (graf)" },
        new() { Keys = "Enter", Action = "Otwórz zaznaczoną kulkę" },
        new() { Keys = "Esc", Action = "Zamknij wyszukiwanie, skupienie lub dokument" },
        new() { Keys = "F2", Action = "Zmień nazwę" },
        new() { Keys = PlatformKeys.Chord("+"), Action = "Powiększ graf" },
        new() { Keys = PlatformKeys.Chord("-"), Action = "Pomniejsz graf" },
        new() { Keys = PlatformKeys.Chord("0"), Action = "Domyślne powiększenie" },
        new() { Keys = PlatformKeys.ChordShift("P"), Action = "Lista poleceń" },
        new() { Keys = PlatformKeys.Chord("/"), Action = "Lista skrótów" },
        new() { Keys = PlatformKeys.Chord(","), Action = "Ustawienia" },
        new() { Keys = PlatformKeys.Chord("W"), Action = "Zamknij panel edycji" },
        new() { Keys = PlatformKeys.TrashLabel, Action = "Przenieś notatkę do kosza" }
    ];
}
