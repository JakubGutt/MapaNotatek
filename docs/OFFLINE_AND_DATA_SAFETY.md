# Offline i bezpieczeństwo danych

Ten dokument opisuje obecne zabezpieczenia MapaNotatek i ich granice. Aplikacja ma być prostym narzędziem działającym wyłącznie na plikach użytkownika, bez usług sieciowych.

## Granica offline

Gotowa aplikacja:

- nie ma klienta HTTP, TCP/UDP ani WebSocket;
- nie osadza przeglądarki i nie pobiera zasobów z adresów URL;
- nie ma telemetrii, kont użytkowników, synchronizacji, reklam ani sprawdzania aktualizacji;
- zapisuje notatki, obrazy, historię, kopie i raport diagnostyczny wyłącznie lokalnie;
- tworzy HTML z polityką blokującą zasoby sieciowe i osadza użyte lokalne obrazy jako dane dokumentu.
- polecenie „Do Confluence” zapisuje rich text wyłącznie do schowka; samo niczego nie otwiera, nie publikuje i nie wysyła.

Test regresyjny skanuje kod źródłowy pod kątem klas klientów sieciowych i kończy się błędem po ich dodaniu. Jest to strażnik architektury, nie zapora systemowa. Po zmianie zależności lub sposobu publikacji nadal trzeba przejrzeć wynikowy pakiet.

Internet może być używany poza uruchomioną aplikacją: deweloper pobiera pakiety NuGet, a GitHub Actions pobiera narzędzia i publikuje artefakty. System operacyjny może niezależnie wykonywać własne kontrole bezpieczeństwa. Żadne z tych działań nie jest ruchem sieciowym inicjowanym przez MapaNotatek.

## Model danych

Notatki, projekty i profile osób są zwykłymi plikami Markdown z metadanymi w nagłówku. Załączone obrazy i awatary są kopiowane do `Assets/<item-id>/`; aplikacja nie pozostawia danych zależnych od pierwotnej lokalizacji importowanego pliku. Import przyjmuje rozpoznane obrazy PNG, JPEG, GIF i WebP do 20 MB.

Edytor komórkowy jest warstwą nad tym samym przenośnym formatem: po zapisie i ponownym otwarciu bloki są odtwarzane z Markdown. Interfejs nie udostępnia osobnego widoku źródłowego; dokładną treść można otrzymać przez eksport do pliku `.md`.

Stan interfejsu jest przechowywany w `app-state.json`. Lokalna historia trafia do `History/` i domyślnie zachowuje do 30 wcześniejszych wersji każdego elementu. Log startowy i ostatni raport awarii, jeśli powstaną, leżą w systemowym folderze tymczasowym. Eksport raportu diagnostycznego odbywa się wyłącznie na żądanie, nie zawiera treści notatek i nie jest nigdzie wysyłany.

## Zapis i konflikt zmian

Przy zapisie aplikacja najpierw tworzy plik tymczasowy, zapisuje go na dysk, a następnie atomowo podmienia plik docelowy. Poprzednia wersja jest zachowywana jako `.bak`, a przed kolejnym zapisem trafia również do ograniczonej historii.

Po wczytaniu dokumentu aplikacja zapamiętuje skrót jego treści. Jeśli plik zostanie w międzyczasie zmieniony przez inny program, zapis zostaje zatrzymany. Użytkownik może zachować oczekujące zmiany jako osobną lokalną kopię; wersja zmieniona zewnętrznie nie jest automatycznie nadpisywana.

Przy zamykaniu aplikacja próbuje opróżnić kolejkę zapisów. W razie błędu nie zamyka się bez ostrzeżenia i daje możliwość ponowienia, powrotu do dokumentu, zapisania konfliktu jako kopii albo świadomego zamknięcia bez zapisu.

## Błędy, kosz i historia

Nieczytelny plik nie jest po cichu zastępowany pustą treścią. Aplikacja zgłasza problem i, jeśli poprawna kopia `.bak` istnieje, może z niej odczytać dane. Uszkodzony `app-state.json`, którego nie można odzyskać, jest przenoszony do `Recovery/` przed utworzeniem świeżego stanu.

Usuwanie notatek i projektów przenosi je do `Trash/`; z poziomu aplikacji można je przywrócić. Historia dostępna w edytorze notatki pozwala podejrzeć i odtworzyć wcześniejszą wersję. Kosz i historia są częścią tej samej biblioteki, więc utrata całego dysku usuwa także je.

## Zweryfikowana kopia

Funkcja kopii w ustawieniach:

1. kopiuje wyłącznie `Notes/`, `Projects/`, `People/`, `Assets/`, `History/`, `Trash/`, `Recovery/` i `app-state.json`;
2. pomija dowiązania symboliczne;
3. zapisuje rozmiar i SHA-256 każdego pliku w manifeście;
4. weryfikuje gotową zawartość przed udostępnieniem folderu kopii;
5. zabrania umieszczenia kopii wewnątrz biblioteki źródłowej.

Przywracanie ponownie sprawdza manifest i akceptuje tylko nowy albo pusty folder. Nie scala danych i nie nadpisuje istniejącej biblioteki. Sama kopia nie jest szyfrowana — poufne dane należy przechowywać na zaszyfrowanym dysku lub zaszyfrowanym nośniku.

## Realistyczne ograniczenia

- Brak synchronizacji oznacza również brak współdzielonej edycji i automatycznego przenoszenia danych między komputerami.
- Historia ma ograniczoną liczbę wersji i nie zastępuje kopii przechowywanej poza komputerem.
- Markdown zachowuje przenośność, ale edytor i eksport obsługują praktyczny podzbiór formatowania, nie pełną zgodność z Wordem.
- Formatowanie blokowe jest wizualne, ale znaczniki formatowania wewnątrz wiersza mogą być widoczne podczas edycji.
- Eksport Markdown zachowuje odwołania do obrazów w bibliotece; samodzielne obrazy osadzają eksporty HTML, DOCX i PDF.
- Dane nie są szyfrowane przez aplikację. Ochronę przed innymi użytkownikami komputera zapewniają uprawnienia i szyfrowanie systemu operacyjnego.
- Pakiet macOS ma wyłącznie bezpłatny podpis ad-hoc; wydania nie mają zaufanego certyfikatu, notaryzacji ani automatycznych aktualizacji. Ostrzeżenie Gatekeepera lub SmartScreen nie oznacza połączenia aplikacji z internetem.
