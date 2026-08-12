"""Sequentially re-extract every island bundle with the TRS-composed walk.

    systemd-run --user --scope -p MemoryMax=4G \
        uv run --with UnityPy python sweep_all.py <bundle-dir> <outdir>

SEQUENTIAL ON PURPOSE.  A parallel fan-out over these bundles has frozen this
machine before; the whole sweep takes ~2 minutes serially, so there is nothing
to win.  Always run it inside the systemd scope above so a leak is capped.
"""
import gc
import glob
import json
import os
import sys
import time

import island_surface
from island_surface import IslandBundle

bundledir = sys.argv[1]
outdir = sys.argv[2]
CELL = 8.0

paths = sorted(glob.glob(os.path.join(bundledir, "*@island_unityclient")))
print(f"{len(paths)} bundles -> {outdir}", flush=True)
t00 = time.time()
summary = []
for i, p in enumerate(paths, 1):
    t0 = time.time()
    b = IslandBundle(p)
    cand = {}
    tv = up = 0
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
           "transform": "TRS-composed",
           "sec": round(time.time() - t0, 2)}
    json.dump({"meta": rec, "cell": CELL, "points": list(cand.values())},
              open(os.path.join(outdir, wid + ".json"), "w"))
    summary.append(rec)
    del b, cand
    island_surface.UnityPy.reset() if hasattr(island_surface.UnityPy, "reset") else None
    gc.collect()
    if i % 25 == 0 or i == len(paths):
        print(f"  {i}/{len(paths)}  {time.time()-t00:.0f}s", flush=True)

bad = [r for r in summary if not r["hasSurfaceData"] or not r["verts"]]
print(f"done in {time.time()-t00:.0f}s; islands with no LOD0 surface: {len(bad)}")
for r in bad:
    print("   ", r["island"])
