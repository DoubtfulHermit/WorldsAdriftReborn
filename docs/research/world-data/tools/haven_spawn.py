"""Derive the Haven player spawn from the TRS-corrected LOD0 surface.

    uv run --with UnityPy python haven_spawn.py

Target is the ruined metal camp (the only constructed area on 1431299145):
island-local centroid (205.3, 15.2, -0.8), props spanning x 164..223,
y -0.5..25.6, z -31..27.

Everything printed here is measured from the bundle except where labelled.
"""
import json
import math
import os

from island_surface import IslandBundle

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.dirname(HERE)
BUNDLE = "/home/ttanurhan/Games/WorldsAdrift/Assets/unity/1431299145@island_unityclient"

CAMP = (205.3, 15.2, -0.8)          # findings-haven.md, corroborated by the groups TextAsset
SEARCH_R = 45.0                     # horizontal radius around the camp centroid
STANDOFF = 2.0                      # metres of drop, so the capsule cannot interpenetrate
INSTANCE5 = (17004.4300, -318.6693420, -1134.16748)   # island instance #5 world position
FIXED = 4096


def to_fixed(d):
    """ToFixedPoint: (long)(d * 4096) — C# cast truncates TOWARD ZERO."""
    return int(d * FIXED)            # Python int() also truncates toward zero


# ------------------------------------------------------------------ surface
b = IslandBundle(BUNDLE)
verts = list(b.iter_surface_vertices())
print(f"Haven LOD0: {len(b.surface_data['lod0Meshes'])} cells, {len(verts)} vertices")

lo = [min(v[i] for v in verts) for i in range(3)]
hi = [max(v[i] for v in verts) for i in range(3)]
print(f"island-local AABB min={[round(x,1) for x in lo]} max={[round(x,1) for x in hi]}")

# ------------------------------------------------------------------- props
props_file = os.path.join(DATA, "haven", "haven-props-resolved.json")
pj = json.load(open(props_file))
props = [(o["pos"]["x"], o["pos"]["y"], o["pos"]["z"], o["asset"])
         for o in pj["static_objects"] + pj["small_static_objects"] if "pos" in o]
print(f"props: {len(props)}")

camp_props = [p for p in props if 164 <= p[0] <= 223 and -31 <= p[2] <= 27]
print(f"props inside the camp footprint: {len(camp_props)}")

# ------------------------------------------------------ candidate selection
GRID = 4.0
vbuck = {}
for v in verts:
    vbuck.setdefault((int(v[0] // GRID), int(v[2] // GRID)), []).append(v)


def column(x, z, r):
    """All surface vertices within horizontal radius r of (x,z)."""
    out = []
    br = int(r // GRID) + 1
    bx, bz = int(x // GRID), int(z // GRID)
    for dx in range(-br, br + 1):
        for dz in range(-br, br + 1):
            for v in vbuck.get((bx + dx, bz + dz), ()):
                if (v[0] - x) ** 2 + (v[2] - z) ** 2 <= r * r:
                    out.append(v)
    return out


pbuck = {}
for p in props:
    pbuck.setdefault((int(p[0] // GRID), int(p[2] // GRID)), []).append(p)


def nearest_prop(x, y, z, r=40.0):
    best = (1e9, None)
    br = int(r // GRID) + 1
    bx, bz = int(x // GRID), int(z // GRID)
    for dx in range(-br, br + 1):
        for dz in range(-br, br + 1):
            for p in pbuck.get((bx + dx, bz + dz), ()):
                d = math.dist((x, y, z), p[:3])
                if d < best[0]:
                    best = (d, p)
    return best


def nearest_prop_h(x, z, r=40.0):
    """Nearest prop in the horizontal plane, ignoring altitude — this is what a
    standing capsule actually cares about; the camp's platforms are +22 m up."""
    best = (1e9, None)
    br = int(r // GRID) + 1
    bx, bz = int(x // GRID), int(z // GRID)
    for dx in range(-br, br + 1):
        for dz in range(-br, br + 1):
            for p in pbuck.get((bx + dx, bz + dz), ()):
                d = math.dist((x, z), (p[0], p[2]))
                if d < best[0]:
                    best = (d, p)
    return best


cands = {}
for v in verts:
    dh = math.dist((v[0], v[2]), (CAMP[0], CAMP[2]))
    if dh > SEARCH_R:
        continue
    if v[4] < 0.97:                       # dead flat only
        continue
    key = (round(v[0], 2), round(v[1], 2), round(v[2], 2))
    if key in cands:
        continue
    col = column(v[0], v[2], 1.5)
    above = max((c[1] for c in col), default=v[1])
    if above > v[1] + 0.5:                # something of the island is over us -> not the top
        continue
    below = min(c[1] for c in col)
    d, p = nearest_prop(v[0], v[1], v[2])
    dhp, php = nearest_prop_h(v[0], v[2])
    cands[key] = {"v": v, "clear": d, "prop": p, "clearH": dhp, "propH": php,
                  "colTop": above, "colBottom": below, "dh": dh}
cands = list(cands.values())

print(f"\nflat (ny>=0.97) top-surface vertices within {SEARCH_R:g} m of the camp centroid: "
      f"{len(cands)}")

# Clearance and proximity-to-the-camp pull in opposite directions, so show the
# trade-off explicitly instead of hiding it inside one arbitrary radius.
print("\n=== best prop clearance vs. how far from the camp centroid we allow ===")
for cap in (10, 15, 20, 25, 30, 45):
    sub = [c for c in cands if c["dh"] <= cap]
    if not sub:
        print(f"  <= {cap:2d} m : none")
        continue
    t = max(sub, key=lambda c: (c["clear"], c["v"][4]))
    v = t["v"]
    print(f"  <= {cap:2d} m : ({v[0]:7.2f},{v[1]:6.2f},{v[2]:7.2f}) ny={v[4]:.3f} "
          f"clear3D={t['clear']:5.2f} m clearH={t['clearH']:5.2f} m "
          f"dist={t['dh']:5.1f} m  ({len(sub)} candidates)")

# Choose inside the authored camp footprint: 25 m of the centroid keeps the
# player inside the ruin (props span x 164..223, z -31..27 => radius ~30 m).
CAP = 25.0
inside = [c for c in cands if c["dh"] <= CAP]
inside.sort(key=lambda c: (-c["clear"], -c["v"][4]))
print(f"\n=== top 8 by prop clearance, within {CAP:g} m of the camp centroid ===")
for c in inside[:8]:
    v = c["v"]
    print(f"  ({v[0]:7.2f},{v[1]:6.2f},{v[2]:7.2f})  ny={v[4]:.3f}  "
          f"clear3D={c['clear']:5.2f} m clearH={c['clearH']:5.2f} m  dist-to-centroid="
          f"{c['dh']:5.1f} m  underside y={c['colBottom']:7.2f}")

best = inside[0]
cands = inside
v = best["v"]
print("\n=== chosen ===")
print(f"  surface vertex island-local ({v[0]:.2f}, {v[1]:.2f}, {v[2]:.2f})  "
      f"normal ({v[3]:.3f}, {v[4]:.3f}, {v[5]:.3f})")
print(f"  nearest prop (3D) {best['clear']:.2f} m away: {best['prop'][3]} "
      f"@ ({best['prop'][0]:.1f},{best['prop'][1]:.1f},{best['prop'][2]:.1f})")
print(f"  nearest prop (horizontal) {best['clearH']:.2f} m away: {best['propH'][3]} "
      f"@ ({best['propH'][0]:.1f},{best['propH'][1]:.1f},{best['propH'][2]:.1f})")
print(f"  horizontal distance to camp centroid: "
      f"{math.dist((v[0],v[2]),(CAMP[0],CAMP[2])):.1f} m  "
      f"(3D {math.dist(v[:3], CAMP):.1f} m)")

# top-surface proof
col14 = column(v[0], v[2], 14.0)
ups = [c for c in col14 if c[4] > 0.4]
print(f"  TOP-SURFACE PROOF: within 14 m horizontally there are {len(col14)} surface "
      f"vertices; upward-facing ones span y {min(c[1] for c in ups):.2f}.."
      f"{max(c[1] for c in ups):.2f}")
print(f"    lowest surface vertex in the same 1.5 m column: y={best['colBottom']:.2f} "
      f"-> local slab thickness {v[1]-best['colBottom']:.1f} m")
allcol = column(v[0], v[2], 6.0)
print(f"    lowest surface vertex within 6 m: y={min(c[1] for c in allcol):.2f} "
      f"(island underside)")

# overhead
overhead = [p for p in props
            if math.dist((p[0], p[2]), (v[0], v[2])) < 6.0 and p[1] > v[1] + 2.0]
if overhead:
    lowest = min(overhead, key=lambda p: p[1])
    print(f"  overhead props within 6 m horizontally: {len(overhead)}, "
          f"lowest at y={lowest[1]:.1f} (+{lowest[1]-v[1]:.1f} m): {lowest[3]}")
else:
    print("  overhead props within 6 m horizontally: none")

# ------------------------------------------------------------------ output
spawn = (v[0], v[1] + STANDOFF, v[2])
world = (spawn[0] + INSTANCE5[0], spawn[1] + INSTANCE5[1], spawn[2] + INSTANCE5[2])
fp = tuple(to_fixed(c) for c in world)

isl_fp = tuple(to_fixed(c) for c in INSTANCE5)

print("\n" + "=" * 72)
print(f"SPAWN (surface + {STANDOFF:g} m stand-off)")
print(f"  island-local : ({spawn[0]:.2f}, {spawn[1]:.2f}, {spawn[2]:.2f})")
print(f"  world        : ({world[0]:.7f}, {world[1]:.7f}, {world[2]:.7f})")
print(f"  190602       : {{ {fp[0]}, {fp[1]}, {fp[2]} }}")
print(f"  ISLAND 190602: {{ {isl_fp[0]}, {isl_fp[1]}, {isl_fp[2]} }}")
print("=" * 72)

# The published table quotes island-local to 2 dp, so encode from THOSE numbers or
# the doc will not reproduce.  The rounding moves the point by <= 5 mm.
print("\n=== paste-ready, encoded from the 2-dp island-local values ===")


def encode(local):
    w = tuple(local[i] + INSTANCE5[i] for i in range(3))
    f = tuple(to_fixed(c) for c in w)
    return w, f


dl = tuple(round(c, 2) for c in spawn)
dw, df = encode(dl)
print(f"  island-local ({dl[0]:.2f}, {dl[1]:.2f}, {dl[2]:.2f})")
print(f"  world        ({dw[0]:.7f}, {dw[1]:.7f}, {dw[2]:.7f})")
print(f"  190602       {{ {df[0]}, {df[1]}, {df[2]} }}")
print(f"  round-trip   {df[0]/FIXED:.5f}, {df[1]/FIXED:.5f}, {df[2]/FIXED:.5f}  "
      f"(max encode loss {max(abs(dw[i]-df[i]/FIXED) for i in range(3))*1000:.3f} mm)")

# runner-up, for the doc's FALLBACK line
alt = None
for c in cands[1:]:
    if math.dist((c["v"][0], c["v"][2]), (v[0], v[2])) > 8.0:
        alt = c
        break
if alt:
    av = alt["v"]
    asp = tuple(round(c, 2) for c in (av[0], av[1] + STANDOFF, av[2]))
    aw, afp = encode(asp)
    print(f"FALLBACK (>8 m away)")
    print(f"  island-local : ({asp[0]:.2f}, {asp[1]:.2f}, {asp[2]:.2f})  "
          f"ny={av[4]:.3f}  clearance {alt['clear']:.2f} m")
    print(f"  world        : ({aw[0]:.7f}, {aw[1]:.7f}, {aw[2]:.7f})")
    print(f"  190602       : {{ {afp[0]}, {afp[1]}, {afp[2]} }}")

# what the OLD table recommended, re-measured against the corrected surface
print("\n=== the previously published point, re-checked ===")
for label, (ox, oy, oz) in (("recommended (200.00, 3.96, 5.00)", (200.0, 3.96, 5.0)),
                            ("fallback    (192.00, 2.30, 16.00)", (192.0, 2.3, 16.0))):
    col = column(ox, oz, 2.0)
    ups = [c for c in col if c[4] > 0.4]
    if ups:
        top = max(c[1] for c in ups)
        print(f"  {label}: true surface at that XZ is y={top:.2f} "
              f"-> published Y was {oy-top:+.2f} m off "
              f"({'below' if oy < top else 'above'} the ground)")
    else:
        print(f"  {label}: NO upward-facing surface within 2 m of that XZ")
