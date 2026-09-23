# MapaNotatek

Prosta, natywna aplikacja desktopowa do notatek i projektów. Działa całkowicie offline.

Jeden kod źródłowy (Avalonia UI) — **macOS** i **Windows** (także Linux).

## Wymagania (deweloper)

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- macOS 12+ albo Windows 10/11

## Uruchomienie (dev)

### macOS

```bash
chmod +x ./run-macos.sh ./publish-macos.sh
./run-macos.sh
```

### Windows

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\run-windows.ps1
```

Albo na obu:

```bash
dotnet run --project src/MapaNotatek/MapaNotatek.csproj
```

## Deploy / publikacja (dla użytkowników końcowych)

Self-contained — odbiorca **nie musi** mieć zainstalowanego .NET.

### macOS

```bash
./publish-macos.sh            # arm64 albo x64 wg maszyny
./publish-macos.sh osx-arm64  # Apple Silicon
./publish-macos.sh osx-x64    # Intel
```

Wynik: `artifacts/osx-arm64/MapaNotatek` (albo `osx-x64`).

Pierwsze uruchomienie z Finder czasem wymaga: PPM → Otwórz (Gatekeeper).

### Windows

```powershell
.\publish-windows.ps1           # x64 albo arm64 wg maszyny
.\publish-windows.ps1 win-x64
.\publish-windows.ps1 win-arm64
```

Wynik: `artifacts\win-x64\MapaNotatek.exe`.

CI (GitHub Actions) buduje i wrzuca artefakty dla `osx-arm64` oraz `win-x64` przy każdym pushu/PR.

## Lokalizacja danych

| Platforma | Domyślny folder |
| --- | --- |
| macOS / Linux | `~/Documents/MapaNotatek` |
| Windows | `%USERPROFILE%\Documents\MapaNotatek` |

```text
MapaNotatek/
  Projects/        # pliki .md projektów
  Notes/           # pliki .md notatek
  Trash/           # notatki w koszu
  app-state.json   # położenie kulek, zoom, folder danych
```

Folder danych i eksport kopii: **Ustawienia** (`⌘,` / `Ctrl+,`).

## Skróty

Na macOS: **⌘**, na Windows/Linux: **Ctrl**.

| Skrót | Działanie |
| --- | --- |
| Mod+N | Nowa notatka |
| Mod+Shift+N | Nowy projekt |
| Mod+S | Zapisz |
| Mod+F | Szukaj |
| Mod+Z | Cofnij |
| Mod+Shift+Z / Ctrl+Y | Ponów |
| Mod+1 / 2 / 3 | Graf / Notatki / Zadania |
| F6 | Następny panel |
| Esc | Zamknij panel |
| F2 | Zmień nazwę |
| Mod++ / Mod+- / Mod+0 | Zoom |
| Mod+Shift+P | Polecenia |
| Mod+/ | Skróty |
| Mod+, | Ustawienia |
| Mod+W | Zamknij panel edycji |
| Delete / ⌫ | Kosz (notatka) |
