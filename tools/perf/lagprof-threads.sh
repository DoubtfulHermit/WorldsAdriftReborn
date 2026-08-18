#!/bin/sh
# lagprof-threads.sh - identify EXACTLY which threads burn the CPU in the
# WorldsAdrift client under Wine, by full thread name and per-thread CPU%.
#
# WHY: a 60 s `top -H` profile (lagprof-session1.out) showed ~40 threads all
# truncated to "Worker +" each burning ~25% CPU constantly - ~700% process CPU
# at idle gameplay. top's COMMAND column truncates; /proc/<pid>/task/<tid>/comm
# carries the first 15 chars of the REAL name, which is enough to tell apart:
#   "Worker Thread"    - Unity's job system pool (Unity 5.6 spawns cores-1 of
#                        these; on a 28-thread i7-14700F that is 27 spinners)
#   "Enlighten*"       - Enlighten GI worker pool
#   "AK *"             - Wwise audio (EventMgr, BankMgr, LEngine)
#   "GfxDeviceWorker"  / "UnityGfxDevice*" - render thread
#   "Threadpool work"  - Mono threadpool
#   "wine_threadpool"  / unnamed - Wine internals / native CoreSdkDll threads
#
# USAGE:   ./lagprof-threads.sh [sample-seconds]      (default 10)
# Run it while the game is in the state you want profiled (e.g. idle on the
# island after load-in). Output is self-contained; attach it to the session
# notes. Optionally run a second time during the loading screen to compare.
#
# The per-thread stack sample at the end (eu-stack, if installed) shows WHERE
# the hottest thread spins - a futex/NtWaitForAlertByThreadId frame means a
# sync-primitive spin (Wine-side cost), a raw loop in unityplayer means the
# engine's own busy-wait.

GAP="${1:-10}"

# Find the game.
#
# This used to match /proc/<pid>/comm against "UnityClient*" and it NEVER
# WORKED - it printed "UnityClient process not found" with the game running.
# The Vivox voice SDK renames the process main thread during init, so the
# client's comm reads "vx_initialize", not "UnityClient@Win". comm is only
# trustworthy for the WORKER threads (below), not for finding the process.
#
# So match the cmdline instead, with two guards:
#   - use the `case` BUILTIN, not grep: a `grep UnityClient@Windows.exe` child
#     has the pattern in its own argv and matches itself;
#   - require /proc/<pid>/exe to be a wine binary, so a shell or editor that
#     merely mentions the exe name in its argv cannot match.
PID=""
for c in /proc/[0-9]*/cmdline; do
    [ -r "$c" ] || continue
    p="${c#/proc/}"; p="${p%/cmdline}"
    cl=$(tr '\0' ' ' < "$c" 2>/dev/null) || continue
    case "$cl" in
        *UnityClient@Windows.exe*) ;;
        *) continue;;
    esac
    case "$(readlink "/proc/$p/exe" 2>/dev/null)" in
        *wine*) PID="$p"; break;;
    esac
done
if [ -z "$PID" ]; then
    echo "UnityClient process not found. Start the game first." >&2
    exit 1
fi

CLK_TCK=$(getconf CLK_TCK)
echo "=== lagprof-threads: pid $PID, sampling ${GAP}s, clk_tck $CLK_TCK ==="
echo "--- process: $(tr '\0' ' ' < /proc/$PID/cmdline 2>/dev/null) ---"
NPROC_VISIBLE=$(grep -c ^processor /proc/cpuinfo)
echo "--- host cpus: $NPROC_VISIBLE; affinity: $(taskset -pc "$PID" 2>/dev/null | sed 's/.*: //') ---"

snapshot() {
    # tid<TAB>comm<TAB>utime+stime  (stat parsed from after the last ')' so
    # comm spaces/parens cannot shift the fields)
    for t in /proc/$PID/task/[0-9]*; do
        tid="${t#/proc/$PID/task/}"
        comm=$(cat "$t/comm" 2>/dev/null) || continue
        stat=$(cat "$t/stat" 2>/dev/null) || continue
        rest="${stat##*) }"
        # rest starts at field 3 (state); utime=field14, stime=15 -> 12th/13th here
        set -- $rest
        printf '%s\t%s\t%s\n' "$tid" "$comm" "$(( ${12} + ${13} ))"
    done
}

S1=$(snapshot)
sleep "$GAP"
S2=$(snapshot)

echo ""
echo "--- per-thread CPU% over ${GAP}s (top 60, full comm names) ---"
printf '%s\n' "$S2" | while IFS="$(printf '\t')" read -r tid comm t2; do
    t1=$(printf '%s\n' "$S1" | awk -F'\t' -v tid="$tid" '$1==tid{print $3}')
    [ -z "$t1" ] && t1=0
    dt=$((t2 - t1))
    pct=$(awk -v d="$dt" -v g="$GAP" -v c="$CLK_TCK" 'BEGIN{printf "%.1f", 100*d/(g*c)}')
    printf '%s\t%s\t%s\n' "$pct" "$tid" "$comm"
done | sort -rn | head -60 | awk -F'\t' 'BEGIN{printf "%6s  %8s  %s\n","CPU%","TID","THREAD NAME"} {printf "%6s  %8s  %s\n",$1,$2,$3}'

echo ""
echo "--- grouped by thread name ---"
printf '%s\n' "$S2" | while IFS="$(printf '\t')" read -r tid comm t2; do
    t1=$(printf '%s\n' "$S1" | awk -F'\t' -v tid="$tid" '$1==tid{print $3}')
    [ -z "$t1" ] && t1=0
    printf '%s\t%s\n' "$comm" "$((t2 - t1))"
done | awk -F'\t' -v g="$GAP" -v c="$CLK_TCK" '
    {n[$1]++; t[$1]+=$2}
    END{
        printf "%6s  %5s  %9s  %s\n","COUNT","SUMCPU","AVG/THREAD","THREAD NAME"
        for (k in n) printf "%6d  %5.0f%%  %8.1f%%  %s\n", n[k], 100*t[k]/(g*c), 100*t[k]/(g*c*n[k]), k
    }' | sort -k2 -rn

echo ""
echo "--- total threads: $(ls /proc/$PID/task | wc -l) ---"

# Where does the hottest thread spin? Native stack, if elfutils is installed.
if command -v eu-stack >/dev/null 2>&1; then
    HOT=$(printf '%s\n' "$S2" | while IFS="$(printf '\t')" read -r tid comm t2; do
        t1=$(printf '%s\n' "$S1" | awk -F'\t' -v tid="$tid" '$1==tid{print $3}')
        [ -z "$t1" ] && t1=0
        printf '%s\t%s\n' "$((t2 - t1))" "$tid"
    done | sort -rn | head -3 | cut -f2)
    for h in $HOT; do
        echo ""
        echo "--- eu-stack of hot tid $h ($(cat /proc/$PID/task/$h/comm 2>/dev/null)) ---"
        eu-stack -1 -p "$h" 2>&1 | head -25
    done
else
    echo "(eu-stack not installed - 'pacman -S elfutils' to also get spin-site stacks)"
fi

echo "=== done ==="
