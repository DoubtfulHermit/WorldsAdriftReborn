"""World-wide census of IslandProps placements across all 255 island bundles.

Answers one question: how many times did Bossa's designers place a prefab from
`IslandProps/Trees/` (65 of them exist in the shared library) into an island?

Reads only the `static_objects` / `small_static_objects` / `groups` TextAssets from
each bundle -- no meshes, so it is cheap.  GUIDs are resolved through the 1,347-entry
`guidlut` + `oldassetlut` tables already committed under world-data/haven/.

    systemd-run --user --scope -p MemoryMax=4G \
        uv run --with UnityPy python tree_prop_census.py
"""
import collections
import json
import os
import sys

import UnityPy

UNITY = "/home/ttanurhan/Games/WorldsAdrift/Assets/unity"
REPO = "/home/ttanurhan/Games/wa-loop-trees/docs/research"
OUT = sys.argv[1] if len(sys.argv) > 1 else "/tmp/tree_prop_census.json"

guidlut = json.load(open(f"{REPO}/world-data/haven/guidlut.json"))
oldlut = json.load(open(f"{REPO}/world-data/haven/oldassetlut.json"))


def resolve(name):
    if "/" in name:
        name = oldlut.get(name, "")
    return guidlut.get(name)


bundles = sorted(f for f in os.listdir(UNITY) if f.endswith("@island_unityclient"))
print(f"{len(bundles)} bundles", flush=True)

per_island = {}
world_cat = collections.Counter()
world_tree = collections.Counter()
world_asset = collections.Counter()
unresolved_total = 0

for n, b in enumerate(bundles):
    island = b.split("@")[0]
    env = UnityPy.load(os.path.join(UNITY, b))
    blobs = {}
    for o in env.objects:
        if str(o.type.name) != "TextAsset":
            continue
        d = o.read()
        nm = getattr(d, "m_Name", None) or getattr(d, "name", "")
        if nm in ("static_objects", "small_static_objects"):
            raw = getattr(d, "m_Script", None) or getattr(d, "script", "")
            if isinstance(raw, bytes):
                raw = raw.decode("utf-8", "replace")
            blobs[nm] = raw

    placements = []
    for nm, raw in blobs.items():
        try:
            placements += json.loads(raw)
        except Exception as e:
            print(f"  {island}/{nm}: parse failed {e}", flush=True)

    cat = collections.Counter()
    trees = collections.Counter()
    unres = 0
    for p in placements:
        path = resolve(p.get("name", ""))
        if not path:
            unres += 1
            continue
        parts = path.split("/")
        c = parts[1] if len(parts) > 1 else path
        cat[c] += 1
        world_cat[c] += 1
        world_asset[path] += 1
        if c == "Trees":
            trees[path] += 1
            world_tree[path] += 1

    unresolved_total += unres
    per_island[island] = {
        "placements": len(placements),
        "unresolved": unres,
        "categories": dict(cat),
        "tree_props": dict(trees),
    }
    if n % 25 == 0:
        print(f"  [{n}/{len(bundles)}] {island} {len(placements)} props "
              f"trees={sum(trees.values())}", flush=True)

total = sum(v["placements"] for v in per_island.values())
tree_total = sum(world_tree.values())
islands_with_trees = sorted(i for i, v in per_island.items() if v["tree_props"])

print()
print(f"islands                {len(per_island)}")
print(f"total placements       {total}")
print(f"unresolved GUIDs       {unresolved_total}")
print(f"TREE-PROP placements   {tree_total}")
print(f"islands with >=1 tree  {len(islands_with_trees)}")
print()
print("world category totals:")
for k, v in world_cat.most_common():
    print(f"  {v:8d}  {k}")

json.dump({
    "generated_by": "docs/research/loop/data/tree_prop_census.py",
    "bundles_swept": len(per_island),
    "total_placements": total,
    "unresolved_guids": unresolved_total,
    "tree_prop_placements": tree_total,
    "islands_with_tree_props": islands_with_trees,
    "world_category_totals": dict(world_cat),
    "world_tree_prop_totals": dict(world_tree),
    "world_asset_totals": dict(world_asset),
    "per_island": per_island,
}, open(OUT, "w"), indent=1, sort_keys=True)
print(f"\nwrote {OUT}")
