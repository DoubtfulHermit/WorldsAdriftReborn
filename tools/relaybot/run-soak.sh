#!/bin/bash
# Two-bot relay staleness soak, one command.
#
#   tools/relaybot/run-soak.sh [minutes] [port] [host]
#
# Defaults: 10 minutes, port 7777, host 127.0.0.1.
#
# What it does:
#   1. Builds the game server from THIS worktree and the bot harness.
#   2. If nothing is listening on the target UDP port (and the host is local),
#      stages the freshly built server next to the deployed native DLLs and
#      starts it under Wine - so the soak always measures the CURRENT server
#      code, never whatever happened to be deployed.
#   3. Runs two headless bots for the requested minutes; they join, circle the
#      spawn, and measure per-packet end-to-end staleness of each other's
#      relayed movement.
#   4. Writes tools/relaybot/run/soak-<timestamp>.csv, prints a FLAT/GROWING
#      verdict, and kills ONLY what it started - identified by the UDP port it
#      holds, never by process-name patterns (which have self-matched before).
#
# Exit code: 0 flat, 1 growing, 2 the soak never produced data.
#
# FAUNA GATE: SOAK_FAUNA=1 tools/relaybot/run-soak.sh 10 7807
#   Production world recipe + bots standing on a tier-1 island + --require-fauna,
#   so the verdict covers the fauna checkout/pose path and a creatureless run
#   FAILS instead of reporting FLAT. See the SOAK_FAUNA block below.
set -uo pipefail

MINUTES="${1:-10}"
PORT="${2:-7777}"
HOST="${3:-127.0.0.1}"

# Relay mode, forwarded to BOTH sides so they cannot disagree: the server gets
# WAREBORN_RELAY_V2 verbatim, and under v2 (anything but 0, the server's own
# default) the bots get --rewritten-1073, because relayed 1073 timestamps are
# then server-issued synthetic stamps - verifiable for monotonicity, not
# matchable to sends. A/B: WAREBORN_RELAY_V2=0 tools/relaybot/run-soak.sh ...
# CAVEAT: when the script attaches to an ALREADY-RUNNING server it cannot know
# that server's mode; it assumes this same variable describes it.
RELAY_V2="${WAREBORN_RELAY_V2:-1}"
BOT_FLAGS=""
[ "$RELAY_V2" != "0" ] && BOT_FLAGS="--rewritten-1073"

# Extra bot flags, forwarded verbatim. The one that matters is --centre X,Y,Z:
# the bots otherwise circle the HAVEN SPAWN, which is 3.8 km from the nearest
# release-world island, so any island-scoped feature (island fauna above all) is
# simply absent from the measurement. A soak that reports FLAT while carrying
# zero creatures is not a fauna gate, and this repo has produced one.
#   SOAK_BOT_EXTRA="--centre 7376.4,25.2,6231.7" tools/relaybot/run-soak.sh 10 7804
BOT_EXTRA="${SOAK_BOT_EXTRA:-}"

# FAUNA GATE MODE (SOAK_FAUNA=1). Stands the bots on a tier-1 island, starts
# the server with the production world recipe, and makes a run that never shows
# a bot a creature FAIL (--require-fauna) instead of printing a confident FLAT.
#
# THE 2026-08-18 "KNOWN GAP" IS SOLVED, and the answer was an env variable, not
# the harness: fauna is gated on optional-terrain readiness, and optional
# terrain is only STREAM-MANAGED for islands whose world entity id was already
# bound when IslandTerrainInterestService was constructed at boot. Entity ids
# are normally allocated lazily, by the first client to reach that entity's
# AddEntity step - hours after the constructor ran - so without the loading
# barrier the service saw zero registered islands, managed nothing, and
# IsTerrainReady() answered false forever: no "[terrain-interest] added" at any
# radius, fauna silently withheld, exactly as the gap note recorded.
# WAREBORN_LOAD_BARRIER=1 changes the boot order: LoadBarrier.Prime BINDS EVERY
# WORLD ENTITY ID before the terrain service is constructed, the islands
# register as managed candidates, terrain checks out to a bot (via the bounded
# ack fallback - a headless bot's 1-byte ack never exactly correlates), and the
# fauna path runs for real. Production runs with the barrier on, so this mode
# is the production shape, not a test hack. Verified 2026-08-18: A/B on this
# exact worktree - barrier off: 0 creature checkouts; barrier on: 40 checkouts,
# 18,582 fauna 190602 poses, VERDICT FLAT.
if [ "${SOAK_FAUNA:-0}" = "1" ]; then
    BOT_FLAGS="$BOT_FLAGS --require-fauna"
    [ -z "$BOT_EXTRA" ] && BOT_EXTRA="--centre 7376.4,25.2,6231.7"  # beautiful-wildlands, tier 1
    # The production world recipe (mirrors the deployed unit), overridable var
    # by var. Only effective when THIS script starts the server below; when a
    # server is already listening its env is its own.
    export WAREBORN_RELEASE_WORLD_DISTRICTS="${WAREBORN_RELEASE_WORLD_DISTRICTS:-tier1}"
    export WAREBORN_INTEREST_RADIUS_M="${WAREBORN_INTEREST_RADIUS_M:-120}"
    export WAREBORN_TERRAIN_INTEREST_ENABLED="${WAREBORN_TERRAIN_INTEREST_ENABLED:-1}"
    export WAREBORN_TERRAIN_LOAD_RADIUS_M="${WAREBORN_TERRAIN_LOAD_RADIUS_M:-4000}"
    export WAREBORN_TERRAIN_UNLOAD_RADIUS_M="${WAREBORN_TERRAIN_UNLOAD_RADIUS_M:-4800}"
    export WAREBORN_ISLAND_FAUNA="${WAREBORN_ISLAND_FAUNA:-1}"
    export WAREBORN_ISLAND_FAUNA_MAX="${WAREBORN_ISLAND_FAUNA_MAX:-4000}"
    export WAREBORN_LOAD_BARRIER="${WAREBORN_LOAD_BARRIER:-1}"   # load-bearing: see above
    export WAREBORN_LOAD_BARRIER_TIMEOUT_MS="${WAREBORN_LOAD_BARRIER_TIMEOUT_MS:-30000}"
    export WAREBORN_SPAWN_PACE_MS="${WAREBORN_SPAWN_PACE_MS:-200}"
    if port_listening_precheck=$(ss -Huln "sport = :$PORT" 2>/dev/null) && [ -n "$port_listening_precheck" ]; then
        echo "[run-soak] SOAK_FAUNA=1 but a server already holds UDP $PORT - its env decides whether fauna exists; --require-fauna will judge the result."
    fi
fi

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
RUN="$HERE/run"
STAGE="$RUN/gameserver"
DEPLOYED="$HOME/Games/WAReborn-servers/WorldsAdriftRebornGameServer"
STAMP="$(date +%Y%m%d-%H%M%S)"
CSV="$RUN/soak-$STAMP.csv"
SERVER_LOG="$RUN/gameserver-$STAMP.log"
mkdir -p "$RUN"

port_listening() {
    [ -n "$(ss -Huln "sport = :$PORT" 2>/dev/null)" ]
}

# PIDs currently holding the UDP port. This is the ONLY process identification
# used for cleanup: an orphaned wine dotnet.exe holding the port has burned
# this project before, and pgrep-by-name has self-matched before.
pids_on_port() {
    ss -Hulpn "sport = :$PORT" 2>/dev/null | grep -oP 'pid=\K[0-9]+' | sort -u
}

STARTED_SERVER=0
cleanup() {
    if [ "$STARTED_SERVER" = 1 ]; then
        echo "[run-soak] stopping the server this script started (by held UDP port $PORT)..."
        local pids
        pids="$(pids_on_port)"
        if [ -n "$pids" ]; then
            kill $pids 2>/dev/null
            for _ in $(seq 1 20); do
                sleep 0.5
                [ -z "$(pids_on_port)" ] && break
            done
            pids="$(pids_on_port)"
            if [ -n "$pids" ]; then
                echo "[run-soak] port still held, sending SIGKILL."
                kill -9 $pids 2>/dev/null
            fi
        fi
        echo "[run-soak] server log kept at $SERVER_LOG"
    fi
}
trap cleanup EXIT

echo "[run-soak] building the bot harness..."
dotnet build "$HERE/RelayBot" -c Release > "$RUN/bot-build-$STAMP.log" 2>&1 \
    || { echo "[run-soak] bot build FAILED, see $RUN/bot-build-$STAMP.log"; exit 2; }

if [ ! -f "$HERE/build-native/libCoreSdkDll.so" ]; then
    echo "[run-soak] building the native CoreSdk shim..."
    "$HERE/build-coresdk-native.sh" > "$RUN/coresdk-build-$STAMP.log" 2>&1 \
        || { echo "[run-soak] native shim build FAILED, see $RUN/coresdk-build-$STAMP.log"; exit 2; }
    # The csproj copies the .so at build time; rebuild so the output picks it up.
    dotnet build "$HERE/RelayBot" -c Release >> "$RUN/bot-build-$STAMP.log" 2>&1
fi

if port_listening; then
    echo "[run-soak] something already listens on UDP $PORT - using it, and NOT killing it afterwards."
elif [ "$HOST" != "127.0.0.1" ] && [ "$HOST" != "localhost" ]; then
    echo "[run-soak] remote host requested and nothing local to start; proceeding against $HOST:$PORT."
else
    echo "[run-soak] no server on UDP $PORT - building the CURRENT worktree server..."
    dotnet build "$REPO/WorldsAdriftRebornGameServer" -c Release > "$RUN/server-build-$STAMP.log" 2>&1 \
        || { echo "[run-soak] server build FAILED, see $RUN/server-build-$STAMP.log"; exit 2; }

    # NATIVE, not Wine. This used to stage the WINDOWS CoreSdkDll.dll from the
    # old Wine deployment and run the server under wine. Production moved to a
    # native Linux server months ago, and that Windows shim predates the
    # channel-count export the server now calls on every connect - so the soak
    # died with
    #   EntryPointNotFoundException: ENet_EXP_PeerChannelCount in DLL 'CoreSdkDll'
    # the instant a bot connected. The gate was measuring nothing and had been
    # unable to measure anything since the native migration. run-ship-acceptance.sh
    # already builds and runs natively; this now does the same thing.
    echo "[run-soak] staging server (worktree build + native shim)..."
    rm -rf "$STAGE"; mkdir -p "$STAGE"
    cp -r "$REPO/WorldsAdriftRebornGameServer/bin/Release/net6.0/." "$STAGE/"
    if [ ! -f "$HERE/build-native/libCoreSdkDll.so" ]; then
        echo "[run-soak] building the native shim (first run only)..."
        "$HERE/build-coresdk-native.sh" > "$RUN/soak-native-build-$STAMP.log" 2>&1 \
            || { echo "[run-soak] native shim build FAILED, see $RUN/soak-native-build-$STAMP.log"; exit 2; }
    fi
    cp "$HERE/build-native/libCoreSdkDll.so" "$STAGE/" \
        || { echo "[run-soak] no libCoreSdkDll.so to stage"; exit 2; }

    echo "[run-soak] starting the game server natively (log: $SERVER_LOG)..."
    (
        cd "$STAGE" || exit 1
        WAREBORN_GAME_PORT="$PORT" WAREBORN_RELAY_V2="$RELAY_V2" \
            DOTNET_ROLL_FORWARD=Major \
            dotnet WorldsAdriftRebornGameServer.dll > "$SERVER_LOG" 2>&1
    ) &
    STARTED_SERVER=1

    echo "[run-soak] waiting for UDP $PORT to come up..."
    UP=0
    for _ in $(seq 1 180); do
        sleep 0.5
        if port_listening; then UP=1; break; fi
    done
    if [ "$UP" != 1 ]; then
        echo "[run-soak] server never bound UDP $PORT; tail of its log:"
        tail -20 "$SERVER_LOG"
        exit 2
    fi
    echo "[run-soak] server is up."
fi

echo "[run-soak] running two bots for $MINUTES minute(s) (relay mode: $([ "$RELAY_V2" != "0" ] && echo v2 || echo raw))..."
dotnet "$HERE/RelayBot/bin/Release/net8.0/RelayBot.dll" \
    --host "$HOST" --port "$PORT" --minutes "$MINUTES" --csv "$CSV" $BOT_FLAGS $BOT_EXTRA
VERDICT=$?

echo "[run-soak] CSV: $CSV"
exit $VERDICT
