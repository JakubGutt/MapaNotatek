#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
if (-not $root) {
    $root = Get-Location
}

$project = Join-Path $root "src\MapaNotatek\MapaNotatek.csproj"
if (-not (Test-Path $project)) {
    throw "Uruchom ten skrypt z katalogu MapaNotatek-main (obok tego pliku)."
}

Write-Host "SDK: $(dotnet --version)"
Write-Host "Czyszczenie obj/bin..."
Remove-Item -Recurse -Force (Join-Path $root "src\MapaNotatek\bin") -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $root "src\MapaNotatek\obj") -ErrorAction SilentlyContinue

Write-Host "Publikacja self-contained x64 (nie uzywaj dotnet run)..."
dotnet publish $project -c Debug -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exe = Get-ChildItem -Path (Join-Path $root "src\MapaNotatek\bin") -Recurse -Filter "MapaNotatek.exe" |
    Where-Object { $_.DirectoryName -match "win-x64" -and $_.DirectoryName -match "publish" } |
    Select-Object -First 1

if (-not $exe) {
    throw "Nie znaleziono MapaNotatek.exe w katalogu publish."
}

$dll = Join-Path $exe.DirectoryName "Microsoft.ui.xaml.dll"
if (-not (Test-Path $dll)) {
    throw "Brak Microsoft.ui.xaml.dll obok exe. Publish nie dolaczyl WinUI."
}

Write-Host "Start: $($exe.FullName)"
Set-Location $exe.DirectoryName
Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName
Write-Host "Jesli okna nie ma, zainstaluj Windows App Runtime 2.4 x64:"
Write-Host "https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x64.exe"
