#Requires -Version 5.1
# Publikuje self-contained build pod Windows.
# Użycie:
#   .\publish-windows.ps1              # auto: x64 albo arm64
#   .\publish-windows.ps1 win-x64
#   .\publish-windows.ps1 win-arm64
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
if (-not $root) { $root = Get-Location }

$project = Join-Path $root "src\MapaNotatek\MapaNotatek.csproj"
if (-not (Test-Path $project)) {
    throw "Uruchom ten skrypt z katalogu repozytorium MapaNotatek."
}
$version = (& dotnet msbuild $project -getProperty:Version -nologo | Select-Object -Last 1).Trim()

$rid = $args[0]
if (-not $rid) {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
        $rid = "win-arm64"
    } else {
        $rid = "win-x64"
    }
}
if ($rid -notin @("win-x64", "win-arm64")) {
    throw "Obsługiwane warianty Windows to win-x64 i win-arm64."
}

$out = Join-Path $root "artifacts\$rid"
$zip = Join-Path $root "artifacts\MapaNotatek-$rid.zip"
$checksum = "$zip.sha256"
$installer = Join-Path $root "artifacts\MapaNotatek-Setup-$rid.exe"
$installerChecksum = "$installer.sha256"
Write-Host "SDK: $(dotnet --version)"
Write-Host "Publish → $out (RID=$rid, self-contained)"

if (Test-Path $out) {
    Remove-Item -Recurse -Force $out
}
if (Test-Path $zip) {
    Remove-Item -Force $zip
}
if (Test-Path $checksum) {
    Remove-Item -Force $checksum
}
if (Test-Path $installer) {
    Remove-Item -Force $installer
}
if (Test-Path $installerChecksum) {
    Remove-Item -Force $installerChecksum
}

dotnet publish $project `
  -c Release `
  -r $rid `
  --no-restore `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $out

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Symbole debugowania bibliotek natywnych nie są potrzebne użytkownikowi,
# a bez kompresji zajmują około 100 MB.
Get-ChildItem -Path $out -Filter "*.pdb" -File | Remove-Item -Force

Copy-Item (Join-Path $root "docs\FIRST_RUN.md") (Join-Path $out "FIRST_RUN.md")
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
Set-Content -Path $checksum -Encoding ascii -Value "$hash  $(Split-Path -Leaf $zip)"

$isccCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path $_) }

if ($isccCandidates.Count -gt 0) {
    $env:MAPANOTATKI_VERSION = $version
    $env:MAPANOTATKI_PUBLISH_DIR = $out
    $env:MAPANOTATKI_RID = $rid
    $env:MAPANOTATKI_ALLOWED_ARCH = if ($rid -eq "win-arm64") { "arm64" } else { "x64compatible" }
    & $isccCandidates[0] (Join-Path $root "packaging\windows\MapaNotatek.iss")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $installerHash = (Get-FileHash -Algorithm SHA256 $installer).Hash.ToLowerInvariant()
    Set-Content -Path $installerChecksum -Encoding ascii -Value "$installerHash  $(Split-Path -Leaf $installer)"
} else {
    Write-Warning "Nie znaleziono Inno Setup. Powstała paczka ZIP, ale instalator EXE nie został zbudowany."
}

Write-Host ""
Write-Host "Gotowe: $out\MapaNotatek.exe"
Write-Host "ZIP do przekazania: $zip"
Write-Host "Suma SHA-256: $checksum"
if (Test-Path $installer) {
    Write-Host "Instalator: $installer"
    Write-Host "Suma instalatora: $installerChecksum"
}
Write-Host "Uruchom: & '$out\MapaNotatek.exe'"
