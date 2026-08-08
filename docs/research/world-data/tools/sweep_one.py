"""Per-island: extract LOD0 surface, thin to spawn candidates in ISLAND-LOCAL space.

    uv run --with UnityPy python sweep_one.py <bundle> <outdir> [cellsize]

FIXED 2026-08-08: this used to accumulate only ``m_LocalPosition`` up the
hierarchy.  Every LOD0 grid cell has ``m_LocalScale = (4,4,4)``, so every vertex
was placed at a quarter of its true offset inside its 64 m cell — the terrain
came out as disconnected 16 m patches with 48 m gaps, at wrong altitudes.  The
walk now composes full TRS matrices (``island_surface`` / ``unity_transform``).
Output format is unchanged; the numbers are not.  Re-run everything.
"""
import json
import os
import sys
import time

from island_surface import IslandBundle

p = sys.argv[1]
outdir = sys.argv[2]
CELL = float(sys.argv[3]) if len(sys.argv) > 3 else 8.0

t0 = time.time()
b = IslandBundle(p)

cand = {}
tv = 0
up = 0
mn = [1e9] * 3
mx = [-1e9] * 3
for (px, py, pz, nx, ny, nz) in b.iter_surface_vertices():
    tv += 1
    for a, v in enumerate((px, py, pz)):
        if v < mn[a]:
            mn[a] = v
        if v > mx[a]:
            mx[a] = v
    if ny <= 0.4:
        continue
    up += 1
    key = (int(px // CELL), int(py // CELL), int(pz // CELL))
    if key not in cand:
        cand[key] = (round(px, 2), round(py, 2), round(pz, 2),
                     round(nx, 3), round(ny, 3), round(nz, 3))

wid = os.path.basename(p).split("@")[0]
isd = b.surface_data
rec = {"island": wid,
       "mib": round(os.path.getsize(p) / 1048576, 2),
       "hasProxy": "IslandProxyVisualizer" in b.scripts,
       "hasSurfaceData": isd is not None,
       "cells": len(isd["lod0Meshes"]) if isd else 0,
       "verts": tv, "upVerts": up, "candidates": len(cand),
       "localAABB": {"min": [round(v, 1) for v in mn],
                     "max": [round(v, 1) for v in mx]} if tv else None,
       "transform": "TRS-composed",   # provenance: distinguishes from the broken tables
       "sec": round(time.time() - t0, 2)}
json.dump({"meta": rec, "cell": CELL, "points": list(cand.values())},
          open(os.path.join(outdir, wid + ".json"), "w"))
print(json.dumps(rec))
