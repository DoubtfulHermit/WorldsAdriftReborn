#!/usr/bin/env python3
"""Run-length structure of the coresdk P/Invoke trace.

WHY THIS AND NOT A RATE CURVE.

A queue that grows without bound does NOT show up as a falling throughput.
Throughput stays flat at the drain rate while the *delay* grows linearly. So the
per-second rate curves cannot confirm or refute the unbounded-queue hypothesis
of findings-relay-latency.md 4.4 - they are consistent with it either way.

What a growing backlog DOES change is the SHAPE of the trace. Hop 13 is verified:
GetOpList returns at most one packet per call. So the client drains a backlog of
k queued packets as k consecutive "received ComponentUpdateOp" lines with nothing
between them. If the backlog grows over the session, the run length of
consecutive receives grows with it. That is measurable from the ordering alone,
with no timestamps and no assumption about the tick interval.

The same logic applies to sends: 4.4 predicts that a pump which finds a non-empty
dispatch queue transmits nothing, so sends should cluster into the gaps between
receive bursts.

Time axis: WorkerProtocol_Connection_SendMetrics is emitted by the stock
SpatialOS worker on a fixed 2 s wall-clock cadence. That is not assumed, it is
cross-checked in main() against two independent Time.unscaledTime rulers in the
BepInEx log (the 2 s camera sweep and the 5 s origin heartbeat).

Usage: degrade_runs.py <session-dir> [first_tick] [last_tick]
"""

import statistics
import sys
from pathlib import Path


def classify(path):
    out = []
    with open(path, "r", errors="replace") as fh:
        for line in fh:
            if "SendComponentUpdate" in line:
                out.append("S")
            elif "received ComponentUpdateOp" in line:
                out.append("R")
            elif "SendMetrics" in line:
                out.append("M")
            else:
                out.append(".")
    return out


def ticks_of(kinds):
    return [i for i, k in enumerate(kinds) if k == "M"]


def runs(seq, symbol):
    """Lengths of every maximal run of `symbol`, ignoring nothing - a run is
    broken by any other symbol, which is the honest reading: an interleaved send
    means the pump went back to the caller."""
    out, cur = [], 0
    for k in seq:
        if k == symbol:
            cur += 1
        else:
            if cur:
                out.append(cur)
            cur = 0
    if cur:
        out.append(cur)
    return out


def describe(name, vals):
    if not vals:
        print("  %-26s (none)" % name)
        return
    vals_sorted = sorted(vals)
    p95 = vals_sorted[int(0.95 * (len(vals_sorted) - 1))]
    print("  %-26s n=%-5d mean=%6.2f  median=%5.1f  p95=%5d  max=%5d"
          % (name, len(vals), statistics.mean(vals), statistics.median(vals), p95, max(vals)))


def slope(vals):
    n = len(vals)
    if n < 4:
        return 0.0
    mx = (n - 1) / 2
    my = sum(vals) / n
    num = sum((x - mx) * (y - my) for x, y in enumerate(vals))
    den = sum((x - mx) ** 2 for x in range(n))
    return num / den if den else 0.0


def main():
    root = Path(sys.argv[1])
    lo = int(sys.argv[2]) if len(sys.argv) > 2 else 0
    hi = int(sys.argv[3]) if len(sys.argv) > 3 else 10 ** 9

    kinds = classify(root / "coresdk.txt")
    ticks = ticks_of(kinds)
    print("SendMetrics ticks: %d  (at 2 s each => %d s of connected play)"
          % (len(ticks), 2 * len(ticks)))

    hi = min(hi, len(ticks) - 1)
    a, b = ticks[lo], ticks[hi]
    window = kinds[a:b]
    print("Analysing ticks %d..%d  = t+%ds..t+%ds  (%d trace lines)"
          % (lo, hi, 2 * lo, 2 * hi, len(window)))
    print("  sends=%d recvs=%d" % (window.count("S"), window.count("R")))

    print("\n== run-length structure over the whole window ==")
    describe("consecutive receives", runs(window, "R"))
    describe("consecutive sends", runs(window, "S"))

    print("\n== per-tick run-length, to see whether the backlog GROWS ==")
    print("  %-6s %-7s %-7s %-9s %-9s %-9s %-9s"
          % ("tick", "sends", "recvs", "maxRrun", "meanRrun", "maxSrun", "meanSrun"))
    max_r, mean_r, max_s, mean_s = [], [], [], []
    for n in range(lo, hi):
        w = kinds[ticks[n]:ticks[n + 1]]
        rr = runs(w, "R")
        ss = runs(w, "S")
        mr = max(rr) if rr else 0
        ar = statistics.mean(rr) if rr else 0.0
        ms = max(ss) if ss else 0
        as_ = statistics.mean(ss) if ss else 0.0
        max_r.append(mr)
        mean_r.append(ar)
        max_s.append(ms)
        mean_s.append(as_)
        print("  %-6d %-7d %-7d %-9d %-9.2f %-9d %-9.2f"
              % (n, w.count("S"), w.count("R"), mr, ar, ms, as_))

    print("\n== trends (positive slope = growing backlog) ==")
    for nm, v in (("max receive run", max_r), ("mean receive run", mean_r),
                  ("max send run", max_s), ("mean send run", mean_s)):
        third = max(1, len(v) // 3)
        f = sum(v[:third]) / third
        l = sum(v[-third:]) / third
        print("  %-18s slope=%+8.4f/tick  first-third=%7.2f  last-third=%7.2f  change=%+.1f%%"
              % (nm, slope(v), f, l, (l - f) / f * 100 if f else 0.0))


if __name__ == "__main__":
    main()
