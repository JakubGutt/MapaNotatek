# Pierwsze uruchomienie MapaNotatek

## Dla użytkownika

1. Pobierz paczkę odpowiednią dla komputera. Na Windows wybierz instalator `MapaNotatek-Setup-win-x64.exe` (większość komputerów) albo `MapaNotatek-Setup-win-arm64.exe`; na macOS rozpakuj ZIP.
2. Uruchom instalator na Windows albo `MapaNotatek.app` na macOS. Przenośny ZIP Windows pozostaje alternatywą bez instalacji.
3. Przy pierwszym starcie aplikacja utworzy lokalną bibliotekę w folderze Dokumenty. Lokalizację można później zmienić w Ustawieniach.
4. Zacznij od „Notatka z szablonu…” albo „Nowa notatka”. Wszystkie zmiany zapisują się automatycznie.
5. Przy zamykaniu wskaż osobny dysk lub nośnik na kopię. Aplikacja zachowa zweryfikowane wersje `Current` i `Previous`. Ręczny eksport z datą nadal jest dostępny w Ustawieniach.

Aplikacja nie wymaga konta, internetu, serwera ani środowiska .NET. Nie synchronizuje danych między komputerami.

## Ostrzeżenie systemowe

Bezpłatna paczka nie ma płatnego certyfikatu Apple/Microsoft. System może więc ostrzec, że wydawca jest nieznany.

Na macOS kliknij aplikację z wciśniętym Control, wybierz „Otwórz”, a następnie potwierdź „Otwórz”. Na Windows użyj „Więcej informacji” i „Uruchom mimo to” tylko wtedy, gdy paczka pochodzi od zaufanej osoby i jej suma SHA-256 jest poprawna.

## Sprawdzenie paczki

Razem z instalatorem lub ZIP-em powinien zostać przekazany odpowiadający mu plik `.sha256`.

macOS:

```bash
shasum -a 256 -c MapaNotatek-osx-arm64.zip.sha256
```

Windows PowerShell:

```powershell
(Get-FileHash .\MapaNotatek-win-x64.zip -Algorithm SHA256).Hash
# albo dla instalatora:
(Get-FileHash .\MapaNotatek-Setup-win-x64.exe -Algorithm SHA256).Hash
```

Wartość z PowerShella powinna być taka sama jak pierwsza wartość w odpowiednim pliku `.sha256`.

## Gdzie są dane

- macOS: `~/Documents/MapaNotatek`
- Windows: `%USERPROFILE%\Documents\MapaNotatek`

Zmiana lokalizacji w aplikacji nie przenosi starego folderu automatycznie. Najpierw utwórz kopię i upewnij się, że wskazujesz właściwą bibliotekę.

Przy zmianie formatu danych aplikacja tworzy zmigrowany katalog obok dotychczasowego. Nie usuwaj starej biblioteki ani starszej wersji aplikacji, dopóki nie sprawdzisz najważniejszych notatek i projektów w nowej wersji.
