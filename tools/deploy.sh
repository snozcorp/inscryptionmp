#!/usr/bin/env bash
# Build the plugin and install it into every Inscryption copy we test against.
#
#   tools/deploy.sh          build + deploy
#   tools/deploy.sh --clean  also wipe the trace logs first
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

INSTALLS=(
  "/c/Steam/steamapps/common/Inscryption"
  "/c/Games/InscryptionClient2"
)

echo "==> building"
dotnet build "$ROOT/src/InscryptionMP.csproj" -v q --nologo | tail -3

DLL="$ROOT/src/bin/Debug/net472/InscryptionMP.dll"
[ -f "$DLL" ] || { echo "build produced no dll"; exit 1; }

for install in "${INSTALLS[@]}"; do
  if [ ! -d "$install" ]; then
    echo "==> skipping (missing): $install"
    continue
  fi

  mkdir -p "$install/BepInEx/plugins"
  cp "$DLL" "$install/BepInEx/plugins/"

  # Needed so SteamAPI.Init() works when the exe is launched outside Steam.
  if [ ! -f "$install/steam_appid.txt" ]; then
    printf '1092790' > "$install/steam_appid.txt"
    echo "    wrote steam_appid.txt"
  fi

  if [ "${1:-}" = "--clean" ]; then
    rm -f "$install/BepInEx/mp-trace.log"
  fi

  echo "==> deployed to $install"
done

echo "done."
