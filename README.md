# MapaNotatek

Prosta, natywna aplikacja desktopowa Windows 11 do notatek i projektów. Działa całkowicie offline.

## Wymagania

- Windows 11 (x64 albo ARM64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows App SDK 2.4.0 (pobierany automatycznie przez NuGet)
- Visual Studio 2022/2026 z obciążeniem **WinUI application development** albo sam `dotnet` CLI
- Tryb dewelopera w Windows nie jest wymagany (aplikacja jest unpackaged)

Aplikacji **nie da się skompilować ani uruchomić na macOS**. Kod można edytować na Macu, a budować i testować w Windows 11 — w tym w Windows 11 ARM w UTM.

## Uruchomienie

W PowerShell, w katalogu repozytorium:

```powershell
dotnet restore MapaNotatek.sln
dotnet build src\MapaNotatek\MapaNotatek.csproj -c Debug -p:Platform=ARM64
dotnet run --project src\MapaNotatek\MapaNotatek.csproj -c Debug -p:Platform=ARM64
```

Na komputerze x64 użyj `-p:Platform=x64`.

W Visual Studio otwórz `MapaNotatek.sln`, wybierz platformę **ARM64** albo **x64** i naciśnij F5.

## Lokalizacja danych

Domyślny folder:

`%USERPROFILE%\Documents\MapaNotatek`

Struktura:

```text
MapaNotatek/
  Projects/        # pliki .md projektów
  Notes/           # pliki .md notatek
  Trash/           # notatki przeniesione skrótem Delete
  app-state.json   # położenie kulek, zoom, folder danych
```

Każdy projekt i każda notatka to osobny plik Markdown z krótkim frontmatter (`id`, `type`, `tags`, `created`, `modified`). Pliki można otworzyć w Notatniku.

Folder danych można zmienić w **Ustawienia** (`Ctrl+,`). **Eksportuj kopię** kopiuje cały folder do wybranej lokalizacji.

## Budowanie ARM64 i x64

```powershell
dotnet build src\MapaNotatek\MapaNotatek.csproj -c Release -p:Platform=ARM64 -r win-arm64
dotnet build src\MapaNotatek\MapaNotatek.csproj -c Release -p:Platform=x64 -r win-x64
```

Publikacja self-contained (runtime Windows App SDK jest dołączany):

```powershell
dotnet publish src\MapaNotatek\MapaNotatek.csproj -c Release -p:Platform=ARM64 -r win-arm64 --self-contained
dotnet publish src\MapaNotatek\MapaNotatek.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained
```

Wyjście trafia do `src\MapaNotatek\bin\Release\net10.0-windows10.0.26100.0\<rid>\`.

## Skróty

| Skrót | Działanie |
| --- | --- |
| Ctrl+N | Nowa notatka |
| Ctrl+Shift+N | Nowy projekt |
| Ctrl+S | Zapisz natychmiast |
| Ctrl+F | Wyszukiwarka |
| Ctrl+Z | Cofnij |
| Ctrl+Y | Ponów |
| Ctrl+1 | Widok grafu |
| Ctrl+2 | Lista notatek |
| Ctrl+3 | Lista otwartych zadań |
| F6 | Następny panel |
| Shift+F6 | Poprzedni panel |
| Strzałki | Przechodzenie między kulkami (graf) |
| Enter | Otwórz zaznaczoną kulkę |
| Esc | Zamknij prawy panel albo wróć do grafu |
| F2 | Zmień nazwę |
| Ctrl++ | Powiększ graf |
| Ctrl+- | Pomniejsz graf |
| Ctrl+0 | Domyślne powiększenie |
| Ctrl+Shift+P | Lista poleceń |
| Ctrl+/ | Lista skrótów |
| Ctrl+, | Ustawienia |
| Ctrl+W | Zamknij panel edycji |
| Delete | Przenieś notatkę do kosza |

Skróty grafu (strzałki, Enter, F2, Delete) działają, gdy aktywny jest graf albo lista. W edytorze tekstu nie są przechwytywane strzałki, Delete, Backspace, Ctrl+C/X/V/A/Z/Y.

## Kompilacja na macOS (stan środowiska)

Ten kod został przygotowany na macOS. `dotnet restore` z .NET SDK 10.0.400 **zakończył się powodzeniem**.

Obie kompilacje:

```powershell
dotnet build src/MapaNotatek/MapaNotatek.csproj -c Release -p:Platform=ARM64 -r win-arm64
dotnet build src/MapaNotatek/MapaNotatek.csproj -c Release -p:Platform=x64 -r win-x64
```

**nie powiodły się na macOS**, ponieważ kompilator XAML (`XamlCompiler.exe`) to binarka Windows i nie uruchamia się na Darwin (`cannot execute binary file`). Nie da się więc tutaj zweryfikować poprawności kompilacji C#/XAML ani uruchomić aplikacji.

Pełną kompilację ARM64 i x64 oraz testy (w tym Windows 11 ARM w UTM) trzeba wykonać na Windows 11.

