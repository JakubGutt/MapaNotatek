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

Na Windows 11 x64 **nie używaj** `dotnet run`. Uruchamia to proces z `dotnet.exe`, a biblioteki WinUI leżą obok `.exe` — okno wtedy często w ogóle nie wstaje.

1. Pobierz **świeży** ZIP z `main` (nie dokładaj na stary folder z `obj`).
2. (Zalecane) zainstaluj [Windows App Runtime 2.4 x64](https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x64.exe).
3. W PowerShell, w katalogu rozpakowanego repo:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\run-windows.ps1
```

Skrypt czyści `obj`/`bin`, publikuje self-contained x64 i odpala `MapaNotatek.exe` z katalogu, w którym leży `Microsoft.ui.xaml.dll`.

Na ARM64:

```powershell
dotnet publish src\MapaNotatek\MapaNotatek.csproj -c Debug -p:Platform=ARM64 -r win-arm64 --self-contained true -p:WindowsAppSDKSelfContained=true
```

Potem uruchom `MapaNotatek.exe` z folderu `publish`.

W Visual Studio otwórz `MapaNotatek.sln`, platforma **x64** albo **ARM64**, F5.

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

