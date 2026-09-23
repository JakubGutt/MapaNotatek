#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
if (-not $root) {
    $root = Get-Location
}

$project = Join-Path $root "src\MapaNotatek\MapaNotatek.csproj"
if (-not (Test-Path $project)) {
    throw "Uruchom ten skrypt z katalogu repozytorium MapaNotatek (obok tego pliku)."
}

Write-Host "SDK: $(dotnet --version)"
Write-Host "Uruchamianie MapaNotatek…"
dotnet run --project $project -c Debug
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
