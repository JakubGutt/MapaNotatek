# Wymagania funkcjonalne MapaNotatek

Wersja dokumentu: 1.0  
Cel: lekka, bezpłatna aplikacja wiedzy dla osób pracujących całkowicie offline.

## 1. Zasady produktu

1. Edytor jest głównym ekranem i otrzymuje większość przestrzeni roboczej.
2. Graf, projekty i zadania pomagają organizować wiedzę, ale nie utrudniają pisania.
3. Dane należą do użytkownika i pozostają w czytelnych plikach na jego komputerze.
4. Uruchomiona aplikacja nie inicjuje żadnego połączenia internetowego.
5. Odbiorca pobiera gotową paczkę i nie instaluje serwera, bazy ani środowiska .NET.
6. Funkcja nie jest ukończona, jeśli zapis, ponowne otwarcie albo eksport gubi jej treść.

## 2. Zakres obecnego wydania

Oznaczenia: **gotowe** — zaimplementowane i objęte kontrolą; **częściowe** — działa użyteczny zakres, ale nie pełny poziom Worda/Confluence; **planowane** — następna iteracja.

### FR-ED — edytor dokumentu

- **FR-ED-01 — gotowe:** po otwarciu notatki użytkownik otrzymuje pełnostronicowy edytor wizualny, nie surowy Markdown.
- **FR-ED-02 — gotowe:** dokument obsługuje akapit, H1–H3, listę punktowaną, listę numerowaną, checklistę, cytat, blok kodu, separator, obraz i tabelę.
- **FR-ED-03 — gotowe:** użytkownik może zmienić typ bloku, dodać blok poniżej, przesunąć go i usunąć.
- **FR-ED-04 — gotowe:** Enter dzieli zwykły blok w miejscu kursora; Backspace na początku scala akapity albo przywraca zwykły akapit.
- **FR-ED-05 — gotowe:** dokument ma stałą maksymalną szerokość; bardzo długi wiersz zawija się i nie rozciąga okna.
- **FR-ED-06 — gotowe:** użytkownik może przełączyć się między edycją komórkową a sformatowanym podglądem bez utraty treści; Markdown pozostaje formatem pliku i eksportu.
- **FR-ED-07 — częściowe:** pogrubienie, kursywa, przekreślenie i kod inline są dostępne; w trakcie edycji ich znaczniki mogą pozostać widoczne.
- **FR-ED-08 — gotowe:** tabele umożliwiają edycję komórek oraz dodawanie/usuwanie wierszy i kolumn.
- **FR-ED-09 — gotowe:** obrazy są wybierane z dysku, walidowane i kopiowane do lokalnego `Assets/<note-id>/`.
- **FR-ED-10 — gotowe:** spis treści powstaje z nagłówków i przenosi fokus do wskazanego bloku.
- **FR-ED-11 — gotowe:** edytor pokazuje liczbę słów/znaków, status zapisu, tryb skupienia i szczegóły notatki.
- **FR-ED-12 — gotowe:** znajdowanie i zamiana działa w całym bieżącym dokumencie, także z rozróżnianiem wielkości liter.
- **FR-ED-13 — planowane:** wklejenie lub przeciągnięcie obrazu ma tworzyć blok obrazu bez użycia okna wyboru pliku.
- **FR-ED-14 — planowane:** formatowanie inline w trybie wizualnym ma ukrywać składnię Markdown.

### FR-TPL — szablony

- **FR-TPL-01 — gotowe:** nową notatkę można utworzyć z szablonu spotkania, decyzji, planu projektu, procedury albo notatki dziennej.
- **FR-TPL-02 — gotowe:** szablon może automatycznie użyć bieżącej daty w tytule.
- **FR-TPL-03 — gotowe:** szablony są dostępne z menu, paska bocznego oraz palety poleceń.
- **FR-TPL-04 — planowane:** bieżącą notatkę można zapisać jako własny lokalny szablon.

### FR-SR — wyszukiwanie i nawigacja

- **FR-SR-01 — gotowe:** wyszukiwanie obejmuje tytuł, treść i tagi.
- **FR-SR-02 — gotowe:** obsługiwane są pełne frazy w cudzysłowie i warunki wykluczające z prefiksem `-`.
- **FR-SR-03 — gotowe:** filtry obejmują `title:`, `body:`, `tag:`, `project:`, `type:` i `has:task`; można je łączyć.
- **FR-SR-04 — gotowe:** notatki można przypinać, a ostatnio używane elementy są widoczne w pasku bocznym.
- **FR-SR-05 — gotowe:** `[[wikilink]]` łączy notatki i jest widoczny w panelu powiązań oraz na grafie.
- **FR-SR-06 — planowane:** widok notatki pokazuje również dokumenty wskazujące na nią.
- **FR-SR-07 — planowane:** użytkownik może zapisać zestaw filtrów jako nazwany widok.

### FR-PR — architektura, projekty, foldery i zadania

- **FR-PR-11 — gotowe:** użytkownik może przeciągać kafelki otwartych zadań, aby ustawić ich globalny priorytet; najważniejsze zadania są wyświetlane u góry, a kolejność jest trwała.
- **FR-GR-06 — gotowe:** uchwyt węzła tworzy walidowane połączenia hierarchiczne, systemowe, projekt–notatka i notatka–notatka; jawne krawędzie można usunąć bez usuwania węzłów.
- **FR-AR-01 — gotowe:** produkt, podsystem i komponent mają jawny wielokrotny wybór systemów bez automatycznego dziedziczenia.
- **FR-NAV-01 — gotowe:** Wstecz/Dalej przywraca widok, dokument i stan kamery grafu w bieżącej sesji.
- **FR-PR-12 — gotowe:** lewe drzewo zachowuje rozwinięte gałęzie podczas otwierania notatek, zapisu oraz przenoszenia elementów.

- **FR-PR-01 — gotowe:** użytkownik może tworzyć projekty oraz zagnieżdżone foldery.
- **FR-PR-02 — gotowe:** tag zgodny ze slugiem projektu przypisuje notatkę do projektu.
- **FR-PR-03 — gotowe:** checklisty notatek i projektów zasilają jeden widok otwartych zadań.
- **FR-PR-04 — gotowe:** graf pokazuje projekty, notatki i relacje, ale jest widokiem dodatkowym.
- **FR-PR-05 — gotowe:** graf może skupić się na projekcie i zachowuje lokalne położenia elementów.
- **FR-PR-06 — gotowe:** struktura rozróżnia system, produkt, podsystem i komponent; starsze projekty pozostają projektami ogólnymi.
- **FR-PR-07 — gotowe:** dozwolony model relacji to system → produkt → podsystem/komponent → komponent, a foldery i projekty ogólne są elastycznymi kontenerami.
- **FR-PR-08 — gotowe:** graf i drzewo używają wspólnego koloru i oznaczenia dla każdego typu architektury.
- **FR-PR-09 — gotowe:** typ elementu można wybrać z jednego menu tworzenia w lewym drzewie i z menu kontekstowego rodzica.
- **FR-PR-10 — gotowe:** lewe drzewo pokazuje notatki pod wszystkimi powiązanymi projektami, a nieprzypisane dokumenty grupuje osobno; notatkę można z drzewa otworzyć lub przeciągnąć na projekt.

### FR-EX — eksport i współpraca bez integracji sieciowej

- **FR-EX-01 — gotowe:** notatkę można wyeksportować do Markdown, samodzielnego HTML, DOCX i PDF.
- **FR-EX-02 — gotowe:** eksport HTML blokuje zasoby zewnętrzne, a poprawne lokalne obrazy osadza jako dane dokumentu.
- **FR-EX-03 — gotowe:** „Do Confluence” kopiuje do schowka rich text i wersję tekstową; aplikacja nie otwiera Confluence ani nie wysyła danych.
- **FR-EX-04 — gotowe:** tabele, nagłówki, listy, checklisty i bloki kodu zachowują użyteczną strukturę w eksportach.
- **FR-EX-05 — planowane:** import pojedynczego DOCX/HTML tworzy raport elementów zachowanych i pominiętych.

### FR-PE — osoby i zaangażowanie

- **FR-PE-01 — gotowe:** użytkownik może utworzyć lokalny profil osoby z imieniem, rolą, opisem i opcjonalnym awatarem.
- **FR-PE-02 — gotowe:** projekty, foldery, notatki i zadania mają osobne przypisanie osób, niezależne od zwykłych tagów.
- **FR-PE-03 — gotowe:** panel osób pokazuje awatary jako węzły i łączy osoby współdzielące kontekst.
- **FR-PE-04 — gotowe:** szczegóły osoby zbierają jej projekty, foldery, notatki oraz zadania i pozwalają otworzyć źródło relacji.
- **FR-PE-05 — gotowe:** osobę przypisuje się przez selektor wielokrotnego wyboru zasilany rejestrem panelu Osoby; niezarejestrowanego identyfikatora nie można zapisać.
- **FR-PE-06 — gotowe:** bez zdjęcia aplikacja pokazuje inicjały, a importowany awatar pozostaje lokalny.
- **FR-PE-07 — gotowe:** profil osoby można przenieść do kosza po potwierdzeniu; operacja usuwa jej przypisania z projektów, notatek i zadań, a sam profil można później przywrócić.

### FR-DATA — zapis, historia i odzyskiwanie

- **FR-DATA-01 — gotowe:** notatki, projekty i profile osób są zapisywane atomowo jako lokalne pliki Markdown z metadanymi.
- **FR-DATA-02 — gotowe:** przed podmianą istniejącego pliku powstaje `.bak`, a wcześniejsze wersje trafiają do ograniczonej historii.
- **FR-DATA-03 — gotowe:** zewnętrzna zmiana pliku zatrzymuje zapis; szkic można zachować jako osobną kopię.
- **FR-DATA-04 — gotowe:** usuwanie przenosi element do kosza z możliwością przywrócenia.
- **FR-DATA-05 — gotowe:** uszkodzony stan jest zachowywany w `Recovery/`; błąd nie może zostać zamieniony po cichu na pusty dokument.
- **FR-DATA-06 — gotowe:** kopia biblioteki ma manifest SHA-256, jest sprawdzana po utworzeniu i przed przywróceniem.
- **FR-DATA-07 — gotowe:** przy zamykaniu użytkownik może zaktualizować zewnętrzną rotację `Current`/`Previous`; ostatnia poprawna kopia nie jest usuwana przed walidacją nowej.
- **FR-DATA-08 — gotowe:** biblioteka ma wersję schematu, nowszy format blokuje zapis, a migracja starszego formatu pracuje na nowym katalogu obok oryginału.
- **FR-DATA-07 — gotowe:** przywracanie wymaga nowego albo pustego folderu i nie nadpisuje istniejącej biblioteki.

## 3. Wymagania niefunkcjonalne

- **NFR-OFF-01:** kod produkcyjny nie może zawierać klienta HTTP, TCP/UDP, WebSocket ani osadzonej przeglądarki.
- **NFR-OFF-02:** brak konta, telemetrii, reklam, synchronizacji, zdalnych obrazów i automatycznych aktualizacji.
- **NFR-PORT-01:** paczka jest samodzielna; użytkownik końcowy nie instaluje .NET.
- **NFR-DATA-01:** aplikacja nie może utracić poprawnego starego pliku, gdy nowy zapis się nie powiedzie.
- **NFR-DATA-02:** import załącznika nie może zapisać pliku poza katalogiem biblioteki.
- **NFR-UX-01:** podstawowe pisanie jest możliwe bez znajomości Markdown.
- **NFR-UX-02:** najczęstsze akcje są dostępne z klawiatury i palety poleceń.
- **NFR-TEST-01:** wydanie musi przejść testy zapisu/odzyskiwania, polityki offline, edytora blokowego oraz struktury eksportów.
- **NFR-TEST-02:** build Release kończy się z zerem błędów i zerem ostrzeżeń.

## 4. Przewaga nad Confluence w przyjętym zakresie

MapaNotatek ma wygrywać w obszarach, które nie wymagają serwera:

- start bez logowania, konta, VPN i oczekiwania na stronę;
- treść dostępna nawet przy całkowitym braku sieci;
- proste, przenośne pliki zamiast danych zamkniętych wyłącznie w usłudze;
- szybkie tworzenie dokumentów z szablonów oraz pełnoekranowe pisanie bez rozpraszaczy;
- lokalna historia, kosz, wykrywanie konfliktów i sprawdzalne kopie;
- jednorazowe kopiowanie rich textu do Confluence, gdy firma nadal wymaga publikacji tam.

Nie może wygrywać w komentarzach na żywo, współedycji, uprawnieniach zespołowych ani centralnym wyszukiwaniu — realizacja tych funkcji złamałaby twardy warunek całkowitego offline.

## 5. Scenariusze odbioru przed wydaniem

1. Utworzyć każdą z pięciu notatek szablonowych, zamknąć aplikację i potwierdzić identyczną strukturę po ponownym otwarciu.
2. Utworzyć nagłówki, listę, zadanie, cytat, kod, obraz i tabelę; zmienić kolejność bloków i ponownie otworzyć dokument.
3. Wpisać kilkaset znaków bez spacji i potwierdzić, że kartka nie zmienia szerokości.
4. Wyszukać `title:`, pełną frazę, wykluczenie, `project:` i `has:task` na jednej bibliotece testowej.
5. Wyeksportować dokument do wszystkich czterech formatów i otworzyć DOCX/PDF poza aplikacją.
6. Skopiować rich text i wkleić go do pustego edytora obsługującego HTML; sprawdzić nagłówki, listy, tabelę i kod.
7. Zmienić plik notatki zewnętrznie podczas edycji i potwierdzić, że aplikacja go nie nadpisuje.
8. Uszkodzić kopię biblioteki i potwierdzić odrzucenie jej przez walidację manifestu.
9. Uruchomić gotową paczkę na czystym profilu bez SDK .NET i bez dostępu do sieci.
