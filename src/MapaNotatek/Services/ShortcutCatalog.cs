using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class ShortcutCatalog
{
    // Shortcuts local to a particular control. Global shortcuts live in MainWindow's
    // command list, which also feeds the palette and both help screens.
    public static IReadOnlyList<ShortcutInfo> Contextual { get; } =
    [
        new() { Keys = "Enter (checklist)", Action = "Zatwierdź zadanie i dodaj kolejne" },
        new() { Keys = "PPM", Action = "Menu kontekstowe na grafie / listach" },
        new() { Keys = "F6", Action = "Następny panel" },
        new() { Keys = "Shift+F6", Action = "Poprzedni panel" },
        new() { Keys = "Strzałki", Action = "Przechodzenie między kulkami (graf)" },
        new() { Keys = "Enter", Action = "Otwórz zaznaczoną kulkę" },
        new() { Keys = "Esc", Action = "Zamknij wyszukiwanie, skupienie lub dokument" },
        new() { Keys = "F2", Action = "Zmień nazwę" },
        new() { Keys = PlatformKeys.TrashLabel, Action = "Przenieś notatkę do kosza" }
    ];
}
