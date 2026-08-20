#!/usr/bin/env python3
"""Project the release map's 44 weather walls into the embedded runtime file.

PROVENANCE. The input is `docs/research/world-data/wamap-islands.json` -> "Walls",
the release MapFile's own wall table, 44 records of exactly {x1, z1, x2, z2, Type}.
That is the whole retail payload: `WorldEditorWallData.WallStoreData` had no tuning
fields, so nothing is being dropped here (findings-storm-walls.md section 9.3).

WHY A SEPARATE FILE rather than embedding wamap-islands.json into the Multiplayer
assembly: that file is 38 KB of islands, biomes and world info the wall feature
never reads, and it is already embedded in a DIFFERENT assembly
(WorldsAdriftServer, for the operator map). Two assemblies embedding the same big
blob for two unrelated reasons is how they drift.

WHAT THIS SCRIPT DOES NOT DO: it does not compute midpoints, orientations or
half-lengths. That arithmetic is the part that can be WRONG - `length` is a
HALF-length, and a sign error in the orientation flips a wall end for end - so it
lives in C# (`WallCatalog`) where it is unit-tested against hand-worked numbers,
not baked into a generated file nobody re-derives.

The wall id is the record's index in the source array, and that is load-bearing:
`WeatherWalls` keys its `WallData` by `wallId` and MERGES every segment sharing one
into a single axial extent, so two different walls sharing an id would fuse into
one wall of the first one's type.

Regenerate, never hand-edit:
    python3 tools/world-import/generate-release-wall-segments.py
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "docs" / "research" / "world-data"
OUT = ROOT / "WorldsAdriftRebornGameServer.Multiplayer" / "Walls" / "release-wall-segments.json"

# WorldEditorWallData.WallType, PROVED off the decompiled client
# (Assets.Scripts.UI.WorldEditor/WorldEditorWallData.cs:11-19).
WALL_TYPES = {0: "WindRift", 1: "StormRift", 2: "Typhon",
              3: "SandStorm", 4: "IceStorm", 5: "WorldEndWall"}


def main():
    world = json.loads((DATA / "wamap-islands.json").read_text())
    walls = world["Walls"]

    records = []
    for index, wall in enumerate(walls):
        if set(wall) != {"x1", "z1", "x2", "z2", "Type"}:
            raise ValueError(f"wall {index} has unexpected fields {sorted(wall)}")
        kind = wall["Type"]
        if kind not in WALL_TYPES:
            raise ValueError(f"wall {index} has unknown type {kind}")
        if wall["x1"] == wall["x2"] and wall["z1"] == wall["z2"]:
            raise ValueError(f"wall {index} is degenerate; it has no direction")
        records.append({"id": index, "type": kind,
                        "x1": wall["x1"], "z1": wall["z1"],
                        "x2": wall["x2"], "z2": wall["z2"]})

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(
        {"schema": 1,
         "source": "docs/research/world-data/wamap-islands.json#Walls",
         "walls": records}, indent=1) + "\n")

    counts = {}
    for record in records:
        counts[record["type"]] = counts.get(record["type"], 0) + 1
    print(f"wrote {len(records)} walls to {OUT.relative_to(ROOT)}")
    for kind in sorted(counts):
        print(f"  {kind} {WALL_TYPES[kind]}: {counts[kind]}")


if __name__ == "__main__":
    main()
