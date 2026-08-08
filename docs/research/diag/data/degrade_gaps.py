#!/usr/bin/env python3
"""How long does the DOWNLINK go silent, and does that grow over the session?

Two players reported sync degrading monotonically over minutes. What a player
actually perceives is not a throughput number, it is the length of the interval
in which no new position for the other avatar arrives. So measure exactly that.

The coresdk P/Invoke trace has no timestamps, but sends run at a near-constant
rate (the client publishes its own transform every physics step regardless of
what the downlink is doing), so the number of trace lines between two
consecutive receives is a usable clock. It is calibrated in main() against the
SendMetrics 2 s tick.

This distinguishes the two hypotheses that a rate curve cannot:
  - a growing queue          => gaps grow monotonically, throughput stays flat
  - an intermittent link     => gaps are bursty with no trend

Usage: degrade_gaps.py <session-dir> [first_tick] [last_tick]
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


def main():
    root = Path(sys.argv[1])
    kinds = classify(root / "coresdk.txt")
    ticks = [i for i, k in enumerate(kinds) if k == "M"]

    lo = int(sys.argv[2]) if len(sys.argv) > 2 else 0
    hi = min(int(sys.argv[3]) if len(sys.argv) > 3 else len(ticks) - 1, len(ticks) - 1)
    a, b = ticks[lo], ticks[hi]

    # Calibrate the line clock: how many trace lines fall in one 2 s tick.
    lines_per_tick = (b - a) / (hi - lo)
    sec_per_line = 2.0 / lines_per_tick
    print("Window ticks %d..%d (t+%ds..t+%ds)" % (lo, hi, 2 * lo, 2 * hi))
    print("Calibration: %.1f trace lines per 2 s tick => 1 line ~= %.1f ms"
          % (lines_per_tick, 1000 * sec_per_line))

    # Gap between consecutive receives, in trace lines and in ms.
    gaps = []           # (tick, lines_since_previous_receive)
    last = None
    for i in range(a, b):
        if kinds[i] != "R":
            continue
        if last is not None:
            tick = lo + int((i - a) / lines_per_tick)
            gaps.append((tick, i - last))
        last = i

    allg = [g for _, g in gaps]
    allg_sorted = sorted(allg)
    def pct(p):
        return allg_sorted[int(p * (len(allg_sorted) - 1))]
    print("\nreceive-to-receive gap over the whole window: n=%d" % len(allg))
    for p in (0.50, 0.90, 0.99, 0.999):
        print("   p%-5s %6d lines = %8.1f ms" % (int(p * 1000) / 10, pct(p), pct(p) * sec_per_line * 1000))
    print("   max    %6d lines = %8.1f ms" % (max(allg), max(allg) * sec_per_line * 1000))

    print("\nthe 20 longest downlink silences and WHEN they happened:")
    for tick, g in sorted(gaps, key=lambda x: -x[1])[:20]:
        print("   t+%-5ds  %6d lines = %8.1f ms" % (2 * tick, g, g * sec_per_line * 1000))

    # Per-tick summary: is the silence growing?
    print("\nper-tick downlink silence (ms):")
    print("  %-7s %-8s %-10s %-10s %-10s" % ("t+s", "recvs", "medianGap", "p95Gap", "maxGap"))
    per = {}
    for tick, g in gaps:
        per.setdefault(tick, []).append(g)
    maxes, p95s, meds = [], [], []
    for tick in range(lo, hi):
        v = per.get(tick, [])
        if not v:
            print("  %-7d %-8d %-10s %-10s %-10s" % (2 * tick, 0, "-", "-", "BLACKOUT"))
            continue
        vs = sorted(v)
        med = statistics.median(vs) * sec_per_line * 1000
        p95 = vs[int(0.95 * (len(vs) - 1))] * sec_per_line * 1000
        mx = max(vs) * sec_per_line * 1000
        meds.append(med); p95s.append(p95); maxes.append(mx)
        print("  %-7d %-8d %-10.1f %-10.1f %-10.1f" % (2 * tick, len(v), med, p95, mx))

    def slope(vals):
        n = len(vals)
        if n < 4:
            return 0.0
        mx = (n - 1) / 2
        my = sum(vals) / n
        num = sum((x - mx) * (y - my) for x, y in enumerate(vals))
        den = sum((x - mx) ** 2 for x in range(n))
        return num / den if den else 0.0

    print("\ntrends across ticks (blackout ticks excluded - they have no gaps to measure):")
    for nm, v in (("median gap", meds), ("p95 gap", p95s), ("max gap", maxes)):
        third = max(1, len(v) // 3)
        f = sum(v[:third]) / third
        l = sum(v[-third:]) / third
        print("  %-12s slope=%+8.3f ms/tick  first-third=%8.1f ms  last-third=%8.1f ms  change=%+.1f%%"
              % (nm, slope(v), f, l, (l - f) / f * 100 if f else 0.0))


if __name__ == "__main__":
    main()
