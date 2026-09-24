# Pierwsze uruchomienie MapaNotatek

## Dla użytkownika

1. Pobierz paczkę odpowiednią dla komputera i rozpakuj ZIP.
2. Uruchom `MapaNotatek.app` na macOS albo `MapaNotatek.exe` na Windows.
3. Przy pierwszym starcie aplikacja utworzy lokalną bibliotekę w folderze Dokumenty. Lokalizację można później zmienić w Ustawieniach.
4. Zacznij od „Notatka z szablonu…” albo „Nowa notatka”. Wszystkie zmiany zapisują się automatycznie.
5. W Ustawieniach wybierz „Eksportuj kopię” i wskaż osobny dysk lub nośnik. Kopia zostanie sprawdzona po utworzeniu.

Aplikacja nie wymaga konta, internetu, serwera ani środowiska .NET. Nie synchronizuje danych między komputerami.

## Ostrzeżenie systemowe

Bezpłatna paczka nie ma płatnego certyfikatu Apple/Microsoft. System może więc ostrzec, że wydawca jest nieznany.

Na macOS kliknij aplikację z wciśniętym Control, wybierz „Otwórz”, a następnie potwierdź „Otwórz”. Na Windows użyj „Więcej informacji” i „Uruchom mimo to” tylko wtedy, gdy paczka pochodzi od zaufanej osoby i jej suma SHA-256 jest poprawna.

## Sprawdzenie paczki

Razem z ZIP-em powinien zostać przekazany plik `.zip.sha256`.

macOS:

```bash
shasum -a 256 -c MapaNotatek-osx-arm64.zip.sha256
```

Windows PowerShell:

```powershell
(Get-FileHash .\MapaNotatek-win-x64.zip -Algorithm SHA256).Hash
```

Wartość z PowerShella powinna być taka sama jak pierwsza wartość w pliku `.zip.sha256`.

## Gdzie są dane

- macOS: `~/Documents/MapaNotatek`
- Windows: `%USERPROFILE%\Documents\MapaNotatek`

Zmiana lokalizacji w aplikacji nie przenosi starego folderu automatycznie. Najpierw utwórz kopię i upewnij się, że wskazujesz właściwą bibliotekę.
