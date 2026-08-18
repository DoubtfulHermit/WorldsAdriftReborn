#!/bin/sh
# Worlds Adrift Reborn - game client, WORKER-POOL EXPERIMENT variant.
#
# ############################################################################
# SUPERSEDED (2026-08-18), AND STILL UNMEASURED. Read tools/perf/README.md.
#
# The diagnosis below was right about the mechanism (27 spinning job workers)
# but wrong about the lever. The cost is not the worker COUNT, it is the
# workers waking CONCURRENTLY: Wine's fsync semaphore release wakes every
# waiter, so each job dispatch fired 27 cross-core wakeups - 1.83M context
# switches/s, and 72.5% of the Unity main thread's time inside FUTEX_WAKE.
#
# Confining the existing 27 workers to a SINGLE cpu (pin-unity-workers.sh,
# now wired into run-client.sh) took the live client from 51.4 -> 120.9 fps
# without touching the pool size. Measured worker concurrency curve:
# 1 cpu 120.9 | 2 cpus 92.2 | 3 cpus 12.4 | 4 cpus 14.0 | 28 cpus 51.4 fps.
#
# Since 3-4 concurrent cpus is far WORSE than free, 7 free-roaming workers
# (what this script produces) is EXPECTED to be worse than the one-cpu pin.
# That is a prediction: this script was never actually run, because testing it
# needs a relaunch and the client has no autologin (returning to the world
# needs manual clicks, which the no-synthetic-input rule forbids).
#
# If you do run it, keep WAREBORN_PIN_WORKERS=0 so the two mechanisms do not
# overlap, and compare against the numbers above.
# ############################################################################
# PREPARED, NOT DEPLOYED: copy over (or symlink next to)
# ~/Games/WAReborn-servers/run-client.sh only when running the controlled
# experiment. The only change vs the stock script is WINE_CPU_TOPOLOGY.
#
# WHY. Unity 5.6.4p1 sizes its job-system pool at (visible cores - 1) and its
# workers busy-wait between jobs. On the host's i7-14700F Wine reports all 28
# hardware threads, so the engine spawns ~27 "Worker Thread" spinners - the
# ~40x25% "Worker +" wall in lagprof-session1.out. Unity 5.6 has NO
# job-worker-count knob (boot.config/-job-worker-count exist only from Unity
# 2019.3), so the only seam is what the engine can SEE.
#
# WINE_CPU_TOPOLOGY=<count>:<cpulist> makes Proton's wine report <count> CPUs
# and pin to <cpulist>. taskset does NOT work for this - Wine reports the
# machine's CPU count regardless of the affinity mask (games that need fewer
# cores are the documented use case: ValveSoftware/Proton#5927, #7154).
# GE-Proton10-34's wine honors it even when the wine binary is exec'd
# directly, as this script does.
#
# 8:0,2,4,6,8,10,12,14 = one thread on each PHYSICAL P-core (P-cores are cpus
# 0-15 as SMT pairs, E-cores 16-27 - /sys/devices/cpu_core/cpus). Unity then
# spawns 7 job workers instead of 27, on the fast cores, and the E-cores stay
# free for the game server + wineserver that run on the same machine.
#
# MEASURE, DON'T TRUST: run tools/perf/lagprof-threads.sh before/after, and
# compare [WAR][perf] beat lines (fps=, thr=) and spike counts across the two
# launches. If stutter persists with 7 workers, the worker spin is heat/noise,
# not the stutter driver - see docs/research/diag/findings-stutter-probe.md.
export WINE_CPU_TOPOLOGY="8:0,2,4,6,8,10,12,14"

# --- below this line: byte-identical to run-client.sh ---
export WINEPREFIX="$HOME/Games/wa-proton/pfx"
export WINEDLLOVERRIDES="winhttp=n,b"
export WINEDEBUG=-all
export WINEFSYNC=1
export WINEESYNC=1
cd "$HOME/Games/WorldsAdrift" || exit 1
exec "$HOME/.steam/steam/compatibilitytools.d/GE-Proton10-34/files/bin/wine" 'UnityClient@Windows.exe'
