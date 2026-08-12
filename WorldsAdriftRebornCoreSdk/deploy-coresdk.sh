#!/bin/bash
# Deploy the mingw-built CoreSdkDll.dll to both places that load it.
#
# The SAME binary is used by the game client (as its SpatialOS SDK replacement)
# and by the game server, so both must be updated together.
#
# Runtime DLL placement matters: Windows resolves a DLL's dependencies against
# the EXECUTABLE's directory, not the DLL's own. So protobuf/abseil/libstdc++ go
# to the game root (for UnityClient@Windows.exe) and to the server folder (which
# is the server's working directory).
set -euo pipefail

BUILD="$HOME/Games/WAReborn-src/WorldsAdriftRebornCoreSdk/build-mingw"
GAME="$HOME/Games/WorldsAdrift"
PLUGIN="$GAME/BepInEx/plugins/WorldsAdriftReborn"
SERVER="$HOME/Games/WAReborn-servers/WorldsAdriftRebornGameServer"

[ -f "$BUILD/CoreSdkDll.dll" ] || { echo "build first: WorldsAdriftRebornCoreSdk/build-mingw.sh"; exit 1; }

backup() {
    if [ -f "$1" ] && [ ! -f "$1.orig-2023" ]; then
        cp "$1" "$1.orig-2023"
        echo "  backed up $(basename "$1")"
    fi
}

echo "==> backing up the original MSVC DLLs (once)"
backup "$PLUGIN/CoreSdkDll.dll"
backup "$SERVER/CoreSdkDll.dll"

echo "==> deploying CoreSdkDll.dll"
cp -f "$BUILD/CoreSdkDll.dll" "$PLUGIN/CoreSdkDll.dll"
cp -f "$BUILD/CoreSdkDll.dll" "$SERVER/CoreSdkDll.dll"

echo "==> deploying runtime dependencies"
n=0
for dll in "$BUILD"/*.dll; do
    [ "$(basename "$dll")" = "CoreSdkDll.dll" ] && continue
    cp -f "$dll" "$GAME/"      # for the client executable
    cp -f "$dll" "$SERVER/"    # for the server working directory
    n=$((n+1))
done
echo "  $n runtime DLLs -> game root and server folder"

echo "==> done"
md5sum "$PLUGIN/CoreSdkDll.dll" "$SERVER/CoreSdkDll.dll"
