namespace MapaNotatek.Services;

public sealed record NoteTemplate(string Id, string Name, string Description, string Title, string Body);

public static class NoteTemplateCatalog
{
    public static IReadOnlyList<NoteTemplate> BuiltIn { get; } =
    [
        new(
            "meeting",
            "Spotkanie",
            "Agenda, notatki, decyzje i działania w jednym dokumencie.",
            "Spotkanie — {date}",
            """
            ## Cel

            

            ## Uczestnicy

            

            ## Agenda

            - 

            ## Notatki

            

            ## Decyzje

            - 

            ## Działania

            - [ ] 
            """),
        new(
            "decision",
            "Decyzja",
            "Kontekst, alternatywy, uzasadnienie i termin przeglądu.",
            "Decyzja — {date}",
            """
            ## Status

            Proponowana

            ## Kontekst

            

            ## Rozważane opcje

            1. 

            ## Decyzja

            

            ## Uzasadnienie

            

            ## Konsekwencje

            - 

            ## Data przeglądu

            
            """),
        new(
            "project-brief",
            "Plan projektu",
            "Cel, zakres, ryzyka, kamienie milowe i następne kroki.",
            "Plan projektu",
            """
            ## Cel

            

            ## Zakres

            ### W zakresie

            - 

            ### Poza zakresem

            - 

            ## Kamienie milowe

            - [ ] 

            ## Ryzyka

            | Ryzyko | Wpływ | Działanie |
            | --- | --- | --- |
            |  |  |  |

            ## Następne kroki

            - [ ] 
            """),
        new(
            "procedure",
            "Procedura",
            "Powtarzalna instrukcja z warunkami i checklistą.",
            "Procedura",
            """
            ## Cel

            

            ## Kiedy używać

            

            ## Wymagania wstępne

            - 

            ## Kroki

            1. 

            ## Weryfikacja

            - [ ] Wynik został sprawdzony

            ## Cofnięcie zmian

            
            """),
        new(
            "daily",
            "Notatka dzienna",
            "Szybkie zebranie planu, obserwacji i zadań.",
            "Dzień — {date}",
            """
            ## Najważniejsze dzisiaj

            - [ ] 

            ## Notatki

            

            ## Decyzje i wnioski

            - 

            ## Na później

            - [ ] 
            """)
    ];

    public static NoteTemplate Get(string id) =>
        BuiltIn.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Nieznany szablon notatki: {id}");

    public static string ResolveTitle(NoteTemplate template, DateTimeOffset now) =>
        template.Title.Replace("{date}", now.ToString("yyyy-MM-dd"), StringComparison.Ordinal);
}
