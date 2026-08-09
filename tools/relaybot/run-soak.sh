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

    echo "[run-soak] staging server (worktree build + deployed native DLLs)..."
    rm -rf "$STAGE"; mkdir -p "$STAGE"
    cp -r "$REPO/WorldsAdriftRebornGameServer/bin/Release/net6.0/." "$STAGE/"
    # The Windows-native shim and its runtime DLLs are not produced by the
    # managed build on Linux; take them from the deployed server, which
    # deploy-coresdk.sh keeps current with the same shim sources.
    cp "$DEPLOYED"/CoreSdkDll.dll "$DEPLOYED"/lib*.dll "$DEPLOYED"/zlib1.dll "$STAGE/" 2>/dev/null
    [ -f "$STAGE/CoreSdkDll.dll" ] || { echo "[run-soak] no CoreSdkDll.dll found at $DEPLOYED"; exit 2; }

    echo "[run-soak] starting the game server under Wine (log: $SERVER_LOG)..."
    (
        cd "$STAGE" || exit 1
        WINEPREFIX="$HOME/Games/wa-prefix" WINEDEBUG=-all WAREBORN_GAME_PORT="$PORT" \
            WAREBORN_RELAY_V2="$RELAY_V2" \
            wine 'C:\dotnet6\dotnet.exe' WorldsAdriftRebornGameServer.dll > "$SERVER_LOG" 2>&1
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
    --host "$HOST" --port "$PORT" --minutes "$MINUTES" --csv "$CSV" $BOT_FLAGS
VERDICT=$?

echo "[run-soak] CSV: $CSV"
exit $VERDICT
