#!/usr/bin/env bash
#
# Read the LIVE game server's log and decide whether it is healthy.
#
# WHY THIS EXISTS. On 2026-08-19 the game server had been logging
# "[error] failed to initialize component NNNN of entity NN" continuously for
# days and nobody noticed, because the only thing anyone ever ran after a deploy
# was:
#
#     journalctl -u wareborn-game --since '2 min ago' | tail -100
#
# That window is clean by construction. The server spawns nothing on its own; the
# errors are produced by a CLIENT checking entities out, so a two-minute window
# after a restart with nobody logged in sees zero and prints green. The real rate
# only appears once somebody plays - and it appeared, and the check said fine.
#
# So this script does three things that window could not:
#
#   1. IT HAS A DENOMINATOR. Errors are counted per component-interest BATCH, not
#      per minute. A window with no batches in it is INCONCLUSIVE, never PASS.
#      "Nobody played, so nothing was wrong" is the exact lie that hid this.
#   2. IT COMPARES AGAINST A COMMITTED BASELINE. Every component id we already
#      know fails, and why, is written down in
#      tools/game-server-error-baseline.txt. An id that is NOT in that file is a
#      NEW gap and fails the check immediately, at count 1. A regression is a new
#      id or a rising rate - never "some errors", which is unactionable.
#   3. IT LOOKS AT AN HOUR, NOT TWO MINUTES, and says so.
#
# Read-only. It ssh's to production and greps; it starts, stops and changes
# nothing. Safe to run while people are playing, and meant to be.
#
#   tools/check-game-server.sh              # the last 60 minutes
#   tools/check-game-server.sh 6h           # any journalctl --since expression
#   tools/check-game-server.sh --since-boot # since the unit last started
#
# Exit status: 0 healthy, 1 a check failed, 2 inconclusive (nobody played).
set -uo pipefail

HOST="${WAREBORN_HOST:-root@62.171.161.19}"
UNIT="wareborn-game"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
baseline="$repo_root/tools/game-server-error-baseline.txt"

window="${1:-60min}"
case "$window" in
  --since-boot)
    since="$(ssh "$HOST" "systemctl show $UNIT -p ActiveEnterTimestamp --value")"
    label="since the unit started ($since)"
    ;;
  *)
    since="$window ago"
    label="the last $window"
    ;;
esac

[ -f "$baseline" ] || { echo "no baseline at $baseline"; exit 1; }

echo "==> $UNIT on $HOST, $label"

# ------------------------------------------------------------------ collect
#
# One ssh, one pass over the journal. The journal is millions of lines, so
# everything is counted server-side and only the tallies come back.
#
# The counted line is SendOPHelper's, which is emitted once per failing
# component per batch and carries both the id and the outcome. The denominator
# is the "[interest] entity N wants" line, emitted once per batch attempted -
# so the ratio is "how many of the components a client asked for did we fail to
# produce", which is the number that means something.
read -r batches errors_total <<<"$(
  ssh "$HOST" "journalctl -u $UNIT --since '$since' --no-pager -o cat 2>/dev/null \
    | awk '/\[interest\] entity .* wants /{b++} /\[error\] failed to initialize component /{e++} END{print b+0, e+0}'"
)"

tallies="$(
  ssh "$HOST" "journalctl -u $UNIT --since '$since' --no-pager -o cat 2>/dev/null \
    | grep -a '\[error\] failed to initialize component ' \
    | sed -E 's/.*component ([0-9]+) of entity (-?[0-9]+) .*outcome ([A-Za-z]+).*/\1 \3/' \
    | sort | uniq -c | sort -rn"
)"

# ----------------------------------------------------------------- verdict

if [ "${batches:-0}" -eq 0 ]; then
  echo "    INCONCLUSIVE: not one component-interest batch in this window."
  echo "    Nobody checked anything out, so nothing was exercised. This is NOT a pass."
  echo "    Re-run over a window in which somebody actually played:"
  echo "        tools/check-game-server.sh 6h"
  exit 2
fi

printf '    %-10s %s\n' "batches" "$batches"
printf '    %-10s %s (%s per 100 batches)\n' "errors" "$errors_total" \
  "$(awk -v e="${errors_total:-0}" -v b="$batches" 'BEGIN{printf "%.1f", 100*e/b}')"
echo

fail=0
known_ids="$(grep -vE '^\s*(#|$)' "$baseline" | awk '{print $1}')"

while read -r count id outcome; do
  [ -n "${id:-}" ] || continue
  if echo "$known_ids" | grep -qx "$id"; then
    note="$(grep -E "^$id[[:space:]]" "$baseline" | cut -d' ' -f2-)"
    printf '    %-8s %-6s %-16s %s\n' "$count" "$id" "$outcome" "${note:-known}"
  else
    printf '    %-8s %-6s %-16s ** NOT IN THE BASELINE - THIS IS NEW **\n' "$count" "$id" "$outcome"
    fail=1
  fi
done <<<"$tallies"

# A NEW ID IS A FAILURE AT COUNT 1. That is the whole point: every new entity
# type this server has ever grown announced itself as an id nobody had a branch
# for, and it is cheap to notice on the first one and expensive to notice on the
# ten-thousandth.
echo
if [ "$fail" = "1" ]; then
  echo "FAIL: a component id failed that nobody has written down."
  echo "Decide which it is, then record it:"
  echo "  * a genuine missing seed  -> write the branch in ComponentsSerializer"
  echo "  * this entity truly lacks it -> declare it in ComponentAbsencePolicy"
  echo "  * either way, add a line to tools/game-server-error-baseline.txt"
  exit 1
fi

# The rate ceiling is deliberately generous and deliberately present: it catches
# a known id that has started firing an order of magnitude more often, which is
# what a regression in an existing branch looks like from outside.
rate="$(awk -v e="${errors_total:-0}" -v b="$batches" 'BEGIN{printf "%d", 100*e/b}')"
ceiling="${WAREBORN_ERROR_CEILING:-25}"
if [ "$rate" -gt "$ceiling" ]; then
  echo "FAIL: $rate errors per 100 interest batches (ceiling $ceiling)."
  echo "Every id is known, so this is a RATE regression, not a new gap."
  exit 1
fi

echo "OK: every failing id is accounted for, at $rate per 100 batches (ceiling $ceiling)."
