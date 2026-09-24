#!/usr/bin/env bash
# Publikuje self-contained build pod macOS.
# Użycie:
#   ./publish-macos.sh              # auto: arm64 albo x64
#   ./publish-macos.sh osx-arm64
#   ./publish-macos.sh osx-x64
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
project="$root/src/MapaNotatek/MapaNotatek.csproj"

if [[ -x /opt/homebrew/opt/dotnet/libexec/dotnet ]]; then
  export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
  export PATH="$DOTNET_ROOT:$PATH"
elif [[ -x /usr/local/share/dotnet/dotnet ]]; then
  export DOTNET_ROOT="/usr/local/share/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi

arch="$(uname -m)"
if [[ "${1:-}" != "" ]]; then
  rid="$1"
elif [[ "$arch" == "arm64" ]]; then
  rid="osx-arm64"
else
  rid="osx-x64"
fi

out="$root/artifacts/$rid"
publish_dir="$out/publish"
app="$out/MapaNotatek.app"
zip="$root/artifacts/MapaNotatek-$rid.zip"
checksum="$zip.sha256"
echo "SDK: $(dotnet --version)"
echo "Publish → $out (RID=$rid, self-contained)"
rm -rf "$out"
rm -f "$zip"
rm -f "$checksum"
dotnet publish "$project" \
  -c Release \
  -r "$rid" \
  --no-restore \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$publish_dir"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish_dir"/. "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/MapaNotatek"
cp "$root/packaging/macos/Info.plist" "$app/Contents/Info.plist"

# Bezpłatny podpis ad-hoc zapewnia integralność pakietu. Nie zastępuje płatnego
# certyfikatu Developer ID ani notaryzacji Apple.
codesign --force --deep --sign - "$app"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
zip -q -j "$zip" "$root/docs/FIRST_RUN.md"
shasum -a 256 "$zip" > "$checksum"
rm -rf "$publish_dir"

echo ""
echo "Gotowe: $app"
echo "ZIP do przekazania: $zip"
echo "Suma SHA-256: $checksum"
echo "Uruchom: open \"$app\""
