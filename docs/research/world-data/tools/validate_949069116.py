"""GROUND TRUTH CHECK for the TRS fix.

findings-spawn.md records a real session on island 949069116: the player was
applied at island-local (0,0,0), free-fell, and came to rest at **y = -31.2**.
That is the only empirically known altitude anywhere in this research, so it is
the only thing that can falsify the extractor.

This script re-extracts the island with the OLD (sum-of-localPosition) walk and
the NEW (full TRS) walk and reports the surface altitude in the centre column
under (x=0, z=0) for each.  Expected: old ~-55.8 (the number the findings
flagged), new ~-31.2.

    uv run --with UnityPy python validate_949069116.py <bundle>
"""
import sys

from UnityPy.helpers.MeshHelper import MeshHandler

from island_surface import IslandBundle
from unity_transform import apply3, normal_matrix, normalize, transform_point

BUNDLE = sys.argv[1] if len(sys.argv) > 1 else \
    "/home/ttanurhan/Games/WorldsAdrift/Assets/unity/949069116@island_unityclient"
b = IslandBundle(BUNDLE)


def old_offset(go_pid):
    """The original offs(): sum of m_LocalPosition, rotation and scale ignored."""
    x = y = z = 0.0
    for _pid, t in b.chain(go_pid):
        lp = t["m_LocalPosition"]
        x += lp["x"]
        y += lp["y"]
        z += lp["z"]
    return x, y, z


old_pts = []
new_pts = []
for mf, mo in b.lod0_cells():
    h = MeshHandler(mo.read())
    h.process()
    vs = h.m_Vertices or []
    ns = h.m_Normals or []
    if len(ns) != len(vs):
        ns = [(0.0, 1.0, 0.0)] * len(vs)
    go = mf["m_GameObject"]["m_PathID"]
    ox, oy, oz = old_offset(go)
    m = b.world_matrix(go)
    nm = normal_matrix(m)
    for k in range(len(vs)):
        v = vs[k]
        old_pts.append((v[0] + ox, v[1] + oy, v[2] + oz, ns[k][1]))
        p = transform_point(m, v)
        n = normalize(apply3(nm, ns[k]))
        new_pts.append((p[0], p[1], p[2], n[1]))

print(f"island 949069116: {len(b.surface_data['lod0Meshes'])} LOD0 cells, "
      f"{len(new_pts)} vertices")


def column(pts, radius):
    """Upward-facing vertices within `radius` of the island-local Y axis."""
    r2 = radius * radius
    return sorted((p for p in pts if p[0] * p[0] + p[2] * p[2] <= r2 and p[3] > 0.4),
                  key=lambda p: -p[1])


GROUND_TRUTH = -31.2   # findings-spawn.md, session log line 5374 "landed, at rest"
for radius in (4.0, 8.0, 16.0):
    o = column(old_pts, radius)
    n = column(new_pts, radius)
    print(f"\n--- centre column, radius {radius:g} m "
          f"(old {len(o)} verts, new {len(n)} verts) ---")
    if o:
        print(f"  OLD sum-of-localPosition : top y = {o[0][1]:8.2f}   "
              f"err vs ground truth {o[0][1] - GROUND_TRUTH:+7.2f} m")
    if n:
        print(f"  NEW full TRS             : top y = {n[0][1]:8.2f}   "
              f"err vs ground truth {n[0][1] - GROUND_TRUTH:+7.2f} m")


def aabb(pts):
    return ([min(p[i] for p in pts) for i in range(3)],
            [max(p[i] for p in pts) for i in range(3)])


for label, pts in (("OLD", old_pts), ("NEW", new_pts)):
    lo, hi = aabb(pts)
    print(f"\n{label} island-local AABB  min={[round(v,1) for v in lo]}  "
          f"max={[round(v,1) for v in hi]}  "
          f"thickness(Y)={hi[1]-lo[1]:.1f} m")

# Cross-check: 64 m cell grid.  Cell meshes are authored 0..16 in mesh-local
# units; only a x4 scale makes them tile a 64 m grid without gaps.  Measure the
# gap between adjacent cell surfaces along X to show old = holes, new = closed.
print("\n=== grid closure: X coverage of the LOD0 surface ===")
for label, pts in (("OLD", old_pts), ("NEW", new_pts)):
    occ = sorted({int(p[0] // 4) for p in pts})
    runs = 1
    for a, c in zip(occ, occ[1:]):
        if c != a + 1:
            runs += 1
    span = occ[-1] - occ[0] + 1
    print(f"  {label}: {len(occ)}/{span} 4 m X-slices occupied, "
          f"{runs} contiguous run(s)")
