#!/usr/bin/env bash
# Build Hoard against the installed game and drop it into BepInEx/plugins/Hoard/.
# Lives in its own repo (github.com/ryanmcgarvey/valheim-hoard); see README.md.
#
#   ./build.sh            build (Release), static patch check, install into the game
#   ./build.sh --no-install   build + check only
#   ./build.sh --clean    wipe bin/obj first
#
# Toolchain: the .NET SDK lives user-locally in ~/.dotnet (installed with the official
# dotnet-install.sh; no sudo needed). The Arch 'dotnet-host' package provides /usr/bin/dotnet
# but no SDK, so this script always calls ~/.dotnet/dotnet explicitly.
set -euo pipefail
cd "$(dirname "$0")"

DOTNET="$HOME/.dotnet/dotnet"
export DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Valheim}"
PLUGIN_DIR="$GAME/BepInEx/plugins/Hoard"
install=1

for arg in "$@"; do
  case "$arg" in
    --no-install) install=0 ;;
    --clean) rm -rf bin obj tools/PatchCheck/bin tools/PatchCheck/obj ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

if [[ ! -x "$DOTNET" ]]; then
  echo "No .NET SDK at ~/.dotnet. Install it user-locally:" >&2
  echo "  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir ~/.dotnet --no-path" >&2
  exit 1
fi
[[ -f "$GAME/valheim_Data/Managed/assembly_valheim.dll" ]] || { echo "Valheim not found at $GAME" >&2; exit 1; }
[[ -f "$GAME/BepInEx/core/BepInEx.dll" ]] || { echo "BepInEx not installed - run: valheim-mods install" >&2; exit 1; }

echo "== build"
"$DOTNET" build -c Release -p:GamePath="$GAME" -v quiet -nologo Hoard.csproj

echo "== patch check (do the Harmony targets still exist in this game build?)"
"$DOTNET" build -c Release -v quiet -nologo tools/PatchCheck/PatchCheck.csproj
"$DOTNET" tools/PatchCheck/bin/Release/net10.0/PatchCheck.dll bin/Release/Hoard.dll "$GAME/valheim_Data/Managed" "$GAME/BepInEx/core" | grep -vE '^  ok'

if [[ $install == 1 ]]; then
  echo "== install -> $PLUGIN_DIR"
  mkdir -p "$PLUGIN_DIR"
  cp bin/Release/Hoard.dll bin/Release/Hoard.pdb "$PLUGIN_DIR/"
  ls -la "$PLUGIN_DIR"
  echo
  echo "Launch the game, then: valheim-mods verify   (and check ~/.config/unity3d/IronGate/Valheim/Player.log for Hoard errors)"
fi
