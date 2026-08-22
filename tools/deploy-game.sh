#!/usr/bin/env bash
#
# Safely deploy the native Linux game server without touching persistent state.
#
# The native deploy directory contains two things dotnet publish does not:
#   * data -> ../WorldsAdriftRebornGameServer/data (the durable world state)
#   * libCoreSdkDll.so (the separately built ENet/native bridge)
# A raw `rsync --delete` removed both on 2026-08-22 and booted an empty world.
# This script makes those invariants executable and fails before restarting if
# any of them changes.
#
#   tools/deploy-game.sh            # build, gate, back up, deploy, restart, verify
#   tools/deploy-game.sh --dry-run  # build and read-only production gates only
set -euo pipefail

HOST="${WAREBORN_HOST:-root@62.171.161.19}"
LIVE="/opt/wareborn/WorldsAdriftRebornGameServer-native"
DATA="/opt/wareborn/WorldsAdriftRebornGameServer/data"
UNIT="wareborn-game"

dry=0
[ "${1:-}" = "--dry-run" ] && dry=1

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

stage="$(mktemp -d /tmp/wareborn-game-native.XXXXXX)"
trap 'rm -rf "$stage"' EXIT

echo "==> publish"
dotnet publish WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.csproj \
  -c Release -r linux-x64 --self-contained true -o "$stage" 2>&1 | tail -1

# A publish must never supply a competing data entry. Excluding it from rsync is
# defence in depth; refusing it here also catches a future project-file change.
if [ -e "$stage/data" ] || [ -L "$stage/data" ]; then
  echo "REFUSING: publish stage unexpectedly contains data/." >&2
  exit 1
fi

echo "==> production safety gates"
read -r players state_hash state_counts <<<"$(ssh "$HOST" "set -e
  test \"\$(readlink -f '$LIVE/data')\" = '$DATA'
  test -f '$DATA/world-state.json'
  test -f '$LIVE/libCoreSdkDll.so'
  players=\$(jq -r '(.players // []) | length' /tmp/wareborn-stats.json 2>/dev/null || echo unknown)
  hash=\$(sha256sum '$DATA/world-state.json' | awk '{print \$1}')
  counts=\$(jq -r '[((.PlacedDeployables // [])|length), ((.BuiltShips // [])|length), ((.MountedParts // [])|length), ((.LooseParts // [])|length)] | @csv' '$DATA/world-state.json')
  printf '%s %s %s\\n' \"\$players\" \"\$hash\" \"\$counts\""
)"

if [ "$players" = "unknown" ]; then
  echo "REFUSING: could not prove the live player count." >&2
  exit 1
fi
if [ "$players" -ne 0 ]; then
  echo "REFUSING: $players player(s) are connected." >&2
  exit 1
fi
printf '    players=0 world-state=%s counts=%s\n' "$state_hash" "$state_counts"

if [ "$dry" = "1" ]; then
  echo "==> --dry-run: stopping before backup, sync and restart"
  exit 0
fi

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup="/opt/wareborn/backups/pre-game-${stamp}"

echo "==> recoverable backup -> $HOST:$backup"
ssh "$HOST" "set -e
  install -d '$backup/game' '$backup/live-data'
  cp -a '$LIVE/.' '$backup/game/'
  cp -a '$DATA/.' '$backup/live-data/'"

echo "==> binary sync (data/ and native SDK explicitly protected)"
rsync -a \
  --exclude='/data' \
  --exclude='/libCoreSdkDll.so' \
  "$stage"/ "$HOST:$LIVE/"

# Prove synchronization did not alter or detach persistence before allowing a
# process restart. This is intentionally a hash comparison, not merely `test -f`.
ssh "$HOST" "set -e
  test \"\$(readlink -f '$LIVE/data')\" = '$DATA'
  test -f '$LIVE/libCoreSdkDll.so'
  test \"\$(sha256sum '$DATA/world-state.json' | awk '{print \$1}')\" = '$state_hash'"

build="$(git rev-parse --short HEAD)"
echo "==> restart $UNIT as build $build"
ssh "$HOST" "set -e
  sed -i 's/^Environment=WAREBORN_BUILD=.*/Environment=WAREBORN_BUILD=$build/' \
    /etc/systemd/system/wareborn-game.service.d/zz-release-build.conf
  systemctl daemon-reload
  systemctl restart '$UNIT'"
sleep 5

echo "==> verify restore and process health"
ssh "$HOST" "set -e
  systemctl is-active '$UNIT'
  since=\$(systemctl show '$UNIT' -p ActiveEnterTimestamp --value)
  log=\$(journalctl -u '$UNIT' --since \"\$since\" --no-pager -o cat)
  printf '%s\\n' \"\$log\" | grep -F 'world persistence: restored '
  if printf '%s\\n' \"\$log\" | grep -Eiq 'Unhandled|\\[error\\]|fatal|exception'; then
    echo 'REFUSING SUCCESS: startup journal contains an error.' >&2
    exit 1
  fi
  test \"\$(readlink -f '$LIVE/data')\" = '$DATA'
  test -f '$LIVE/libCoreSdkDll.so'"

echo "==> done"
