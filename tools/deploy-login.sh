#!/usr/bin/env bash
#
# Deploy the login/web server to production.
#
# WHY THIS EXISTS: the public /patchnotes page is GENERATED from the commit log
# by tools/patchnotes/build-changelog.sh, and for the first day of its life that
# was a manual step that whoever deployed had to remember. They did not. Twelve
# commits shipped while the page still claimed 510, and the maintainer noticed
# before we did - which is the worst way to find out that a page whose entire
# promise is "this is what shipped" had quietly stopped being true.
#
# So regeneration is a STEP OF THE DEPLOY, not a thing to remember. If the notes
# are stale this script refuses to continue until they are committed, because a
# deploy that silently rewrites a tracked file is its own kind of surprise.
#
#   tools/deploy-login.sh            # regenerate, gate, build, publish, verify
#   tools/deploy-login.sh --dry-run  # everything except the rsync and restart
#
# The GAME server is deliberately not deployed here. It is a separate unit with
# separate risk (it holds live player progression and a restart drops everyone),
# and the one rule that binds them: A SCHEMA MIGRATION MEANS BOTH BINARIES SHIP
# TOGETHER. A split deploy once left the game server refusing persistence and
# destroyed a character's progression. If your change migrates the schema, this
# script is not enough on its own.
set -euo pipefail

HOST="root@62.171.161.19"
LIVE="/opt/wareborn/WorldsAdriftServer-linux"
UNIT="wareborn-login"
BASE="http://62.171.161.19:8085"
PUBLIC="https://wareborn.ratlabs.cc"

dry=0
[ "${1:-}" = "--dry-run" ] && dry=1

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# ---------------------------------------------------------------- patch notes

echo "==> checking public homepage status"
bash tools/public-site/check-status-freshness.sh

echo "==> regenerating the patch notes from the commit log"
bash tools/patchnotes/build-changelog.sh

notes="WorldsAdriftServer/Web/Assets/patch-notes.md"
if ! git diff --quiet -- "$notes"; then
  echo
  echo "REFUSING: $notes is out of date - it has just been regenerated and differs."
  echo "The published page would have claimed a different history from the one you"
  echo "are shipping. Commit the regenerated file, then run this again:"
  echo
  echo "    git add $notes && git commit -m 'Regenerate the patch notes'"
  echo
  exit 1
fi
echo "    up to date"

# --------------------------------------------------------------------- gates

echo "==> tests"
dotnet test WorldsAdriftServer.Tests -c Release --nologo 2>&1 | tail -1

echo "==> publish"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
dotnet publish WorldsAdriftServer/WorldsAdriftServer.csproj \
  -c Release -r linux-x64 --self-contained true -o "$stage" 2>&1 | tail -1

if [ "$dry" = "1" ]; then
  echo "==> --dry-run: stopping before rsync and restart"
  exit 0
fi

# -------------------------------------------------------------------- deploy

# No --delete: live state and configuration next to the binary are not all
# produced by publish, and removing them is how you lose a config nobody
# remembered was there.
echo "==> rsync -> $HOST:$LIVE"
rsync -a "$stage"/ "$HOST:$LIVE/"

echo "==> restart $UNIT"
ssh "$HOST" "systemctl restart $UNIT"
sleep 5
ssh "$HOST" "systemctl is-active $UNIT"

# -------------------------------------------------------------------- verify

echo "==> verifying from outside"
fail=0
check() {
  local url="$1" want="$2" name="$3"
  local got
  got="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 15 "$url" || echo 000)"
  if [ "$got" = "$want" ]; then
    printf '    %-22s %s\n' "$name" "$got"
  else
    printf '    %-22s %s  EXPECTED %s\n' "$name" "$got" "$want"
    fail=1
  fi
}

check "$PUBLIC/patchnotes"          200 "/patchnotes"
check "$PUBLIC/"                    200 "/"
check "$PUBLIC/map"                 200 "/map"
check "$PUBLIC/login"               200 "/login"
check "$PUBLIC/patch/manifest.json" 200 "/patch/manifest.json"
check "$PUBLIC/account"             302 "/account (-> login)"
check "$BASE/welcomeMessage"        200 "/welcomeMessage"
# Plain http on purpose: the game client's TLS tops out at 1.0, so this is the
# scheme it actually fetches a crest over. See EmblemOrigin.
check "$BASE/alliance-emblem/objects.json" 200 "emblem catalogue"

# The root returning 200 proves routing, not that the reviewed homepage asset is
# the one production embedded. Compare the explicit review marker as well.
local_status="$(grep -oE 'data-game-status-through="[0-9a-f]+"' \
  WorldsAdriftServer/Web/Assets/home-body.html | grep -oE '[0-9a-f]+' || echo '?')"
live_status="$(curl -sS --max-time 15 "$PUBLIC/" 2>/dev/null \
  | grep -oE 'data-game-status-through="[0-9a-f]+"' | grep -oE '[0-9a-f]+' || echo '?')"
if [ "$live_status" = "$local_status" ]; then
  printf '    %-22s %s\n' "homepage agrees" "$live_status"
else
  printf '    %-22s live=%s repo=%s  MISMATCH\n' "homepage" "$live_status" "$local_status"
  fail=1
fi

# The page must agree with the history it was built from.
live_count="$(curl -sS --max-time 15 "$PUBLIC/patchnotes/source" 2>/dev/null \
  | grep -oE '^Every commit, newest first\. [0-9]+' | grep -oE '[0-9]+$' || echo '?')"
repo_count="$(grep -c '^\* ' "$notes" || echo 0)"
if [ "$live_count" = "$repo_count" ]; then
  printf '    %-22s %s commits\n' "patch notes agree" "$live_count"
else
  printf '    %-22s live=%s repo=%s  MISMATCH\n' "patch notes" "$live_count" "$repo_count"
  fail=1
fi

[ "$fail" = "0" ] || { echo; echo "SOME CHECKS FAILED - look before walking away."; exit 1; }
echo "==> done"
