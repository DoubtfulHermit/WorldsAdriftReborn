#!/bin/sh
# fps-probe.sh - measure the client's fps, CPU and context-switch rate from
# OUTSIDE the game, with ~8 s resolution and no HUD, no overlay, no relaunch.
#
# WHY THIS EXISTS
# ---------------
# The mod's own `[WAR][perf] beat` line is authoritative but only lands every
# 30 s, which is useless for A/B testing a knob you can flip live. DXVK_HUD and
# any in-game counter need a relaunch, and the client has no autologin, so a
# relaunch costs you the in-world session entirely.
#
# The trick: DXVK's `WSI swapchain q` thread does exactly ONE voluntary context
# switch per presented frame. Its `voluntary_ctxt_switches` delta over a wall
# second IS the frame rate. Verified against the mod's independent counter:
# probe said 51.6, heartbeat said fps=51.5; probe said 119.0, heartbeat said
# fps=119.2.
#
# This is what made the worker-pin experiment possible at all - it let a
# hypothesis be flipped and measured on the SAME running session, same scene,
# same entity set, so the only variable was the thing under test.
#
# ctxsw/s is reported because it is the direct read-out of the fsync futex
# herd: >1M/s means the herd is active (see README). Healthy is <100k/s.
#
# USAGE: fps-probe.sh [seconds]        (default 8)

DUR="${1:-8}"

# Find the game by cmdline, NOT comm - comm reads "vx_initialize" because the
# Vivox SDK renames the main thread. `case` not `grep` (grep matches its own
# argv); /proc/<pid>/exe must be a wine binary.
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
[ -n "$PID" ] || { echo "UnityClient process not found. Start the game first." >&2; exit 1; }

PID="$PID" DUR="$DUR" python3 - <<'EOF'
import os, time, glob

P = os.environ["PID"]
DUR = float(os.environ["DUR"])
base = f"/proc/{P}/task"

def find(name):
    for t in os.listdir(base):
        try:
            if open(f"{base}/{t}/comm").read().strip() == name:
                return t
        except OSError:
            pass
    return None

wsi = find("WSI swapchain q")
if wsi is None:
    print("no 'WSI swapchain q' thread - is DXVK actually loaded? "
          "(check /proc/%s/maps for d3d11.dll + winevulkan)" % P)

def vcs(tid):
    try:
        for l in open(f"{base}/{tid}/status"):
            if l.startswith("voluntary_ctxt"):
                return int(l.split()[1])
    except OSError:
        pass
    return 0

def totals():
    cs = 0
    cpu = 0
    for t in os.listdir(base):
        try:
            for l in open(f"{base}/{t}/status"):
                if l.startswith("voluntary_ctxt"):
                    cs += int(l.split()[1]); break
            st = open(f"{base}/{t}/stat").read()
            r = st[st.rindex(') ') + 2:].split()
            cpu += int(r[11]) + int(r[12])
        except (OSError, ValueError):
            pass
    return cs, cpu

f0 = vcs(wsi) if wsi else 0
cs0, cpu0 = totals()
t0 = time.time()
time.sleep(DUR)
f1 = vcs(wsi) if wsi else 0
cs1, cpu1 = totals()
dt = time.time() - t0

fps = (f1 - f0) / dt if wsi else float("nan")
hz = int(os.sysconf("SC_CLK_TCK"))
print(f"pid={P}  fps={fps:6.1f}  procCPU={(cpu1 - cpu0) * 100.0 / hz / dt:6.0f}%  "
      f"ctxsw/s={(cs1 - cs0) / dt:>12,.0f}"
      f"{'   <-- FUTEX HERD ACTIVE' if (cs1 - cs0) / dt > 1e6 else ''}")
EOF
