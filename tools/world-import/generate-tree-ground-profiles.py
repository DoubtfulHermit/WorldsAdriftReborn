#!/usr/bin/env python3
"""Generate release-tree-ground-profiles.txt - a baked per-seat ground profile so
a felled log can be laid ALONG the terrain without a runtime terrain query.

WHY THIS EXISTS. When a tree falls the server has to decide where the trunk ends
up. A trunk laid flat at the seat's own height sinks into a rising slope and
floats over a falling one, and either one reads as broken. The honest fix is to
ask the ground how it runs away from the seat - but the game server has no
collision mesh, no heightfield and no physics for the island it is dropping the
log onto. Every terrain answer it can give has to be baked beforehand.

So this bakes one. For each authored tree seat in release-tree-placements.json,
and for each of the 8 compass bearings the fall heading can land on, it records a
single signed number: how far the ground has RISEN, in decimetres, by the time
you are REACH metres out along that bearing. The server reads the two numbers
either side of the fall heading and tilts the trunk. That is the whole contract -
8 bytes per seat, no query, no mesh, no allocation.

WHAT THE NUMBER MEANS, and why it is a max and not an average. Along one bearing
the sampled ground is a scatter of points, not a line. A log resting on that
scatter touches the HIGHEST of them and bridges the rest; it does not sink to
their mean. So for every qualifying sample the script computes the rise the log
line would need at REACH in order to pass through that sample, and keeps the
LARGEST. That is the smallest tilt for which the trunk clears every sampled
ground point along the bearing - the "rest on the high side" rule. Averaging, or
taking only the far-end sample, buries the trunk in the first hummock.

THE SOURCE, and its one sharp edge. docs/research/world-data/island-surfaces/ is
an 8 m VOXEL DECIMATION of the extracted LOD0 collision surface, not a height
field: an XZ column can carry several samples at different heights (overhangs,
arches, the underside of the island). Nothing here may assume one Y per column.
The DECK_BAND filter is what keeps the profile on the deck the tree stands on
instead of picking up the rock shelf 60 m below it; the CORRIDOR filter is what
keeps a bearing reading its own strip of ground rather than the whole island.

HOW MIN_T AND DECK_BAND WERE CHOSEN, and one thing they cannot fix. Run with
--sweep to re-derive them: it prints the value distribution over a grid of both
constants and writes nothing. The short version is that DECK_BAND drives the
railed +-127 values and MIN_T drives the noise amplification, and the pair below
takes the rail rate from 6.4% to 0.0% and p90 from 74 dm to 40 dm.

What the sweep will NOT do is get p90 under about 25 dm, and it is worth knowing
why before someone tries. That was tested directly: replacing the max with the
75th-percentile ratio over the same qualifying samples moves p90 only 41 -> 36 dm.
The steep values are therefore the GROUND, not a lone outcrop caught by the max
rule - a large minority of authored tree seats really do sit on 20-25 degree
terrain, because that is what a Worlds Adrift island looks like. Any constant that
forces p90 below 25 buys it by turning real slope into UNKNOWN. If the runtime
wants a gentler trunk than the ground justifies, it should clamp the tilt it
APPLIES; this file's job is to report the hillside as measured.

Islands with no extracted surface, or with no authored seats, are SKIPPED rather
than guessed at, and the count of what was skipped is printed and written into
the file's own header.

Coordinates are island-local metres throughout - the same space the seats and the
surface samples are already in - so nothing here depends on where the island was
placed in the world.

Usage:  python3 tools/world-import/generate-tree-ground-profiles.py
        python3 tools/world-import/generate-tree-ground-profiles.py \\
            --min-t 6 --deck-band 12 --dry-run     # try constants without writing
"""

import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SURFACES = ROOT / "docs/research/world-data/island-surfaces"
SEATS = ROOT / "WorldsAdriftRebornGameServer.Multiplayer/Islands/release-tree-placements.json"
OUTPUT = ROOT / "WorldsAdriftRebornGameServer.Multiplayer/Islands/release-tree-ground-profiles.txt"

SCHEMA = 1

# --- The contract, mirrored on the C# side ---------------------------------
# How far out the profile is measured. A felled trunk is on this order of length,
# so the number answers "where does the far end of the log sit" directly.
REACH = 16.0
# The 8 compass bearings a fall heading is quantised to. Bearing 0 is +Z and each
# step is +45 degrees towards +X: this is the server's fall-heading convention
# and the two have to agree or every log tilts the wrong way.
BEARINGS = 8
# Half-width of the strip of ground a bearing is allowed to see. Wide enough that
# an 8 m voxel decimation still puts samples in it, narrow enough that the
# bearing reads its own ground and not the neighbouring one.
CORRIDOR = 4.0
# Vertical gate. The surface table is multi-valued in Y per XZ column, so without
# this a seat on a plateau would profile against the cliff face below it.
#
# This, not MIN_T, is what actually drives the railed +-127 values: a --sweep over
# min-t {4,6,8,10,12} x deck {4,6,8,12,20} moves the rail rate from 5.9% (deck 20)
# to 2.3% (deck 12) to 0.6% (deck 8) to 0.0% (deck 6 and below) almost independently
# of min-t. Those extremes were cliff tops and shelves 12-20 m off the deck being
# read as if the trunk could rest on them. Held under one voxel, a neighbouring
# deck cannot be mistaken for this one. The cost is UNKNOWN bearings, which is why
# this stops at 6.0 rather than going tighter: 4.0 buys nothing on the rail rate
# and pushes UNKNOWN from 10.9% to 15.0%.
# Overridable with --deck-band; the C# side mirrors the baked value.
DEFAULT_DECK_BAND = 6.0
# Nearest sample a bearing is allowed to read, measured along the bearing.
#
# This is the single most load-bearing constant here and 1.0 was WRONG. The
# source is an 8 m VOXEL DECIMATION: a sample 1 m from the seat is not a closer
# reading of the ground, it is the same 8 m voxel with a shorter lever. And the
# lever is what hurts - the stored value is (dy * REACH / t), so at t = 1 m every
# centimetre of decimation noise is multiplied by SIXTEEN. Baked at 1.0 the world
# came out with 6.4% of values railed at +-127 and a p90 of 74 dm, i.e. one felled
# log in ten standing up at 25-38 degrees like a ramp out of the hillside - worse
# than the flat log it replaces. Holding the first reading out to one FULL voxel
# means every sample the profile reads is a genuinely different cell of the source
# data, and caps the lever at 2x. Overridable with --min-t; mirrored in C#.
DEFAULT_MIN_T = 8.0
# Emitted when a bearing has no qualifying sample at all - an island edge, a gap
# in the decimation. Out of band for a signed byte payload, so the server can
# tell "flat" (0) from "no idea" and fall back rather than trust a zero.
UNKNOWN = -128
# The stored value is a signed byte of decimetres.
RISE_MIN = -127
RISE_MAX = 127


def bearing_vectors():
    """Unit XZ directions for the 8 bearings, in bearing order.

    Kept as a function so the convention is stated exactly once: heading in
    degrees -> (sin, cos), i.e. bearing 0 is +Z and +45 degrees turns towards +X.
    """
    headings = [b * (360 // BEARINGS) for b in range(BEARINGS)]
    dx = np.array([math.sin(math.radians(h)) for h in headings])
    dz = np.array([math.cos(math.radians(h)) for h in headings])
    return dx, dz


DX, DZ = bearing_vectors()


def profile_island(seats, samples, min_t, deck_band):
    """Rise ratios for every seat x bearing of one island.

    `seats` is (M,3) and `samples` is (N,3), both island-local metres. Returns
    (values, known): (M,BEARINGS) float ratios in metres-of-rise-at-REACH, and a
    (M,BEARINGS) bool saying whether any sample qualified.

    Vectorised per island because the pure-Python form is ~400M distance tests
    over the whole world. Seats are chunked so a 22k-sample island does not
    build a 90 MB intermediate.
    """
    count = len(seats)
    values = np.zeros((count, BEARINGS))
    known = np.zeros((count, BEARINGS), dtype=bool)
    if len(samples) == 0:
        return values, known

    # Cap the (chunk, N, BEARINGS) intermediates at a few million elements.
    chunk = max(1, 2_000_000 // (len(samples) * BEARINGS))
    for start in range(0, count, chunk):
        block = seats[start:start + chunk]
        # (m, N) offsets from each seat to every sample.
        rx = samples[None, :, 0] - block[:, None, 0]
        ry = samples[None, :, 1] - block[:, None, 1]
        rz = samples[None, :, 2] - block[:, None, 2]
        # (m, N, BEARINGS): distance along the bearing, and offset across it.
        along = rx[:, :, None] * DX[None, None, :] + rz[:, :, None] * DZ[None, None, :]
        across = rx[:, :, None] * DZ[None, None, :] - rz[:, :, None] * DX[None, None, :]
        mask = (
            (along >= min_t)
            & (along <= REACH)
            & (np.abs(across) <= CORRIDOR)
            & (np.abs(ry[:, :, None]) <= deck_band)
        )
        # Divide only where the mask already guarantees along >= min_t; the
        # discarded lanes would otherwise raise on t == 0.
        safe = np.where(mask, along, 1.0)
        ratio = np.where(mask, ry[:, :, None] * REACH / safe, -np.inf)
        # The high side wins: the smallest tilt that clears every sample.
        values[start:start + len(block)] = np.max(ratio, axis=1)
        known[start:start + len(block)] = np.any(mask, axis=1)
    return values, known


def encode(value):
    """Metres of rise at REACH -> the stored signed decimetre byte."""
    return max(RISE_MIN, min(RISE_MAX, int(round(value * 10))))


def load():
    """Read the seats and every island surface ONCE, in placements order.

    Returns a list of (asset, name, seats, samples, skip_reason) where `samples`
    is None exactly when skip_reason is set. Kept separate from the bake so a
    --sweep can try several constants without re-parsing ~1M surface samples per
    combination.
    """
    payload = json.loads(SEATS.read_text(encoding="utf-8"))
    dataset = []
    for island in payload["islands"]:
        asset = island["asset"]
        name = island.get("name", "")
        points = island["points"]
        surface_path = SURFACES / (asset + ".json")
        if not points:
            dataset.append((asset, name, None, None, "no authored seats"))
            continue
        if not surface_path.exists():
            dataset.append((asset, name, np.asarray(points, dtype=float), None,
                            "no extracted surface"))
            continue
        surface = json.loads(surface_path.read_text(encoding="utf-8"))
        dataset.append((asset, name,
                        np.asarray(points, dtype=float),
                        np.asarray([p[:3] for p in surface["points"]], dtype=float),
                        None))
    return dataset


def percentile(sorted_values, q):
    """Nearest-rank percentile. No interpolation, so the answer is an actual
    stored value and the table cannot report a decimetre the file does not hold."""
    if not sorted_values:
        return 0
    rank = max(1, math.ceil(q / 100.0 * len(sorted_values)))
    return sorted_values[min(rank, len(sorted_values)) - 1]


def bake(dataset, min_t, deck_band, progress=False):
    """Profile the whole world at these constants.

    Returns (seat_lines, stats). `stats` carries everything both the file header
    and the sweep table need, so the two can never disagree about what was baked.
    """
    lines = []
    covered_islands = 0
    covered_seats = 0
    skipped = []
    skipped_seats = 0
    stats = {"unknown": 0, "stored": [], "clamped": 0}

    for index, (asset, name, seats, samples, reason) in enumerate(dataset):
        if reason is not None:
            skipped.append((asset, name, reason))
            skipped_seats += 0 if seats is None else len(seats)
            continue

        values, known = profile_island(seats, samples, min_t, deck_band)

        lines.append("@ %s %d" % (asset, len(seats)))
        for row in range(len(seats)):
            cells = []
            for bearing in range(BEARINGS):
                if not known[row][bearing]:
                    stats["unknown"] += 1
                    cells.append(UNKNOWN)
                    continue
                raw = float(values[row][bearing])
                if raw * 10 > RISE_MAX or raw * 10 < RISE_MIN:
                    stats["clamped"] += 1
                stored = encode(raw)
                stats["stored"].append(stored)
                cells.append(stored)
            lines.append(" ".join(str(c) for c in cells))

        covered_islands += 1
        covered_seats += len(seats)
        if progress and ((index + 1) % 25 == 0 or index + 1 == len(dataset)):
            print("  %3d/%d islands, %5d seats profiled"
                  % (index + 1, len(dataset), covered_seats))

    stats["stored"].sort()
    stats["islands"] = covered_islands
    stats["seats"] = covered_seats
    stats["skipped"] = skipped
    stats["skippedSeats"] = skipped_seats
    stats["total"] = stats["unknown"] + len(stats["stored"])
    return lines, stats


def build_header(stats, min_t, deck_band):
    return [
        "# Release-world tree GROUND PROFILES: how far the ground rises, in decimetres, at",
        "# %.1f m from each authored tree seat along each of %d compass bearings." % (REACH, BEARINGS),
        "#",
        "# WHAT IT IS FOR: laying a felled log along the terrain with NO runtime terrain",
        "# query. The game server has no collision mesh for the island it drops the trunk",
        "# onto, so the slope it needs is baked here, ahead of time, one signed byte per",
        "# seat per bearing. Read the bearings either side of the fall heading and tilt.",
        "# The value is the SMALLEST rise for which a straight log line from the seat",
        "# clears every sampled ground point along that bearing - it rests on the high",
        "# side, it does not average.",
        "#",
        "# GENERATED by tools/world-import/generate-tree-ground-profiles.py from",
        "# docs/research/world-data/island-surfaces/ and release-tree-placements.json.",
        "# Regenerate it; do not hand-edit it.",
        "#",
        "# Covered %d islands, %d seats. Skipped %d islands (%d seats) with no authored"
        % (stats["islands"], stats["seats"], len(stats["skipped"]), stats["skippedSeats"]),
        "# seats or no extracted surface.",
        "# schema %d reach %.1f bearings %d corridor %.1f deck %.1f min-t %.1f unknown %d"
        % (SCHEMA, REACH, BEARINGS, CORRIDOR, deck_band, min_t, UNKNOWN),
        "# Bearing 0 is +Z (island-local); each step is +45 degrees towards +X.",
        "# '@' starts an island: '@ <workshopId> <seatCount>'. Then one line per seat, in",
        "# release-tree-placements.json order: 8 signed decimetre integers, space separated.",
    ]


def report(stats):
    stored = stats["stored"]
    print("  %d islands, %d seats, %d values"
          % (stats["islands"], stats["seats"], stats["total"]))
    print("  unknown %d (%.2f%%)"
          % (stats["unknown"], 100.0 * stats["unknown"] / stats["total"]))
    if stored:
        within5 = sum(1 for v in stored if abs(v) <= 5)
        within20 = sum(1 for v in stored if abs(v) <= 20)
        railed = sum(1 for v in stored if abs(v) == RISE_MAX)
        print("  stored dm: min %d  p10 %d  p25 %d  median %d  p75 %d  p90 %d  p99 %d  max %d"
              % (stored[0], percentile(stored, 10), percentile(stored, 25),
                 percentile(stored, 50), percentile(stored, 75),
                 percentile(stored, 90), percentile(stored, 99), stored[-1]))
        print("  |v| <= 5 dm %.1f%%   |v| <= 20 dm %.1f%%   railed at +-127 %d (%.2f%%)"
              % (100.0 * within5 / len(stored), 100.0 * within20 / len(stored),
                 railed, 100.0 * railed / len(stored)))
        print("  outside +-127 dm before clamping: %d (%.2f%% of known)"
              % (stats["clamped"], 100.0 * stats["clamped"] / len(stored)))
    for asset, name, why in stats["skipped"]:
        print("  skipped %-12s %-28s %s" % (asset, name, why))


def sweep(dataset, min_ts, deck_bands):
    """Print the distribution table for a grid of constants. Writes nothing.

    This is how MIN_T and DECK_BAND were chosen; keeping it in the generator
    means the next person can re-derive the defaults instead of trusting them.
    """
    header = ("min-t deck  %UNK  %rail    p10   p25   med   p75   p90   p99"
              "   %<=5dm  %<=20dm")
    print(header)
    print("-" * len(header))
    for min_t in min_ts:
        for deck_band in deck_bands:
            _, stats = bake(dataset, min_t, deck_band)
            stored = stats["stored"]
            railed = sum(1 for v in stored if abs(v) == RISE_MAX)
            print("%5.1f %4.1f %5.2f %6.2f %6d %5d %5d %5d %5d %5d %8.1f %8.1f"
                  % (min_t, deck_band,
                     100.0 * stats["unknown"] / stats["total"],
                     100.0 * railed / len(stored),
                     percentile(stored, 10), percentile(stored, 25),
                     percentile(stored, 50), percentile(stored, 75),
                     percentile(stored, 90), percentile(stored, 99),
                     100.0 * sum(1 for v in stored if abs(v) <= 5) / len(stored),
                     100.0 * sum(1 for v in stored if abs(v) <= 20) / len(stored)))


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--min-t", type=float, default=DEFAULT_MIN_T,
                        help="nearest sample a bearing may read, metres along the "
                             "bearing (default %(default)s)")
    parser.add_argument("--deck-band", type=float, default=DEFAULT_DECK_BAND,
                        help="vertical gate around the seat, metres "
                             "(default %(default)s)")
    parser.add_argument("--dry-run", action="store_true",
                        help="report the distribution without writing the file")
    parser.add_argument("--sweep", action="store_true",
                        help="print the min-t x deck-band distribution table and exit")
    args = parser.parse_args()

    dataset = load()

    if args.sweep:
        sweep(dataset, [4.0, 6.0, 8.0, 10.0], [4.0, 6.0, 8.0, 12.0, 20.0])
        return 0

    lines, stats = bake(dataset, args.min_t, args.deck_band, progress=True)
    if args.dry_run:
        print("dry run, nothing written (min-t %.1f, deck %.1f)"
              % (args.min_t, args.deck_band))
        report(stats)
        return 0

    header = build_header(stats, args.min_t, args.deck_band)
    OUTPUT.write_text("\n".join(header + lines) + "\n", encoding="ascii", newline="\n")
    print("wrote %s (min-t %.1f, deck %.1f)"
          % (OUTPUT.relative_to(ROOT), args.min_t, args.deck_band))
    report(stats)
    return 0


if __name__ == "__main__":
    sys.exit(main())
