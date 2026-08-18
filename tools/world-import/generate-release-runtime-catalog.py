#!/usr/bin/env python3
"""Build the compact, runtime-owned release-island catalogue from preserved evidence."""

import json
from collections import Counter
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from metal_inference import MetalInference, metals_for  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "docs/research/world-data"
OUTPUT = ROOT / "WorldsAdriftRebornGameServer.Multiplayer/Islands/release-runtime-catalog.json"
HAVEN_ASSET = "1431299145"


def fnv_key(point):
    value = 14695981039346656037
    for coordinate in point[:3]:
        quantized = round(coordinate * 1000)
        word = quantized & ((1 << 64) - 1)
        for _ in range(8):
            value ^= word & 0xff
            value = (value * 1099511628211) & ((1 << 64) - 1)
            word >>= 8
    return value


def spaced_once(points, target, spacing, occupied=(), min_normal=.90):
    result = []
    used = list(occupied)
    for point in sorted((p for p in points if p[4] >= min_normal), key=lambda p: (fnv_key(p), p[:3])):
        if len(result) >= target:
            break
        if all(sum((point[i] - other[i]) ** 2 for i in range(3)) >= spacing ** 2
               for other in used):
            result.append(point[:3])
            used.append(point[:3])
    return result


def spaced(points, target, spacing, occupied=()):
    for candidate_spacing, normal in ((spacing, .90), (spacing * .7, .90),
                                      (spacing * .4, .75), (5, .40), (0, .40)):
        result = spaced_once(points, target, candidate_spacing, occupied, normal)
        if len(result) == target:
            return result
    # A handful of exceptionally tiny meshes expose fewer decimated samples than
    # their surveyed databank count (Belial: 3 samples, 5 banks). Fill only with
    # deterministic midpoints between measured samples, keeping every coordinate
    # inside the sampled surface rather than inventing an arbitrary offset.
    bases = list(result)
    for left in range(len(bases)):
        for right in range(left + 1, len(bases)):
            if len(result) >= target:
                return result
            result.append([(bases[left][axis] + bases[right][axis]) / 2 for axis in range(3)])
    return result


def shell(points, rays=16):
    """A small radial silhouette used to build a non-physical mesh client-side."""
    bins = [None] * rays
    for point in points:
        x, y, z = point[:3]
        index = int(((math.atan2(z, x) + math.pi) / (2 * math.pi)) * rays) % rays
        radius = x * x + z * z
        if bins[index] is None or radius > bins[index][0]:
            bins[index] = (radius, x, z)

    # An empty angular bin used to emit a UNIT vector, which put a 1 m radius
    # point between neighbours hundreds of metres out and pinched the silhouette
    # into a spike. 12 of the 254 islands hit this, 83 points in total, the worst
    # spanning 1 m against a real 599 m extent. Interpolate the missing radius
    # from the nearest MEASURED bins on either side instead: the angle is the
    # bin's own, and the radius stays bounded by real samples rather than being
    # invented. A wholly empty ray set has no silhouette to state and is refused.
    measured = [i for i, entry in enumerate(bins) if entry is not None]
    if not measured:
        raise ValueError("island surface produced no radial samples")
    outline = []
    for index, entry in enumerate(bins):
        if entry is not None:
            outline.append([round(entry[1], 1), round(entry[2], 1)])
            continue
        # Interpolate the POSITION between the nearest measured samples, not the
        # radius. Reusing a neighbour's radius at this bin's angle overshoots badly
        # on a long or concave island - it put 66 points outside their own AABB,
        # the worst by 383 m. A point on the chord between two measured samples is
        # inside their convex hull by construction, which is the same rule the
        # databank/deposit filler above already follows.
        before = max((i for i in measured if i <= index), default=measured[-1] - rays)
        after = min((i for i in measured if i >= index), default=measured[0] + rays)
        span = (after - before) % rays or rays
        weight = ((index - before) % rays) / span
        start = bins[before % rays]
        end = bins[after % rays]
        outline.append([round(start[1] * (1 - weight) + end[1] * weight, 1),
                        round(start[2] * (1 - weight) + end[2] * weight, 1)])
    return outline


# --- landing points -------------------------------------------------------
#
# ONE point per island a player may be TELEPORTED onto: the arrival pad the
# Wilderness shrine uses. This is deliberately a different question from
# `spaced()` above, which scatters resources: a resource may sit on a slope or a
# ledge, a player arriving from a loading screen may not. The server has no
# terrain query and never will, so the only defence against dropping somebody
# into a hole is to land them on an EVIDENCED, WELL-SUPPORTED surface sample -
# exactly the criteria the hand-derived Haven and Mental Facility points were
# written against (docs/research/findings-haven.md,
# docs/research/findings-first-region-terrain.md), now applied by a committed
# script instead of by hand.
#
# The surface tables are one representative LOD0 vertex per 8 m voxel, so
# neighbours land 8 m away on the cardinals and 11.31 m away on the diagonals.
LANDING_SUPPORT_RADIUS = 12.0   # reaches both, and nothing in the next ring
LANDING_OVERHEAD_RADIUS = 4.0   # roughly a character capsule's shoulders
LANDING_OVERHEAD_CLEAR = 3.0    # ... and its head, plus slack

# Progressive relaxation: (min upward normal, min supporting columns, max step).
# The first rung that yields any candidate wins, so a normal island is judged by
# the strictest rule and only genuinely poor meshes fall through. Every rung is
# still a MEASURED sample - the ladder never invents a coordinate.
LANDING_LADDER = (
    (0.98, 8, 2.5),
    (0.98, 6, 3.5),
    (0.95, 5, 4.0),
    (0.90, 4, 6.0),
    (0.75, 2, 12.0),
    (0.40, 0, float("inf")),
    (-1.0, 0, float("inf")),
)

# Islands whose landing point was derived and REVIEWED before this script
# existed. Kept verbatim so the generated field cannot contradict a coordinate
# the rest of the server already names.
#   1143725558 Mental Facility - TeleportPolicy.MentalFacilityName, local
#   (120.00, 34.26, -16.00), ny 0.990; see findings-first-region-terrain.md.
LANDING_OVERRIDES = {
    "1143725558": [120.0, 34.26, -16.0],
}

# --- the arrival pad is also a resource anchor (WAREBORN TUNING) ------------
# A graduating player is teleported onto `landing` and has to find ore from
# there. Hash-ordered scattering is uniform over the whole surface and does not
# know the pad exists: measured on the tier-1 set, the nearest deposit to an
# arrival pad ran to 256 m (Isle of Lynerea) with ten islands over 100 m. The
# first deposits an island fills are therefore drawn from an ANNULUS around its
# pad. This selects among the island's OWN measured samples under the same
# spacing and normal rules - it changes which sample wins, never what a sample is.
LANDING_NEAR_RADIUS = 60.0      # what this project is willing to call "on arrival"
LANDING_NEAR_DEPOSITS = 2       # enough for a first ore run, small next to the budget
# Nothing may be seated in the spot the player materialises in. The surface table
# is one representative sample per 8 m voxel, so this removes the pad's own sample
# and its immediate neighbours and nothing else. Without it the global scatter is
# free to pick the pad's own sample, and did: three tier-1 islands had a seat at
# exactly 0.0 m from the arrival point.
LANDING_CLEAR = 6.0


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


def columns_of(points):
    """The HIGHEST sample at each (x, z). A column is what "is there ground
    beside me" and "is there rock over my head" are both really asking about,
    and an island's underside samples would otherwise answer both wrongly."""
    highest = {}
    for point in points:
        key = (round(point[0], 1), round(point[2], 1))
        if key not in highest or point[1] > highest[key]:
            highest[key] = point[1]
    return [(key[0], height, key[1]) for key, height in highest.items()]


def measure(tops, point, max_step):
    """(supporting columns, worst step, blocked overhead) for one sample."""
    support = 0
    step = 0.0
    for column_x, column_y, column_z in tops:
        offset = (column_x - point[0]) ** 2 + (column_z - point[2]) ** 2
        if (offset <= LANDING_OVERHEAD_RADIUS ** 2
                and column_y >= point[1] + LANDING_OVERHEAD_CLEAR):
            return 0, 0.0, True
        if 0.0 < offset <= LANDING_SUPPORT_RADIUS ** 2:
            rise = abs(column_y - point[1])
            if rise <= max_step:
                support += 1
                step = max(step, rise)
    return support, step, False


def landing(points, pinned=None):
    """The single arrival point on one island, in island-local metres.

    Deterministic: no RNG, no clock, and every tie broken on the point's own
    coordinates, so the same surface table always yields the same pad.

    `pinned` names an already-reviewed sample to keep; it is still MEASURED
    here, so a reviewed point reports its support honestly rather than being
    exempt from the same numbers everything else is judged by.
    """
    tops = columns_of(points)

    if pinned is not None:
        match = next((p for p in points if p[:3] == pinned), None)
        if match is None:
            raise ValueError("reviewed landing point is not a measured sample")
        support, step, _ = measure(tops, match, LANDING_LADDER[0][2])
        return {"x": match[0], "y": match[1], "z": match[2], "ny": match[4],
                "support": support, "step": round(step, 2), "reviewed": True}

    for min_normal, min_columns, max_step in LANDING_LADDER:
        best = None
        for point in points:
            if point[4] < min_normal:
                continue
            support, step, blocked = measure(tops, point, max_step)
            if blocked or support < min_columns:
                continue
            # Flattest first, then most upward, then broadest, then coordinates.
            key = (round(step, 3), -round(point[4], 3), -support,
                   point[0], point[1], point[2])
            if best is None or key < best[0]:
                best = (key, point, support, step)
        if best is not None:
            _, point, support, step = best
            return {"x": point[0], "y": point[1], "z": point[2],
                    "ny": point[4], "support": support, "step": round(step, 2),
                    "reviewed": False}
    raise ValueError("island surface produced no landing candidate")


def main():
    world = json.loads((DATA / "wamap-islands.json").read_text())
    survey = json.loads((DATA / "cardinal-guild-islands.json").read_text())
    surveys = {f["properties"]["workshopId"]: f["properties"] for f in survey["features"]}
    biomes = world["Biomes"]
    placements = {}
    for island in world["Islands"]:
        asset = island["Island"].removesuffix(".json")
        if asset != HAVEN_ASSET:
            if asset in placements:
                raise ValueError(f"duplicate ordinary island asset {asset}")
            placements[asset] = island
    if set(placements) != set(surveys):
        raise ValueError("release MapFile and survey workshop ids do not join exactly")

    inference = MetalInference(list(surveys.values()))
    result = []
    null_cells = sorted((b for b in biomes if b.get("District") is None), key=lambda b: (b["z"], b["x"]))
    for asset in sorted(placements, key=int):
        placed = placements[asset]
        profile = surveys[asset]
        cell = min(biomes, key=lambda b: (placed["x"] - b["x"]) ** 2 + (placed["z"] - b["z"]) ** 2)
        district = cell.get("District")
        cell_id = district or f"unassigned-t{cell['Type']}-{null_cells.index(cell) + 1}"
        surface = json.loads((DATA / "island-surfaces" / f"{asset}.json").read_text())
        points = surface["points"]
        cell_count = surface["meta"]["cells"]
        # WHICH metals and HOW MANY deposits are two independent questions, and
        # conflating them is what left 216 islands barren. The count has always
        # been a function of the island's own LOD0 cell count - the same
        # `IslandMeshCount` retail's island reported up to component 1010's
        # spawner - and never depended on the survey. It was gated behind
        # `if metals` only because an unsurveyed island had no metal NAME to
        # stamp on a deposit. metals_for() answers that question separately and
        # states its own provenance, so the density rule now applies to every
        # island exactly as it always applied to the surveyed 38.
        metals, metal_source = metals_for(profile, inference)
        deposit_count = math.ceil(cell_count * .05)
        # The pad is chosen first because the deposits are now placed relative to
        # it. It does not depend on them, so nothing is circular.
        pad = landing(points, LANDING_OVERRIDES.get(asset))
        seats = seatable(points, pad) or points
        near_deposits = spaced(within(seats, pad, LANDING_NEAR_RADIUS),
                               min(LANDING_NEAR_DEPOSITS, deposit_count), 35)
        deposit_points = near_deposits + spaced(
            seats, deposit_count - len(near_deposits), 35, near_deposits)
        databank_points = spaced(seats, profile["databanks"], 30, deposit_points)
        if len(databank_points) < profile["databanks"]:
            # The databank COUNT is recovered evidence - the survey counted them on
            # all 254 islands - while the landing clearance is this project's own
            # tuning. On a handful of rocks with barely more samples than banks the
            # two collide, and evidence wins: those islands seat their full surveyed
            # count over the unfiltered surface. Four banks across 254 islands.
            databank_points = spaced(points, profile["databanks"], 30, deposit_points)
        aabb = surface["meta"]["localAABB"]
        result.append({
            "asset": asset,
            "name": profile["name"],
            "slug": profile["slug"],
            "x": placed["x"], "y": placed["y"], "z": placed["z"],
            "district": district, "cell": cell_id, "cellTier": cell["Type"],
            "tier": profile["tier"],
            "culture": profile["type"], "databanks": profile["databanks"],
            "revival": profile["revivalChambers"], "dangerous": profile["dangerous"],
            "turrets": profile["turrets"], "trees": profile.get("trees") or [],
            # pveMetals/pvpMetals stay EXACTLY as the survey recorded them,
            # empty arrays included. `metals` is the effective table the
            # deposits below were stamped from and `metalSource` says where it
            # came from; the evidence is never overwritten by the inference.
            "pveMetals": [{"name": m["name"], "quality": m["quality"]}
                          for m in profile.get("pveMetals") or []],
            "pvpMetals": [{"name": m["name"], "quality": m["quality"]}
                           for m in profile.get("pvpMetals") or []],
            "metals": [{"name": m["name"], "quality": m["quality"]} for m in metals],
            "metalSource": metal_source,
            "aabb": [*aabb["min"], *aabb["max"]],
            "landing": pad,
            "shell": shell(points),
            "deposits": [{"x": p[0], "y": p[1], "z": p[2],
                          "metal": metals[i % len(metals)]["name"],
                          "quality": metals[i % len(metals)]["quality"]}
                         for i, p in enumerate(deposit_points)],
            "databankPoints": [{"x": p[0], "y": p[1], "z": p[2]}
                               for p in databank_points],
        })
    if len(result) != 254:
        raise ValueError(f"expected 254 ordinary islands, got {len(result)}")
    OUTPUT.write_text(json.dumps({"schema": 2, "islands": result}, separators=(",", ":")) + "\n")
    sources = Counter(island["metalSource"] for island in result)
    print(f"wrote {OUTPUT.relative_to(ROOT)}: {len(result)} islands, "
          f"{sum(len(x['deposits']) for x in result)} deposits, "
          f"{sum(len(x['databankPoints']) for x in result)} databanks, "
          f"{len(result)} landing points "
          f"({sum(1 for x in result if x['landing']['ny'] >= .98)} on a >=0.98 normal, "
          f"{sum(1 for x in result if x['landing']['reviewed'])} reviewed)")
    for source in sorted(sources):
        print(f"  {source:14s} {sources[source]:3d} islands, "
              f"{sum(len(x['deposits']) for x in result if x['metalSource'] == source):5d} deposits")


if __name__ == "__main__":
    main()
