"""Sanity-check the offline surface extraction against independent signals.

    uv run --with UnityPy python verify_extract.py <bundle>

REWRITTEN 2026-08-08.  The previous version's check 1 asked "are all grid-cell
transforms unrotated / unit scale?" and answered "0/497 rotated, 0/497
non-unit-scale" — and that answer was TRUE and USELESS, because it only looked
at the MeshFilter's own GameObject.  The x4 scale lives on its PARENT, the cell
GameObject.  Check 1 now audits the entire chain to the root, which is the only
version of the question that could have caught the bug.
"""
import collections
import re
import sys

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

from island_surface import IslandBundle
from unity_transform import transform_point

b = IslandBundle(sys.argv[1])
isd = b.surface_data
cells = b.lod0_cells()
N = len(cells)
print(f"bundle: {sys.argv[1].split('/')[-1]}   LOD0 cells: {N}")

print("\n=== check 1: TRS audit of the WHOLE chain, not just the leaf ===")
rot_nodes = collections.Counter()
scale_nodes = collections.Counter()
depths = collections.Counter()
for mf, _mo in cells:
    ch = b.chain(mf["m_GameObject"]["m_PathID"])
    depths[len(ch)] += 1
    for depth, (pid, t) in enumerate(ch):
        q = t["m_LocalRotation"]
        s = t["m_LocalScale"]
        if abs(q["w"] - 1.0) > 1e-5 or abs(q["x"]) + abs(q["y"]) + abs(q["z"]) > 1e-5:
            rot_nodes[depth] += 1
        if max(abs(s["x"] - 1), abs(s["y"] - 1), abs(s["z"] - 1)) > 1e-5:
            scale_nodes[depth] += 1
            b._last_scale = (s["x"], s["y"], s["z"])
print(f"  chain depths (leaf..root): {dict(depths)}")
print(f"  rotated nodes by depth   : {dict(rot_nodes) or 'none'}")
print(f"  non-unit-scale by depth  : {dict(scale_nodes) or 'none'}"
      f"{'  e.g. ' + str(getattr(b, '_last_scale', '')) if scale_nodes else ''}")
if scale_nodes or rot_nodes:
    print("  => summing m_LocalPosition WOULD BE WRONG here. Full TRS required.")

print("\n=== check 2: does the cell NAME (i,j,k) predict its transform origin? ===")
samples = []
for mf, mo in cells[:400]:
    nm = mo.read_typetree()["m_Name"]
    m = re.match(r"\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)_LOD0", nm)
    if not m:
        continue
    ijk = tuple(float(g) for g in m.groups())
    wm = b.world_matrix(mf["m_GameObject"]["m_PathID"])
    samples.append((ijk, transform_point(wm, (0.0, 0.0, 0.0))))
for ijk, wp in samples[:4]:
    print(f"  cell {ijk} -> origin {tuple(round(v,2) for v in wp)}")
d = collections.defaultdict(list)
for (i, j, k), wp in samples:
    d[(j, k)].append((i, wp[0]))
CELLSZ = None
for key, vals in d.items():
    vals.sort()
    if len(vals) >= 2:
        CELLSZ = (vals[-1][1] - vals[0][1]) / (vals[-1][0] - vals[0][0])
        print(f"  inferred cell size along X = {CELLSZ:.2f} m  "
              f"(row j,k={key}, {len(vals)} cells)")
        break

print("\n=== check 3: do MeshCollider meshes == lod0Meshes? ===")
lod0_meshpids = {mf["m_Mesh"]["m_PathID"] for mf, _ in cells}
colpids = set()
ncol = 0
for o in b.env.objects:
    if str(o.type.name) == "MeshCollider":
        ncol += 1
        mp = o.read_typetree().get("m_Mesh", {}).get("m_PathID")
        if mp:
            colpids.add(mp)
print(f"  MeshColliders: {ncol}, distinct collider meshes: {len(colpids)}")
print(f"  collider meshes that ARE lod0Meshes: {len(colpids & lod0_meshpids)} / {len(colpids)}")

print("\n=== check 4: THE TILING PROOF — mesh-local extent x scale must equal cell pitch ===")
mf, mo = cells[0]
h = MeshHandler(mo.read())
h.process()
vs = h.m_Vertices
xs = [v[0] for v in vs]
ys = [v[1] for v in vs]
zs = [v[2] for v in vs]
wm = b.world_matrix(mf["m_GameObject"]["m_PathID"])
o0 = transform_point(wm, (0.0, 0.0, 0.0))
lo = transform_point(wm, (min(xs), min(ys), min(zs)))
hi = transform_point(wm, (max(xs), max(ys), max(zs)))
print(f"  mesh '{mo.read_typetree()['m_Name']}' verts={len(vs)}")
print(f"  MESH-LOCAL   x[{min(xs):.1f},{max(xs):.1f}] y[{min(ys):.1f},{max(ys):.1f}] "
      f"z[{min(zs):.1f},{max(zs):.1f}]  (span {max(xs)-min(xs):.1f} m)")
print(f"  cell transform origin = {tuple(round(v,2) for v in o0)}")
print(f"  ISLAND-LOCAL x[{lo[0]:.1f},{hi[0]:.1f}] y[{lo[1]:.1f},{hi[1]:.1f}] "
      f"z[{lo[2]:.1f},{hi[2]:.1f}]  (span {hi[0]-lo[0]:.1f} m)")
if CELLSZ:
    span = hi[0] - lo[0]
    print(f"  composed span {span:.1f} m vs cell pitch {CELLSZ:.1f} m -> "
          f"{'TILES (gap-free)' if span >= CELLSZ - 0.5 else 'LEAVES A GAP — extraction is wrong'}")
    print(f"  (raw mesh-local span was {max(xs)-min(xs):.1f} m; summing localPosition alone "
          f"would leave a {CELLSZ-(max(xs)-min(xs)):.1f} m hole between every pair of cells)")
ns = h.m_Normals
print(f"  normals present: {ns is not None and len(ns)==len(vs)}; "
      f"first normal {tuple(round(c,3) for c in ns[0][:3])}, "
      f"|n|={sum(c*c for c in ns[0][:3])**0.5:.4f}")

print("\n=== check 5: X-coverage — the composed surface must be one contiguous run ===")
occ = sorted({int(p[0] // 4) for p in b.iter_surface_vertices()})
runs = 1 + sum(1 for a, c in zip(occ, occ[1:]) if c != a + 1)
span = occ[-1] - occ[0] + 1
print(f"  {len(occ)}/{span} 4 m X-slices occupied, {runs} contiguous run(s)"
      f"{'  OK' if runs == 1 else '  <-- HOLES: extraction is wrong'}")

print("\n=== check 6: IslandMetaData ===")
if b.meta:
    print("  ", {k: v for k, v in b.meta.items() if k not in ("m_Script", "m_GameObject")})
