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
echo "SDK: $(dotnet --version)"
echo "Publish → $out (RID=$rid, self-contained)"
rm -rf "$out"
dotnet publish "$project" \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$out"

echo ""
echo "Gotowe: $out/MapaNotatek"
echo "Uruchom: open \"$out/MapaNotatek\"   albo   \"$out/MapaNotatek\""
