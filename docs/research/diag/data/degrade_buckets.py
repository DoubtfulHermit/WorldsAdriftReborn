#!/usr/bin/env python3
"""Bucket a session's coresdk P/Invoke trace and BepInEx client log over time.

The question this answers: does ANYTHING grow monotonically over a session?
Two players reported sync degrading steadily over minutes, which points at an
accumulator rather than a fixed per-frame cost.

Neither artefact carries a per-line timestamp, so we use the periodic probes we
already ship as rulers:

  client.log
    "[WAReborn] camera:"      RemoteRigSweeper, gated Time.unscaledTime + 2f
    "[WAReborn] local pos"    LocalPlayerTelemetry, gated unscaledTime + 0.5f
    "ORIGIN heartbeat"        OriginStrategyProbe, gated unscaledTime + 5f

  coresdk.txt
    "SendMetrics"             emitted by the stock SpatialOS worker on its own
                              cadence; used only as an ORDINAL tick, never
                              assumed to be a particular number of seconds.

Usage: degrade_buckets.py <session-dir>
"""

import re
import sys
from pathlib import Path


def coresdk_series(path):
    """Classify every line of the P/Invoke trace, in order."""
    sends, recvs, metrics, flags = [], [], [], []
    kinds = []
    with open(path, "r", errors="replace") as fh:
        for i, line in enumerate(fh):
            if "SendComponentUpdate" in line:
                kind = "send"
                sends.append(i)
            elif "received ComponentUpdateOp" in line:
                kind = "recv"
                recvs.append(i)
            elif "SendMetrics" in line:
                kind = "metrics"
                metrics.append(i)
            elif "GetFlag" in line:
                kind = "flag"
                flags.append(i)
            else:
                kind = "other"
            kinds.append(kind)
    return kinds, sends, recvs, metrics, flags


def bucket_by_ticks(kinds, ticks):
    """Between consecutive tick line-indices, count sends and receives."""
    rows = []
    for n, (a, b) in enumerate(zip(ticks, ticks[1:])):
        window = kinds[a:b]
        s = window.count("send")
        r = window.count("recv")
        rows.append((n, a, b, s, r))
    return rows


def equal_count_buckets(kinds, n_buckets):
    """Fallback ruler: split the send+recv stream into equal-EVENT buckets and
    report the composition of each. Time-axis free, so it cannot be fooled by a
    wrong assumption about the tick interval."""
    events = [k for k in kinds if k in ("send", "recv")]
    size = len(events) // n_buckets
    rows = []
    for b in range(n_buckets):
        window = events[b * size:(b + 1) * size]
        s = window.count("send")
        r = window.count("recv")
        rows.append((b, s, r))
    return rows


def client_ruler(path):
    """Walk the client log and, for each 2-second camera sweep, count the
    probes and errors that fell inside it."""
    cam = re.compile(r"\[WAReborn\] camera: 'Camera'")
    pos = re.compile(r"\[WAReborn\] local pos")
    beat = re.compile(r"ORIGIN heartbeat")
    err = re.compile(r"^\[(Error|Warning)")

    sweeps = []          # (line_index, counts since previous sweep)
    cur = {"pos": 0, "beat": 0, "err": 0, "lines": 0}
    with open(path, "r", errors="replace") as fh:
        for i, line in enumerate(fh):
            cur["lines"] += 1
            if pos.search(line):
                cur["pos"] += 1
            if beat.search(line):
                cur["beat"] += 1
            if err.match(line):
                cur["err"] += 1
            if cam.search(line):
                sweeps.append((i, dict(cur)))
                cur = {"pos": 0, "beat": 0, "err": 0, "lines": 0}
    return sweeps


def trend(values):
    """Least-squares slope and the first/last-third means, so a bend is visible
    without plotting."""
    n = len(values)
    if n < 4:
        return None
    xs = list(range(n))
    mx = sum(xs) / n
    my = sum(values) / n
    num = sum((x - mx) * (y - my) for x, y in zip(xs, values))
    den = sum((x - mx) ** 2 for x in xs)
    slope = num / den if den else 0.0
    third = max(1, n // 3)
    first = sum(values[:third]) / third
    last = sum(values[-third:]) / third
    return slope, first, last


def show(name, values):
    t = trend(values)
    print("  %-28s n=%-4d mean=%8.2f" % (name, len(values), sum(values) / len(values)), end="")
    if t:
        slope, first, last = t
        print("  slope=%+9.4f/bucket   first-third=%8.2f  last-third=%8.2f  change=%+.1f%%"
              % (slope, first, last, (last - first) / first * 100 if first else 0))
    else:
        print()


def main():
    root = Path(sys.argv[1])
    core = root / "coresdk.txt"
    client = root / "client.log"

    print("=" * 100)
    print("CORESDK P/Invoke trace:", core)
    kinds, sends, recvs, metrics, flags = coresdk_series(core)
    print("  total lines %d | sends %d | recvs %d | metrics %d | flags %d"
          % (len(kinds), len(sends), len(recvs), len(metrics), len(flags)))
    print("  send/recv ratio over the whole trace: %.3f" % (len(sends) / len(recvs)))

    print("\n-- bucketed by SendMetrics tick (ordinal, not assumed seconds) --")
    rows = bucket_by_ticks(kinds, metrics)
    print("  %-5s %-9s %-9s %-9s %-9s" % ("tick", "lines", "sends", "recvs", "s/r"))
    for n, a, b, s, r in rows:
        print("  %-5d %-9d %-9d %-9d %-9s" % (n, b - a, s, r, ("%.2f" % (s / r)) if r else "inf"))

    print("\n-- trends across SendMetrics ticks --")
    show("sends per tick", [r[3] for r in rows])
    show("recvs per tick", [r[4] for r in rows])
    show("lines per tick", [r[2] - r[1] for r in rows])
    ratio = [r[3] / r[4] for r in rows if r[4]]
    show("send/recv ratio per tick", ratio)

    print("\n-- equal-event buckets (no time assumption at all) --")
    print("  %-5s %-9s %-9s %-9s" % ("b", "sends", "recvs", "s/r"))
    for b, s, r in equal_count_buckets(kinds, 20):
        print("  %-5d %-9d %-9d %-9s" % (b, s, r, ("%.2f" % (s / r)) if r else "inf"))

    print("\n-- cumulative (sends - recvs) at each 5%% of the trace --")
    net = 0
    marks = []
    step = max(1, len(kinds) // 20)
    for i, k in enumerate(kinds):
        if k == "send":
            net += 1
        elif k == "recv":
            net -= 1
        if i % step == 0:
            marks.append((i, net))
    for i, n in marks:
        print("  line %-8d net=%+d" % (i, n))

    if not client.exists():
        return

    print("\n" + "=" * 100)
    print("CLIENT log (2-second camera-sweep ruler):", client)
    sweeps = client_ruler(client)
    print("  sweeps=%d  =>  in-play span ~= %d s of Time.unscaledTime" % (len(sweeps), 2 * len(sweeps)))
    # The sweeper logs one line per ENABLED camera, so consecutive duplicates of
    # the same sweep must not be double counted; 'Camera'/'PlayerCamera' is the
    # one camera that is always enabled, so it is exactly one line per sweep.
    print("\n  %-5s %-9s %-9s %-9s %-9s" % ("sweep", "logline", "localpos", "beats", "err/warn"))
    for n, (i, c) in enumerate(sweeps):
        print("  %-5d %-9d %-9d %-9d %-9d" % (n, c["lines"], c["pos"], c["beat"], c["err"]))

    print("\n-- trends across 2 s camera sweeps --")
    show("local pos per 2 s", [c["pos"] for _, c in sweeps])
    show("log lines per 2 s", [c["lines"] for _, c in sweeps])
    show("err/warn per 2 s", [c["err"] for _, c in sweeps])


if __name__ == "__main__":
    main()
