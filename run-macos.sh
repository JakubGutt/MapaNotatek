#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
project="$root/src/MapaNotatek/MapaNotatek.csproj"

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

echo "SDK: $(dotnet --version)"
echo "Uruchamianie MapaNotatek…"
dotnet run --project "$project" -c Debug
