#!/usr/bin/env python3
"""Dump every entity-prefab root name in resources.assets.

Improbable ships one GameObject per (prefab, worker) as "<name>_unityclient" /
"<name>_unityworker"; the client's asset DB is keyed on exactly that string
(WorkerSpecificAssetDatabaseTemplateProvider). So this file IS the catalogue of
prefab names a server may legally name in an AddEntityOp.
"""
import os, UnityPy

DATA = os.path.expanduser("~/Games/WorldsAdrift/UnityClient@Windows_Data")
env = UnityPy.load(os.path.join(DATA, "resources.assets"))
names = set()
for o in env.objects:
    if o.type.name != "GameObject":
        continue
    try:
        d = o.read_typetree()
    except Exception:
        continue
    n = d.get("m_Name", "")
    if n.endswith("_unityclient") or n.endswith("_unityworker"):
        names.add(n)

base = {}
for n in sorted(names):
    stem, _, worker = n.rpartition("_")
    base.setdefault(stem, set()).add(worker)

print("prefab\tclient\tworker")
for stem in sorted(base):
    w = base[stem]
    print(f"{stem}\t{'yes' if 'unityclient' in w else 'no'}\t{'yes' if 'unityworker' in w else 'no'}")
