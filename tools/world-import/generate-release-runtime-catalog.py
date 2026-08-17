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
        deposit_points = spaced(points, deposit_count, 35)
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
          f"{sum(len(x['databankPoints']) for x in result)} databanks")
    for source in sorted(sources):
        print(f"  {source:14s} {sources[source]:3d} islands, "
              f"{sum(len(x['deposits']) for x in result if x['metalSource'] == source):5d} deposits")


if __name__ == "__main__":
    main()
