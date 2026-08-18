#!/bin/sh
# pin-unity-workers.sh - collapse Unity's job-system futex thundering herd.
#
# WHAT IT FIXES
# -------------
# Unity 5.6.4p1 sizes its job-worker pool at (visible CPUs - 1). On this
# i7-14700F that is 27 "Worker Thread"s. Between jobs each worker blocks on the
# job-queue semaphore, which under Wine's fsync backend is a futex_waitv
# (syscall 449). Wine's fsync semaphore release wakes EVERY waiter, so each job
# dispatch is a 27-way wakeup with a cross-core IPI per worker.
#
# Measured in-world on the live client (ents=98, 3440x1440, DXVK):
#
#   workers free on all 28 CPUs   51.4 fps   807% CPU   1,826,265 ctxsw/s
#   workers confined to ONE cpu  120.9 fps   137% CPU      58,367 ctxsw/s
#
# 1.83 MILLION voluntary context switches per second - ~35,000 per rendered
# frame. It is pure sync overhead, not work: with the herd collapsed the 27
# workers together drop from 672% CPU to 26.6%, and the MAIN thread's system
# time drops from 72.5% to 3.4% (it was spending three quarters of every frame
# inside FUTEX_WAKE, which is why the main thread - and therefore the frame
# rate - was pegged).
#
# WHY ONE CPU AND NOT "A FEW"
# ---------------------------
# The curve is violently non-monotonic, because the herd only forms when
# workers can spin/wake CONCURRENTLY. Measured, same scene, same session:
#
#   1 cpu  (cpu17)     120.9 fps      2 cpus (16,17)    92.2 fps
#   3 cpus (16-18)      12.4 fps      4 cpus (24-27)    14.0 fps
#   28 cpus (free)      51.4 fps
#
# Three or four CPUs is WORSE than leaving them free: enough parallelism for 27
# oversubscribed workers to preempt each other while holding job-queue state
# (a convoy), not enough to drain it. One CPU is the only safe point - the
# kernel serialises the workers, so a semaphore release has at most one
# runnable waiter and the IPI storm cannot happen.
#
# Pinning the main/render/dxvk threads on top of this is worth +0.7 fps
# (120.6 -> 121.3), i.e. noise. Worker confinement is the whole effect.
#
# USAGE
#   pin-unity-workers.sh <pid> [cpu]      # runs until <pid> exits
# Env:
#   WAREBORN_WORKER_CPU   override the CPU to confine workers to
#   WAREBORN_PIN_INTERVAL poll seconds (default 2)
#
# Workers are created during engine init, after the process exists, and the
# pool can be topped up later - so this re-applies on a poll rather than
# pinning once. Cost is 27 sched_setaffinity calls every 2 s.
#
# NOTE ON FINDING THE GAME: do NOT match /proc/<pid>/comm against "UnityClient".
# The Vivox voice SDK renames the process main thread, so comm reads
# "vx_initialize". Pass the pid in from the launcher instead.

PID="$1"
[ -n "$PID" ] || { echo "usage: $0 <pid> [cpu]" >&2; exit 2; }

CPU="$2"
[ -n "$CPU" ] || CPU="$WAREBORN_WORKER_CPU"
if [ -z "$CPU" ]; then
    # Prefer the LAST E-core: keeps every P-core free for the main, render and
    # dxvk threads, which are the ones that actually set the frame rate.
    atom=$(cat /sys/devices/cpu_atom/cpus 2>/dev/null)
    if [ -n "$atom" ]; then
        CPU=${atom##*-}
        CPU=${CPU##*,}
    else
        online=$(cat /sys/devices/system/cpu/online 2>/dev/null)
        CPU=${online##*-}
        CPU=${CPU##*,}
    fi
fi
[ -n "$CPU" ] || CPU=0

INTERVAL="${WAREBORN_PIN_INTERVAL:-2}"

echo "[pin-unity-workers] pid=$PID confining Unity 'Worker Thread's to cpu $CPU"

announced=0
while [ -d "/proc/$PID" ]; do
    n=0
    for t in /proc/"$PID"/task/*; do
        [ -r "$t/comm" ] || continue
        # Unity's job workers are named exactly "Worker Thread". Do not touch
        # "BackgroundWorker", the dxvk-* threads or "UnityGfxDeviceWorker".
        if [ "$(cat "$t/comm" 2>/dev/null)" = "Worker Thread" ]; then
            taskset -pc "$CPU" "${t##*/}" >/dev/null 2>&1 && n=$((n + 1))
        fi
    done
    if [ "$n" -gt 0 ] && [ "$announced" -eq 0 ]; then
        echo "[pin-unity-workers] pinned $n worker threads to cpu $CPU"
        announced=1
    fi
    sleep "$INTERVAL"
done

echo "[pin-unity-workers] pid $PID gone, exiting"
