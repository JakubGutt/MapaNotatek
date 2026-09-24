# MapaNotatek

Lokalna aplikacja desktopowa do pisania notatek, prowadzenia projektów i łączenia wiedzy. Głównym miejscem pracy jest pełnostronicowy edytor; graf pozostaje dodatkowym widokiem do odkrywania powiązań.

Aplikacja działa bez konta, serwera i subskrypcji. Gotowy program nie potrzebuje internetu ani zainstalowanego środowiska .NET.

## Co jest dostępne

- pełnostronicowy edytor wizualny z blokami, trybem skupienia, podglądem, licznikiem słów i automatycznym zapisem;
- Markdown pozostaje przenośnym formatem pliku i opcjonalnym widokiem źródłowym — nie trzeba pracować bezpośrednio na znacznikach blokowych;
- edytowalne bloki: akapit, trzy poziomy nagłówków, listy, checklisty, cytaty, kod, obrazy, tabele i separatory; bloki można zmieniać, przesuwać i usuwać;
- pasek formatowania: pogrubienie, kursywa, przekreślenie, kod w tekście, linki i `[[wikilinki]]`;
- lokalne obrazy PNG, JPEG, GIF i WebP — importowany plik jest kopiowany do biblioteki;
- tabela pozwala dodawać i usuwać wiersze oraz kolumny, a spis treści prowadzi do nagłówków dokumentu;
- pięć szablonów startowych: spotkanie, decyzja, plan projektu, procedura i notatka dzienna;
- znajdowanie i zamiana w otwartym dokumencie, z opcją rozróżniania wielkości liter;
- wyszukiwanie biblioteki z pełnymi frazami, wykluczeniami oraz filtrami `title:`, `body:`, `tag:`, `project:`, `type:` i `has:task`;
- lokalna historia wcześniejszych wersji notatki z podglądem i przywracaniem;
- eksport notatki do Markdown, samodzielnego HTML, DOCX i PDF;
- kopiowanie bezpiecznego rich textu do schowka, gotowego do wklejenia do Confluence lub innego edytora;
- projekty, zagnieżdżone foldery, tagi, checklisty, zadania zbiorcze, przypięte i ostatnio otwierane notatki;
- graf projektów, notatek i wikilinków jako widok pomocniczy;
- kosz dla notatek i projektów z możliwością przywrócenia;
- zweryfikowane kopie danych i przywracanie ich do nowego lub pustego folderu.

Edytor wizualny zapisuje zwykły Markdown w tle. Daje to czytelne i przenośne pliki bez własnościowego formatu. Formatowanie blokowe jest wizualne; składnia formatowania wewnątrz pojedynczego wiersza, np. `**pogrubienie**`, może być widoczna podczas bezpośredniej edycji. Nie jest to pełne odwzorowanie wszystkich funkcji Worda.

## Offline i prywatność

Kod aplikacji nie zawiera klientów HTTP, TCP/UDP, WebSocket ani osadzonej przeglądarki. Nie ma telemetrii, logowania, synchronizacji, reklam, zdalnych obrazów ani automatycznych aktualizacji. Eksport HTML ma blokadę zewnętrznych zasobów, a lokalne obrazy osadza w dokumencie.

Proces pobierania zależności i budowania projektu korzysta z NuGet i może wymagać internetu. Dotyczy to wyłącznie dewelopera — opublikowany, samodzielny program działa lokalnie. Więcej szczegółów: [Offline i bezpieczeństwo danych](docs/OFFLINE_AND_DATA_SAFETY.md).

## Obsługiwane wydania

Projekt publikuje samodzielne pliki dla:

- macOS: Apple Silicon (`osx-arm64`) i Intel (`osx-x64`);
- Windows: x64 (`win-x64`) i ARM64 (`win-arm64`).

Linux nie ma obecnie przygotowanego ani przetestowanego wydania.

## Uruchomienie deweloperskie

Wymagany jest .NET 10 SDK.

macOS:

```bash
chmod +x ./run-macos.sh ./publish-macos.sh
./run-macos.sh
```

Windows:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\run-windows.ps1
```

Na obu platformach można też uruchomić:

```bash
dotnet run --project src/MapaNotatek/MapaNotatek.csproj
```

## Publikacja dla współpracowników

Odbiorca nie potrzebuje .NET. Skrypty tworzą gotowy ZIP: na macOS z pakietem `MapaNotatek.app`, a na Windows z samodzielnym `MapaNotatek.exe`.

Przed pierwszym publikowaniem deweloper wykonuje jednorazowo `dotnet restore MapaNotatek.sln`. Same skrypty publikujące działają później z `--no-restore`, więc nie inicjują połączenia z NuGet.

```bash
./publish-macos.sh osx-arm64
./publish-macos.sh osx-x64
```

```powershell
.\publish-windows.ps1 win-x64
.\publish-windows.ps1 win-arm64
```

Wynik do przekazania koledze to `artifacts/MapaNotatek-<RID>.zip`. Obok powstaje plik `.zip.sha256`, którym można sprawdzić integralność paczki. CI buduje i testuje `osx-arm64` oraz `win-x64` przy każdym pushu i zgłoszeniu zmian (pull request).

Pakiet macOS ma bezpłatny podpis ad-hoc, ale artefakty nie mają zaufanego podpisu Developer ID/Authenticode ani notaryzacji. Gatekeeper lub SmartScreen mogą więc pokazać ostrzeżenie. Do wygodnej dystrybucji poza małą, zaufaną grupą potrzebne są płatne certyfikaty, notaryzacja i ewentualnie `.dmg`/instalator. Aplikacja nie ma automatycznego aktualizatora — nową wersję przekazuje się jako nowy ZIP.

Krótka instrukcja dla odbiorcy: [Pierwsze uruchomienie](docs/FIRST_RUN.md).

## Dane na dysku

Domyślna biblioteka to `~/Documents/MapaNotatek` na macOS albo `%USERPROFILE%\Documents\MapaNotatek` na Windows. Lokalizację można zmienić w ustawieniach.

```text
MapaNotatek/
  Notes/                 # notatki Markdown i kopie .bak
  Projects/              # projekty/foldery Markdown i kopie .bak
  Assets/<note-id>/      # obrazy skopiowane do notatek
  History/               # ograniczona historia lokalnych wersji
  Trash/                 # usunięte notatki
    Projects/            # usunięte projekty
  Recovery/              # zachowane uszkodzone pliki stanu
  app-state.json         # ustawienia biblioteki i stan interfejsu
```

Notatki i projekty są czytelnymi plikami `.md` z prostym nagłówkiem metadanych. Można je otworzyć także poza aplikacją, ale równoczesna edycja tego samego pliku w dwóch programach może wywołać konflikt. MapaNotatek wykrywa zmianę zewnętrzną przed nadpisaniem i pozwala zachować własne zmiany jako osobną kopię.

## Kopie i odzyskiwanie

Zapisy plików są wykonywane atomowo, z opróżnieniem bufora na dysk i kopią `.bak`. Błędy odczytu są pokazywane użytkownikowi; gdy to możliwe, aplikacja korzysta z kopii awaryjnej. Uszkodzony stan aplikacji jest zachowywany w `Recovery/`, zamiast znikać bez śladu.

Kopia tworzona w ustawieniach jest folderem z manifestem SHA-256. Zawiera tylko dane należące do aplikacji, jest weryfikowana po utworzeniu i nie może leżeć wewnątrz biblioteki źródłowej. Przywracanie nie nadpisuje istniejącej biblioteki — wymaga nowego albo pustego folderu.

Historia wersji i kosz ułatwiają cofnięcie pomyłki, ale nie zastępują kopii na osobnym nośniku.

## Testy

```bash
dotnet restore MapaNotatek.sln
dotnet build MapaNotatek.sln -c Release --no-restore
dotnet run --project tests/MapaNotatek.StorageTests/MapaNotatek.StorageTests.csproj -c Release --no-build
dotnet run --project tests/MapaNotatek.ExportTests/MapaNotatek.ExportTests.csproj -c Release --no-build
```

Zestaw regresyjny obejmuje między innymi zapis i odzyskiwanie, integralność kopii, ograniczenie ścieżek, kosz, historię, konflikty zmian zewnętrznych, edytor blokowy, szablony, filtrowanie, statyczną blokadę klientów sieciowych oraz prawdziwą walidację eksportów DOCX, PDF i HTML.

## Szybkie wyszukiwanie

Zapytania można łączyć; wszystkie podane warunki muszą pasować. Przykłady:

```text
title:migracja tag:ważne
body:"pełna fraza" -tag:archiwum
project:atlas has:task
type:folder
```

## Najważniejsze skróty

Na macOS klawiszem `Mod` jest `⌘`, a na Windows `Ctrl`.

| Skrót | Działanie |
| --- | --- |
| `Mod+N` | Nowa notatka |
| `Mod+Shift+N` | Nowy projekt |
| `Mod+S` | Zapisz natychmiast |
| `Mod+F` | Znajdź i zamień w dokumencie |
| `Mod+Shift+F` | Szukaj w całej bibliotece |
| `Mod+1 / 2 / 3` | Graf / notatki / zadania |
| `Mod+Shift+P` | Lista poleceń |
| `Mod+,` | Ustawienia |
| `Mod+/` | Pełna lista skrótów |
