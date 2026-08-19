#!/usr/bin/env python3
"""Generate release-loot-placements.json - loot-container seats for every release
island.

WHAT IS EVIDENCE AND WHAT IS CALIBRATION - read this before changing anything.

  EVIDENCE (recovered from the shipped client; do not widen, do not invent):

    * THAT loot volume scaled with FLAT SURFACE AREA, and the exact curve.
      acs/LootablePerAreaDataVisualizer.cs:50-62 is a clamped exponential lerp,
      transcribed into Multiplayer/Loot/LootBudget.cs and mirrored below in
      `budget()`. Retail budgeted databanks, loot containers and loot chests this
      way and nothing else. It is AREA, not tier: the tier decides what is inside
      a container, never how many there are.

    * THE SPACING RULE, 20 m. acs/IslandDataBankAndLootableSpawnerVisualizer.cs:64
      rejects any candidate within `sqrMagnitude < 400f` of an accepted one. This
      is the one placement constant in the whole loot pipeline that is not a
      guess.

    * THE GROUNDING RULE. The same file, :100-101, places the prop at
      `surfacePoint - normal * Random(0.15, 0.30)` and aligns its up-axis to the
      surface normal. This script emits RAW surface vertices; the sink is applied
      once on the C# side at LootContainers.Sink, so Haven's seats and these go
      through identical arithmetic. Do not pre-sink here.

    * WHERE a container may stand. Each island's own extracted, TRS-correct
      collision surface, docs/research/world-data/island-surfaces/<asset>.json,
      filtered by the same upward-normal rule Haven uses
      (Resources/HavenSurface.cs, LootMinUpwardNormal).

  CALIBRATION (no retail source, and it says so):

    * The nineteen tuning fields of 1244 LootablePerAreaDataState did not ship, so
      min/max/areaForMin/areaForMax/expLerp are WAREBORN TUNING. They live in
      LootBudget.cs and are mirrored here; LootContainerPlacementTests asserts
      every emitted point count equals what the C# formula says, so the two cannot
      drift apart silently - the same trick the tree and runtime-catalogue
      generators already use.

    * The arrival-pad annulus. A player graduating from the Wilderness shrine
      materialises on the island's `landing` point; uniform hash-ordered scatter
      has no idea the pad exists. One container is drawn from within 60 m of it so
      that arriving somewhere new reliably shows you a chest, exactly as the tree
      generator seats a starter stand. Same measured table, same rules; only the
      candidate set differs.

Usage:  python3 tools/world-import/generate-release-loot-placements.py
"""

import json
import math
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SURFACES = os.path.join(ROOT, "docs", "research", "world-data", "island-surfaces")
CATALOG = os.path.join(
    ROOT, "WorldsAdriftRebornGameServer.Multiplayer", "Islands",
    "release-runtime-catalog.json")
TREES = os.path.join(
    ROOT, "WorldsAdriftRebornGameServer.Multiplayer", "Islands",
    "release-tree-placements.json")
OUTPUT = os.path.join(
    ROOT, "WorldsAdriftRebornGameServer.Multiplayer", "Islands",
    "release-loot-placements.json")

# --- Recovered: the budget curve's SHAPE (LootablePerAreaDataVisualizer.DoMath).
# --- WAREBORN TUNING: its constants. Mirrored from Multiplayer/Loot/LootBudget.cs.
SQUARE_METRES_PER_SAMPLE = 64.0
MIN_CONTAINERS = 2
AREA_FOR_MIN = 3200.0
MAX_CONTAINERS = 12
AREA_FOR_MAX = 300000.0
EXP_LERP = 0.55
EXTRA_MULTIPLIER = 1.0

# --- Recovered placement rules ---------------------------------------------
LOOT_MIN_UPWARD_NORMAL = 0.97          # HavenSurface.LootMinUpwardNormal
LOOT_MIN_SPACING = 20.0                # IslandDataBankAndLootableSpawnerVisualizer.cs:64

# --- WAREBORN TUNING: the arrival pad ---------------------------------------
LANDING_CLEAR = 6.0
LANDING_NEAR_RADIUS = 60.0
LANDING_NEAR_CONTAINERS = 1

# --- WAREBORN TUNING: keep-out from things already standing -----------------
# A chest wedged inside a metal deposit is a chest you cannot open. Deposits,
# databanks and trees are all passed as occupied anchors; the 20 m spacing rule
# then does the work.
OCCUPIED_CLEARANCE = LOOT_MIN_SPACING


def do_math(area, low, area_for_low, high, area_for_high, exp_lerp):
    """acs/LootablePerAreaDataVisualizer.cs:50-62, transcribed."""
    if area < area_for_low:
        return low
    if area > area_for_high:
        return high
    if area_for_high <= area_for_low:
        return high
    f = (area - area_for_low) / (area_for_high - area_for_low)
    return low + (f ** exp_lerp) * (high - low)


def budget(samples):
    """Containers for an island with this many 8 m surface samples. Mirrored in C#."""
    if samples <= 0:
        return 0
    area = samples * SQUARE_METRES_PER_SAMPLE
    raw = do_math(area, MIN_CONTAINERS, AREA_FOR_MIN,
                  MAX_CONTAINERS, AREA_FOR_MAX, EXP_LERP)
    return int(raw * EXTRA_MULTIPLIER)


def fnv_key(point):
    """FNV-1a 64 over millimetre-quantised x/y/z.

    Byte-for-byte the same as SurfacePlacementGenerator.HashKey in C#, so the
    same surface always yields the same seats on any machine.
    """
    h = 14695981039346656037
    for value in point[:3]:
        q = int(math.floor(abs(value) * 1000.0 + 0.5))
        if value < 0:
            q = -q
        q &= 0xFFFFFFFFFFFFFFFF
        for _ in range(8):
            h ^= q & 0xFF
            h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
            q >>= 8
    return h


def spaced_once(points, target, spacing, occupied, min_normal):
    """Greedy minimum-spacing pass in deterministic hash order."""
    used = list(occupied)
    picked = []
    limit = spacing * spacing
    candidates = [p for p in points if p[4] >= min_normal]
    candidates.sort(key=lambda p: (fnv_key(p), p[0], p[1], p[2]))
    for p in candidates:
        if len(picked) >= target:
            break
        if all((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 + (p[2] - q[2]) ** 2 >= limit
               for q in used):
            picked.append(p)
            used.append(p)
    return picked


def spaced(points, target, spacing, occupied, min_normal):
    """Relaxation ladder, first rung that reaches `target` wins.

    Same shape as the tree generator's, and for the same reason: a small or steep
    island cannot satisfy the ideal rule, and silently under-filling it would
    leave a landable island with nothing to search. The ladder relaxes the
    SPACING first and the NORMAL only at the end - a container on a slope is
    worse than two containers close together, because the sink is applied
    straight down and only holds while the ground is near level.
    """
    ladder = [
        (spacing, min_normal),
        (spacing * 0.7, min_normal),
        (spacing * 0.5, 0.94),
        (spacing * 0.5, 0.90),
    ]
    best = []
    for rung_spacing, rung_normal in ladder:
        picked = spaced_once(points, target, rung_spacing, occupied, rung_normal)
        if len(picked) > len(best):
            best = picked
        if len(picked) >= target:
            return picked
    return best


def pad_distance(point, pad):
    return math.sqrt((point[0] - pad["x"]) ** 2 + (point[1] - pad["y"]) ** 2
                     + (point[2] - pad["z"]) ** 2)


def seatable(points, pad):
    return [point for point in points if pad_distance(point, pad) >= LANDING_CLEAR]


def within(points, pad, radius):
    return [point for point in points if pad_distance(point, pad) <= radius]


def main():
    with open(CATALOG, encoding="utf-8") as handle:
        catalog = json.load(handle)
    with open(TREES, encoding="utf-8") as handle:
        tree_seats = {i["asset"]: i["points"] for i in json.load(handle)["islands"]}

    islands = []
    total = 0
    short = []
    near_misses = []

    for island in catalog["islands"]:
        surface_path = os.path.join(SURFACES, island["asset"] + ".json")
        with open(surface_path, encoding="utf-8") as handle:
            surface = json.load(handle)
        if surface["meta"].get("transform") != "TRS-composed":
            sys.exit("island %s: surface predates the TRS fix, refusing to use it"
                     % island["asset"])

        # The 8 m-lattice sample count IS the measurable analogue of retail's
        # "mostly flat surface area": one accepted, upward-facing sample per 64 m
        # square. meta["cells"] is the coarse LOD0 MESH cell count and is a
        # different quantity entirely - using it here would under-budget every
        # island by a factor of about 25.
        samples = surface["meta"]["candidates"]
        target = budget(samples)

        # Everything already standing on this island, island-local.
        anchors = [(d["x"], d["y"], d["z"]) for d in island["deposits"]]
        anchors += [(d["x"], d["y"], d["z"]) for d in island["databankPoints"]]
        anchors += [tuple(p) for p in tree_seats.get(island["asset"], [])]

        pad = island["landing"]
        seats = seatable(surface["points"], pad)
        near_target = min(LANDING_NEAR_CONTAINERS, target)
        near = spaced(within(seats, pad, LANDING_NEAR_RADIUS),
                      near_target, LOOT_MIN_SPACING, anchors, LOOT_MIN_UPWARD_NORMAL)
        picked = near + spaced(seats, target - len(near), LOOT_MIN_SPACING,
                               anchors + [p[:3] for p in near], LOOT_MIN_UPWARD_NORMAL)

        if len(picked) < target:
            short.append((island["name"], len(picked), target))
        if not near and target > 0:
            near_misses.append(island["name"])

        islands.append({
            "asset": island["asset"],
            "name": island["name"],
            "samples": samples,
            "cells": surface["meta"]["cells"],
            "tier": island["cellTier"],
            "points": [[round(p[0], 3), round(p[1], 3), round(p[2], 3)] for p in picked],
        })
        total += len(picked)

    payload = {"schema": 1, "islands": islands}
    with open(OUTPUT, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, separators=(",", ":"))
        handle.write("\n")

    print("%d islands, %d loot containers" % (len(islands), total))
    if near_misses:
        print("no seat within %.0f m of the arrival pad (%d islands): %s"
              % (LANDING_NEAR_RADIUS, len(near_misses), ", ".join(near_misses[:20])))
    if short:
        print("under target (surface too small or too steep): %d islands" % len(short))
        for name, got, want in short[:20]:
            print("  %-32s %3d / %3d" % (name, got, want))


if __name__ == "__main__":
    main()
