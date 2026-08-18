#!/usr/bin/env bash
# Isolated real-wire two-peer ship acceptance. Never attaches to production and
# never reads the operator's world-state: it creates a fresh data directory with
# one disposable minimum hull, deck and mounted helm, starts the CURRENT native
# server build on an alternate UDP port, then drives it with two ENet bots.
set -euo pipefail

port="${1:-17779}"
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
run="$here/run"
stamp="$(date +%Y%m%d-%H%M%S)"
mkdir -p "$run"
stage="$(mktemp -d "$run/ship-acceptance-$stamp-XXXXXX")"
data="$stage/data"
server_log="$run/ship-acceptance-server-$stamp.log"
bot_log="$run/ship-acceptance-bots-$stamp.log"
mkdir -p "$data"

if ss -Huln "sport = :$port" | grep -q .; then
    echo "[ship-acceptance] UDP $port is already in use; refusing to attach to an unknown server."
    exit 2
fi

server_pid=""
cleanup() {
    if [ -n "$server_pid" ] && kill -0 "$server_pid" 2>/dev/null; then
        kill "$server_pid" 2>/dev/null || true
        wait "$server_pid" 2>/dev/null || true
    fi
}
# INT TERM as well: a `timeout`-wrapped run dies by SIGTERM, and bash does NOT
# run the EXIT trap on an unhandled signal - which is how 8 servers leaked
# (some for 8 hours) on 2026-08-18. Trapping the signals makes cleanup run,
# and the explicit `exit` inside then fires EXIT exactly once.
trap 'cleanup; trap - EXIT; exit 143' INT TERM
trap cleanup EXIT

echo "[ship-acceptance] building current server, bot and native shim..."
dotnet build "$repo/WorldsAdriftRebornGameServer" -c Release > "$run/ship-server-build-$stamp.log" 2>&1
if [ ! -f "$here/build-native/libCoreSdkDll.so" ]; then
    "$here/build-coresdk-native.sh" > "$run/ship-native-build-$stamp.log" 2>&1
fi
dotnet build "$here/RelayBot" -c Release > "$run/ship-bot-build-$stamp.log" 2>&1

cp -a "$repo/WorldsAdriftRebornGameServer/bin/Release/net6.0/." "$stage/"
cp "$here/build-native/libCoreSdkDll.so" "$stage/"

# Haven spawn +12 m north. The mounted helm is one metre forward in hull-local
# space. HullBytes is ShipHull.MinimumHullDataBase64; every other list is empty.
jq -n --arg hull 'AQAAAAAA6AAAGAAA6AAAGAAAAAAAAAHoAAAYAADoAAAYAAAAAAAA' '{
  PlacedDeployables: [],
  BuiltShips: [{
    Salvaged: false,
    HullX: 70502113, HullY: -1273730, HullZ: -4580013,
    HullYawRadians: 0,
    HullBytes: $hull,
    OwnerCharacterUid: "relaybot-fixture",
    ShipyardX: 0, ShipyardY: 0, ShipyardZ: 0
  }],
  MountedParts: [{
    PartUid: "relaybot-helm",
    BuiltShipIndex: 0,
    SchematicId: "helm",
    ItemType: "helm",
    Title: "Helm",
    PrefabName: "Helm01",
    AttachmentType: "deck",
    PartSpecificComponents: [],
    LocalX: 0, LocalY: 0, LocalZ: 4096,
    PackedRotation: 1023,
    OwnerCharacterUid: "relaybot-fixture",
    SailUnfurled: false,
    LampOff: false
  }],
  LooseParts: []
}' > "$data/world-state.json"

echo "[ship-acceptance] starting isolated native server on UDP $port..."
(
    cd "$stage"
    WAREBORN_GAME_PORT="$port" \
    WAREBORN_DATA_DIR="$data" \
    WAREBORN_STATIC_SHIP=0 \
    WAREBORN_SPAWN_TREE=1 \
    WAREBORN_TREE_COUNT=1 \
    WAREBORN_SPAWN_METAL=0 \
    WAREBORN_SPAWN_FUELPODS=0 \
    WAREBORN_METAL_HANDSHAKE=0 \
    WAREBORN_INTEREST_RADIUS_M=250 \
    WAREBORN_INTEREST_INITIAL_RADIUS_M=45 \
    WAREBORN_HELM_FLIGHT=1 \
    WAREBORN_LOAD_BARRIER=0 \
    WAREBORN_RELAY_V2=1 \
    DOTNET_ROLL_FORWARD=Major \
    dotnet WorldsAdriftRebornGameServer.dll > "$server_log" 2>&1
) &
server_pid="$!"

for _ in $(seq 1 120); do
    if ss -Huln "sport = :$port" | grep -q .; then break; fi
    if ! kill -0 "$server_pid" 2>/dev/null; then
        echo "[ship-acceptance] server exited during startup; log tail:"
        tail -40 "$server_log"
        exit 2
    fi
    sleep 0.25
done
if ! ss -Huln "sport = :$port" | grep -q .; then
    echo "[ship-acceptance] server did not bind; log tail:"
    tail -40 "$server_log"
    exit 2
fi

echo "[ship-acceptance] running two real ENet peers..."
set +e
dotnet "$here/RelayBot/bin/Release/net8.0/RelayBot.dll" \
    --host 127.0.0.1 --port "$port" --setup-timeout 90 \
    --rewritten-1073 --ship-acceptance 2>&1 | tee "$bot_log"
result="${PIPESTATUS[0]}"
set -e

if [ "$result" -ne 0 ]; then
    echo "[ship-acceptance] FAILED. Server log: $server_log"
    tail -80 "$server_log"
    exit "$result"
fi

echo "[ship-acceptance] PASS. Bot log: $bot_log"
echo "[ship-acceptance] Server log: $server_log"
