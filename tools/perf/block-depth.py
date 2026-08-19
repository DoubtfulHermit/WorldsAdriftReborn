#!/usr/bin/env python3
"""Is a long futex wait ONE block, or many short waits merged?

Polls syscall + schedstat together. For every window where the thread stays in
the same syscall on the same address, reports how many scheduler switches
happened inside it. nr_switches ~= 1 means a genuine single block (a real long
frame). Many switches means the thread woke and re-slept repeatedly and the
"long wait" is an artifact of poll-based merging.
"""
import os, sys, time

PID = int(sys.argv[1]); DUR = float(sys.argv[2]); MIN_MS = float(sys.argv[3])
T = f"/proc/{PID}/task/{PID}"
fs = os.open(f"{T}/syscall", os.O_RDONLY)
fd = os.open(f"{T}/schedstat", os.O_RDONLY)

def sysc():
    try: return os.pread(fs, 256, 0).decode().strip()
    except OSError: return ""
def sched():
    a = os.pread(fd, 128, 0).split()
    return int(a[0]), int(a[2])       # on-cpu ns, nr_switches

out = []
cur = None
end = time.monotonic() + DUR
while time.monotonic() < end:
    s = sysc(); now = time.monotonic_ns()
    key = None
    if s and not s.startswith("running") and not s.startswith("-1"):
        p = s.split()
        key = (p[0], p[1] if len(p) > 1 else "")
    if cur is None or cur[0] != key:
        if cur is not None and key is None or (cur is not None and cur[0] != key):
            dur = (now - cur[1]) / 1e6
            if cur[0] is not None and dur >= MIN_MS:
                on, sw = sched()
                out.append((cur[0], dur, sw - cur[3], (on - cur[2]) / 1e6))
        on, sw = sched()
        cur = (key, now, on, sw)

SY = {"449": "futex_waitv", "202": "futex", "1": "write", "0": "read", "7": "poll", "271": "ppoll"}
out.sort(key=lambda x: -x[1])
print(f"{'syscall':>14} {'dur_ms':>8} {'switches':>9} {'oncpu_ms':>9}")
for k, dur, sw, on in out[:30]:
    print(f"{SY.get(k[0], k[0]):>14} {dur:8.1f} {sw:9d} {on:9.2f}")

import statistics
big = [o for o in out if o[1] >= MIN_MS]
if big:
    sws = [o[2] for o in big]
    print(f"\nwindows>={MIN_MS}ms: n={len(big)}  median switches inside={statistics.median(sws)}  "
          f"mean={sum(sws)/len(sws):.1f}")
    single = [o for o in big if o[2] <= 2]
    print(f"genuine single blocks (<=2 switches): {len(single)}  "
          f"max={max((o[1] for o in single), default=0):.1f}ms")
