#!/usr/bin/env python3
"""Generate release-tree-placements.json - harvestable tree seats for every
release island that is not explicitly recorded as treeless.

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
    * WHICH species an island grows, WHERE the survey recorded one. From the
      Cardinal Guild survey, carried on every island of
      release-runtime-catalog.json as `trees`. 72 islands carry a real species
      list; 2 say literally "No trees" and are honoured as treeless; 180 are
      EMPTY, which means unsurveyed. See wood_inference.py for why, and for what
      those 180 get instead - a tier-cohort inference stamped
      `woodSource: "inferred-tier"`, exactly as metal_inference.py already does
      for the 193 islands whose ore table was never read.

      This is the fix for the bug that made a graduating player teleport onto a
      tier-1 island with nothing to chop: `if not species: continue` treated a
      coverage gap as geography, and 32 of the 46 tier-1 islands - Mount Spero
      among them - fell through it.
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
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wood_inference import WoodInference, woods_for  # noqa: E402

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

# --- The arrival pad is a first-class anchor (WAREBORN TUNING) --------------
# A player who graduates from the Wilderness shrine is teleported onto the
# island's `landing` point and has to find wood from there. Hash-ordered
# scattering alone is uniform over the WHOLE surface, so on a 600-cell island the
# nearest of 60 trees was measured at 50.6 m and could as easily have been 300 m.
# The first seats an island fills are therefore drawn from an ANNULUS around its
# arrival pad, from the same measured surface table and by the same rules - this
# changes WHICH measured samples win, never what a sample is.
#
# LANDING_CLEAR keeps a trunk out of the spot the player materialises in - the
# global scatter has no idea the pad exists and was picking the pad's own sample
# on three tier-1 islands, putting a tree at exactly 0.0 m. The surface table is
# one representative sample per 8 m voxel, so 6 m removes that sample and nothing
# beyond its immediate neighbours. LANDING_NEAR_RADIUS is what this project is
# willing to call "on arrival"; both mirror the runtime-catalogue generator.
LANDING_CLEAR = 6.0
LANDING_NEAR_RADIUS = 60.0
# Four is one visible stand of trees rather than a token single trunk, and it is
# small enough that the remaining budget still covers the rest of the island.
LANDING_NEAR_TREES = 4


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
    # The floor is 5 m and never 0. ReleaseTreeCatalogTests asserts that no two
    # shipped seats are closer than 5 m, and a rung that can violate the
    # generator's own asserted invariant is a trap waiting for the first island
    # steep enough to reach it. The last rung relaxes the NORMAL, not the spacing.
    ladder = [
        (spacing, min_normal),
        (spacing * 0.7, min_normal),
        (spacing * 0.4, 0.80),
        (5.0, 0.55),
        (5.0, 0.40),
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
    """The island's measured samples minus the arrival spot itself."""
    return [point for point in points if pad_distance(point, pad) >= LANDING_CLEAR]


def within(points, pad, radius):
    """The measured samples within `radius` of the arrival pad.

    A FILTER over the island's own surface table - it selects, never synthesises.
    """
    return [point for point in points if pad_distance(point, pad) <= radius]


def main():
    with open(CATALOG, encoding="utf-8") as handle:
        catalog = json.load(handle)

    # The catalogue carries the survey's `trees` array verbatim on every island,
    # plus its tier and its workshop id, so the cohort rule is derived from the
    # same artefact the seats are written against - no second source to drift.
    profiles = [{"workshopId": island["asset"], "tier": island["tier"],
                 "trees": island["trees"]} for island in catalog["islands"]]
    inference = WoodInference(profiles)
    by_asset = {profile["workshopId"]: profile for profile in profiles}

    islands = []
    total = 0
    short = []
    sources = Counter()
    near_misses = []
    for island in catalog["islands"]:
        woods, wood_source = woods_for(by_asset[island["asset"]], inference)
        if woods is None:
            # "No trees", verbatim from a volunteer who stood on it. Honoured.
            sources["survey-none"] += 1
            continue

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

        # Pass 1: the stand a graduating player walks into. Pass 2: the rest of
        # the island, with pass 1 held as occupied so the two never overlap.
        # Both draw from the SAME measured table under the SAME rules; only the
        # candidate set differs.
        pad = island["landing"]
        seats = seatable(surface["points"], pad)
        near_target = min(LANDING_NEAR_TREES, target)
        near = spaced(within(seats, pad, LANDING_NEAR_RADIUS),
                      near_target, TREE_MIN_SPACING, anchors, TREE_MIN_UPWARD_NORMAL)
        picked = near + spaced(seats, target - len(near), TREE_MIN_SPACING,
                               anchors + [p[:3] for p in near], TREE_MIN_UPWARD_NORMAL)
        if len(picked) < target:
            short.append((island["name"], len(picked), target))
        if not near:
            near_misses.append(island["name"])

        islands.append({
            "asset": island["asset"],
            "name": island["name"],
            "cells": cells,
            "woods": woods,
            "woodSource": wood_source,
            "points": [[round(p[0], 3), round(p[1], 3), round(p[2], 3)] for p in picked],
        })
        total += len(picked)
        sources[wood_source] += 1

    payload = {"schema": 2, "islands": islands}
    with open(OUTPUT, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, separators=(",", ":"))
        handle.write("\n")

    print("%d islands, %d trees" % (len(islands), total))
    for source in sorted(sources):
        print("  %-14s %3d islands, %5d trees"
              % (source, sources[source],
                 sum(len(x["points"]) for x in islands if x["woodSource"] == source)))
    if near_misses:
        print("no seat within %.0f m of the arrival pad (%d islands): %s"
              % (LANDING_NEAR_RADIUS, len(near_misses), ", ".join(near_misses)))
    if short:
        print("under target (surface too small or too steep):")
        for name, got, want in short:
            print("  %-32s %3d / %3d" % (name, got, want))


if __name__ == "__main__":
    main()
