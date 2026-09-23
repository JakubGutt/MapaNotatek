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

$rid = $args[0]
if (-not $rid) {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
        $rid = "win-arm64"
    } else {
        $rid = "win-x64"
    }
}

$out = Join-Path $root "artifacts\$rid"
Write-Host "SDK: $(dotnet --version)"
Write-Host "Publish → $out (RID=$rid, self-contained)"

if (Test-Path $out) {
    Remove-Item -Recurse -Force $out
}

dotnet publish $project `
  -c Release `
  -r $rid `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $out

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Gotowe: $out\MapaNotatek.exe"
Write-Host "Uruchom: & '$out\MapaNotatek.exe'"
