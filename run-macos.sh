#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
project="$root/src/MapaNotatek/MapaNotatek.csproj"
debug_binary="$root/src/MapaNotatek/bin/Debug/net10.0/MapaNotatek"

if [[ "$(uname -m)" == "arm64" ]]; then
  rid="osx-arm64"
else
  rid="osx-x64"
fi

app="$root/artifacts/$rid/MapaNotatek.app"
app_binary="$app/Contents/MacOS/MapaNotatek"

if [[ ! -f "$project" ]]; then
  echo "Uruchom ten skrypt z katalogu repozytorium MapaNotatek." >&2
  exit 1
fi

if [[ -x /opt/homebrew/opt/dotnet/libexec/dotnet ]]; then
  export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
  export PATH="$DOTNET_ROOT:$PATH"
elif [[ -x /usr/local/share/dotnet/dotnet ]]; then
  export DOTNET_ROOT="/usr/local/share/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi

for executable in "$debug_binary" "$app_binary"; do
  process_pattern="$executable( .*)?"
  pkill -TERM -f -x "$process_pattern" 2>/dev/null || true
  for _ in {1..20}; do
    if ! pgrep -f -x "$process_pattern" >/dev/null 2>&1; then
      break
    fi
    sleep 0.1
  done

  if pgrep -f -x "$process_pattern" >/dev/null 2>&1; then
    echo "Nie udało się zamknąć poprzedniej instancji MapaNotatek." >&2
    exit 1
  fi
done

echo "SDK: $(dotnet --version)"
echo "Budowanie najnowszego pakietu MapaNotatek…"
"$root/publish-macos.sh" "$rid"
echo "Uruchamianie jednej, świeżej instancji: $app"
open "$app"
