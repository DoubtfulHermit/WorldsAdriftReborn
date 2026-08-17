#!/usr/bin/env python3
"""Generate release-tree-placements.json - harvestable tree seats for every
release island the Cardinal Guild survey says had trees.

WHY THIS EXISTS AT ALL. Not one of the 465,571 props baked into the 255 island
bundles is a tree (docs/research/loop/findings-harvestable-world.md). Every
harvestable tree in Worlds Adrift was a SpatialOS entity created by the GSim from
a server-side list, and that list did not ship. There is also no tree spawner in
the client decompile: IslandResourceType has a Value2Tree member but
IslandProxyVisualizer.OnSpawnResources handles only Metal and Egg. So retail tree
positions are unrecoverable, and this script authors new ones from the same
evidence the deposit/databank generator uses.

WHAT IS EVIDENCE AND WHAT IS CALIBRATION - read this before changing anything.

  EVIDENCE (do not invent, do not widen):
    * WHICH islands have trees, and WHICH species. From the Cardinal Guild
      survey, carried on every island of release-runtime-catalog.json as
      `trees`. 72 islands carry a real species list; 2 say literally
      "No trees"; 180 are empty. Only the 72 are populated here.
    * WHERE a tree may stand. From that island's extracted TRS-correct
      collision surface, docs/research/world-data/island-surfaces/<asset>.json,
      filtered by the same upward-normal and spacing rules that produced
      Haven's 80 working trees (Resources/HavenSurface.cs).
    * WHICH prefab a species maps to. From the 65 shipped tree prefabs whose
      authored TreePreprocessor.woodType was parsed in
      docs/research/loop/data/tree_woodtypes.json (65/65, no guesses). This
      script emits the WOOD only; the C# side picks the client-verified prefab
      for that wood, because the "is this prefab safe to spawn" judgement lives
      with WorldEntities.VerifiedSpecies.

  CALIBRATION (has no retail source, and says so):
    * HOW MANY trees an island gets. Retail budgeted databanks, loot containers
      and loot chests by surface area (LootablePerAreaDataState, component 1244)
      and NOTHING ELSE - there is no tree budget field anywhere in the schema.
      The count is therefore not recoverable at any fidelity. It is calibrated
      to Haven, whose 80 trees over 90 LOD0 cells are proven to work in a live
      session, and clamped. See TREES_PER_CELL / MIN_TREES / MAX_TREES.

The count formula is duplicated in C# (Islands/ReleaseTreeBudget.cs) and a unit
test asserts every island's emitted point count equals what the C# formula says,
so the two cannot drift apart silently. That is the same trick the existing
generator uses for its FNV hash.

Usage:  python3 tools/world-import/generate-release-tree-placements.py
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
OUTPUT = os.path.join(
    ROOT, "WorldsAdriftRebornGameServer.Multiplayer", "Islands",
    "release-tree-placements.json")

# --- Calibration -----------------------------------------------------------
# Haven carries 80 distributed trees over a 90-cell LOD0 surface and that
# density is proven in a live session. Nothing in retail's schema budgets trees,
# so this is the honest anchor rather than a recovered number.
TREES_PER_CELL = 80.0 / 90.0
# Floor: a 9-cell islet still has to be worth landing on for wood.
MIN_TREES = 12
# Ceiling: bounds boot-time entity registration. The largest tree island
# (734 cells) would otherwise ask for 652 trees on its own, and a past bug had a
# joining second player OOM instantiating too much world in one burst
# (docs/HANDOVER.md). 49 of the 72 islands clamp here, so the ceiling - not the
# density - is what actually sets the world's tree budget.
MAX_TREES = 60

# --- Evidence-derived placement rules --------------------------------------
# Both taken verbatim from Resources/HavenSurface.cs, which produced the 80
# Haven trees that work today. A tree wants flatter ground than a deposit.
TREE_MIN_UPWARD_NORMAL = 0.94
TREE_MIN_SPACING = 15.0
# Deposits and databanks already occupy seats on these islands; a tree grown
# inside a metal deposit is a tree you cannot shoot. They are passed as
# occupied anchors exactly as the existing generator passes deposits when it
# places databanks, and the tree spacing rule above is what keeps them clear.
#
# The eight woods, lower-cased. The survey's species vocabulary is exactly this
# set (verified: sorted(set(...)) over all 72 islands).
KNOWN_WOODS = {"ash", "birch", "cedar", "chestnut", "elm", "hemlock", "oak", "palm"}


def budget(cells):
    """Trees for an island with this many LOD0 cells. Mirrored in C#."""
    scaled = int(math.floor(cells * TREES_PER_CELL + 0.5))
    return max(MIN_TREES, min(MAX_TREES, scaled))


def fnv_key(point):
    """FNV-1a 64 over millimetre-quantised x/y/z.

    Byte-for-byte the same as SurfacePlacementGenerator.HashKey in C#. It gives
    a stable, seed-free shuffle so the same surface always yields the same
    seats, on any machine, in any order.
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

    Same shape and intent as generate-release-runtime-catalog.py: small or steep
    islands cannot satisfy the ideal rule, and silently under-filling them would
    leave an island the survey says is wooded with three trees on it. Each rung
    relaxes one constraint at a time and the ladder is fixed, so the outcome
    stays reproducible.
    """
    ladder = [
        (spacing, min_normal),
        (spacing * 0.7, min_normal),
        (spacing * 0.4, 0.80),
        (5.0, 0.55),
        (0.0, 0.40),
    ]
    best = []
    for rung_spacing, rung_normal in ladder:
        picked = spaced_once(points, target, rung_spacing, occupied, rung_normal)
        if len(picked) > len(best):
            best = picked
        if len(picked) >= target:
            return picked
    return best


def main():
    with open(CATALOG, encoding="utf-8") as handle:
        catalog = json.load(handle)

    islands = []
    total = 0
    short = []
    for island in catalog["islands"]:
        species = [s for s in island["trees"] if s != "No trees"]
        if not species:
            continue

        woods = []
        for name in species:
            wood = name.strip().lower()
            if wood not in KNOWN_WOODS:
                sys.exit("island %s: unknown surveyed species %r - refusing to "
                         "guess a wood" % (island["asset"], name))
            if wood not in woods:
                woods.append(wood)

        surface_path = os.path.join(SURFACES, island["asset"] + ".json")
        with open(surface_path, encoding="utf-8") as handle:
            surface = json.load(handle)
        if surface["meta"].get("transform") != "TRS-composed":
            sys.exit("island %s: surface predates the TRS fix, refusing to use it"
                     % island["asset"])

        cells = surface["meta"]["cells"]
        target = budget(cells)

        # Existing occupants, island-local, so trees never grow through them.
        anchors = [(d["x"], d["y"], d["z"]) for d in island["deposits"]]
        anchors += [(d["x"], d["y"], d["z"]) for d in island["databankPoints"]]

        picked = spaced(surface["points"], target, TREE_MIN_SPACING, anchors,
                        TREE_MIN_UPWARD_NORMAL)
        if len(picked) < target:
            short.append((island["name"], len(picked), target))

        islands.append({
            "asset": island["asset"],
            "name": island["name"],
            "cells": cells,
            "woods": woods,
            "points": [[round(p[0], 3), round(p[1], 3), round(p[2], 3)] for p in picked],
        })
        total += len(picked)

    payload = {"schema": 1, "islands": islands}
    with open(OUTPUT, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, separators=(",", ":"))
        handle.write("\n")

    print("%d islands, %d trees" % (len(islands), total))
    if short:
        print("under target (surface too small or too steep):")
        for name, got, want in short:
            print("  %-32s %3d / %3d" % (name, got, want))


if __name__ == "__main__":
    main()
