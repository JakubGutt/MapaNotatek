# Plan produktu MapaNotatek

Priorytetem jest szybkie, spokojne pisanie i pełna własność danych. Graf pomaga odkrywać relacje, ale nie może zabierać miejsca ani uwagi edytorowi. Produkt nie próbuje kopiować całego Confluence: ma być wyraźnie lepszy w jednoosobowej pracy offline, uruchamiać się bez konta i pozostawiać po sobie czytelne pliki.

Szczegółowe wymagania i kryteria odbioru znajdują się w [FUNCTIONAL_REQUIREMENTS.md](FUNCTIONAL_REQUIREMENTS.md).

## Stan obecnego wydania

### Pisanie

- wizualny edytor blokowy jest domyślnym trybem pełnej strony;
- akapity, nagłówki, listy, zadania, cytaty, kod, obrazy, tabele i separatory są osobnymi blokami;
- bloki można zmieniać, wstawiać, przesuwać i usuwać, a Enter dzieli tekst na kolejne bloki;
- tabele mają edycję komórek oraz dodawanie i usuwanie wierszy i kolumn;
- Markdown pozostaje formatem pliku i eksportu, bez osobnego widoku źródłowego w interfejsie;
- długi nieprzerwany wiersz zawija się i nie poszerza kartki dokumentu;
- spis treści jest budowany z nagłówków i prowadzi do wybranego miejsca;
- dostępne są wyszukiwanie i zamiana, tryb skupienia, podgląd, licznik oraz autosave.

### Powtarzalna praca i wymiana

- wbudowane szablony: spotkanie, decyzja, plan projektu, procedura i notatka dzienna;
- lokalne obrazy są kopiowane do biblioteki, dzięki czemu notatka nie zależy od pliku źródłowego;
- eksporty: Markdown, samodzielny HTML, DOCX i PDF;
- polecenie „Do Confluence” umieszcza w schowku rich text oraz zwykły tekst, bez wysyłania danych;
- eksporty HTML nie pobierają zasobów z internetu, a lokalne obrazy są osadzane w pliku;
- historia wersji pozwala podejrzeć i przywrócić wcześniejszy stan dokumentu.

### Znajdowanie i organizacja

- systemy, produkty, podsystemy i komponenty z walidowaną hierarchią oraz pomocnicze projekty i foldery;
- panel osób z lokalnymi awatarami i osobnymi przypisaniami do projektów, folderów, notatek oraz zadań;
- wyszukiwanie zwykłe, pełne frazy, wykluczenia i filtry pól;
- kosz z przywracaniem notatek oraz projektów;
- graf architektury, notatek i wikilinków jako widok pomocniczy, z odrębnym kolorem każdego typu.

### Bezpieczeństwo danych i offline

- brak klientów HTTP, TCP/UDP, WebSocket, osadzonej przeglądarki, konta, telemetrii i aktualizatora;
- atomowy zapis, kopia `.bak`, ograniczona historia oraz wykrywanie konfliktu z zewnętrzną zmianą pliku;
- zweryfikowane kopie z manifestem SHA-256 i przywracaniem wyłącznie do pustej biblioteki;
- samodzielne paczki dla macOS oraz Windows — odbiorca nie instaluje .NET;
- testy struktury DOCX, nagłówka PDF, polityki offline HTML i statycznej blokady klientów sieciowych.

## Następna iteracja — P0

1. Wklejanie obrazu bezpośrednio ze schowka i przeciąganie pliku na dokument.
2. Własne szablony tworzone z bieżącej notatki, przechowywane i kopiowane razem z biblioteką.
3. Prawdziwe formatowanie inline bez pokazywania znaczników `**`, `*` i `~~` w trybie wizualnym.
4. Testy interfejsu dla tworzenia z szablonu, zamykania z błędem zapisu, konfliktu, historii oraz bardzo długiego wiersza.
5. Dostępne klawiaturą menu typu bloku i komendy `/` z filtrowaniem.

## Dalsza rozbudowa — P1

1. Rozmiar, podpis i wyrównanie obrazu oraz prosta galeria.
2. Scalanie komórek, wyrównanie i szerokości kolumn tabeli.
3. Kolor tekstu i wyróżnienia oraz kilka spójnych stylów akapitu.
4. Widoki zapisane: np. „moje otwarte zadania”, „decyzje projektu” i „zmienione w tym tygodniu”.
5. Odnośniki do konkretnego nagłówka lub bloku i lista dokumentów wskazujących na bieżącą notatkę.
6. Import pojedynczego DOCX/HTML z czytelnym raportem elementów, których nie dało się zachować.

## Rzeczy celowo poza zakresem

- współedycja, komentarze na żywo i obecność innych osób;
- synchronizacja między komputerami, logowanie i chmura;
- osadzanie zdalnych stron i automatyczne pobieranie obrazów;
- automatyczne aktualizacje lub usługa działająca w tle;
- pełna zgodność z każdym detalem formatowania Worda.

Pierwsze cztery punkty są sprzeczne z twardym wymaganiem „nigdy bez internetu”. Pliki można przekazywać ręcznie, przez firmowy nośnik albo system wybrany niezależnie przez użytkownika, ale aplikacja nie wykonuje tej operacji.

## Kryteria wydania

- build Release kończy się bez błędów i ostrzeżeń;
- wszystkie testy zapisu, odzyskiwania, edytora i eksportów przechodzą;
- paczka uruchamia się bez SDK .NET na każdej deklarowanej platformie;
- długi wiersz nie zmienia szerokości dokumentu;
- zwykłe pisanie nie wymaga przejścia do Markdown;
- awaria zapisu, konflikt albo uszkodzenie pliku nie może cicho usunąć szkicu;
- użytkownik widzi lokalizację biblioteki oraz potrafi utworzyć i sprawdzić kopię;
- paczka nie zawiera funkcji sieciowych; zmiana zależności wymaga ponownego audytu.
