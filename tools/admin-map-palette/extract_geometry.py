#!/usr/bin/env python3
"""Pull the real map geometry out of the previous artefact's 'after' SVG.

The SVG is generated from the same release MapFile the admin page renders, so the
polygons, wall paths, island placements and cell labels in it ARE the real map.
We recover them into a palette-independent JSON model so the artefact can be
re-rendered with any candidate palette.
"""
import json
import re
import sys

SRC = sys.argv[1]
OUT = sys.argv[2]

CIVIDIS = {"#01295d": 1, "#4d5361": 2, "#848069": 3, "#c4b34a": 4}

text = open(SRC, encoding="utf-8").read()
lines = text.splitlines()

cells = []
walls = []
islands = []
haven_rect = None
haven_label = None

for ln in lines:
    m = re.match(r'^<path d="(M [^"]+)" fill="(#[0-9a-f]{6})"(.*)$', ln)
    if m and m.group(2) in CIVIDIS:
        d, fill, rest = m.groups()
        cells.append({
            "d": d,
            "tier": CIVIDIS[fill],
            "unassigned": "stroke-dasharray" in rest,
        })
        continue
    m = re.match(r'^<path d="(M [^"]+)" fill="none" stroke="(#[0-9a-f]{6})" stroke-width="([0-9.]+)"', ln)
    if m:
        d, stroke, w = m.groups()
        walls.append({"d": d, "stroke": stroke, "width": float(w)})
        continue
    m = re.match(r'^<use href="#(isl|hav)" x="([-0-9.e]+)" y="([-0-9.e]+)"', ln)
    if m:
        kind, x, y = m.groups()
        islands.append({"haven": kind == "hav", "x": float(x), "y": float(y)})
        continue
    m = re.match(r'^<rect x="([-0-9.]+)" y="([-0-9.]+)" width="([0-9.]+)" height="([0-9.]+)" fill="#17322f"', ln)
    if m:
        haven_rect = [float(v) for v in m.groups()]
        continue
    if 'HAVEN CORRIDOR' in ln:
        m = re.match(r'^<text x="([-0-9.]+)"', ln)
        haven_label = float(m.group(1))
        continue
    m = re.match(r'^<text x="([-0-9.]+)" y="([-0-9.]+)" text-anchor="middle".*?<tspan[^>]*>([^<]*)</tspan><tspan[^>]*>([^<]*)</tspan>', ln)
    if m:
        x, y, top, bottom = m.groups()
        # The label rides on the cell whose polygon contains it; recover its tier
        # from the "T<n> - Name" second line rather than from any colour.
        tier = int(re.search(r'T(\d)', bottom).group(1))
        cells_label = {
            "x": float(x), "y": float(y),
            "district": top, "tier": tier,
            "unassigned": top == "NO DISTRICT",
        }
        cells_label["tierText"] = bottom
        globals().setdefault("labels", []).append(cells_label)

labels = globals().get("labels", [])

# Pair each label with its polygon: labels are emitted in the same order as the
# polygons in the source SVG, and both come from the same biome loop.
assert len(labels) == len(cells), f"{len(labels)} labels vs {len(cells)} cells"
for cell, label in zip(cells, labels):
    assert cell["tier"] == label["tier"], (cell["tier"], label["tier"])
    assert cell["unassigned"] == label["unassigned"]
    cell["label"] = label

# Wall names, in the source's own type order, matched by the halo/stroke pairs.
WALL_NAMES = {
    "#74c9cf": "Wind Rift",
    "#9b86d8": "Storm Rift",
    "#d48388": "Typhon",
    "#e8963c": "Sand Storm",
    "#a9d6ed": "Ice Storm",
    "#ec8f88": "World End",
}
named = []
for w in walls:
    if w["stroke"] == "#071017":
        continue
    w["name"] = WALL_NAMES[w["stroke"]]
    named.append(w)

model = {
    "viewBox": [-18000.0, -18000.0, 36000.0, 36000.0],
    "ocean": "#09151d",
    "havenRect": haven_rect,
    "havenLabelX": haven_label,
    "cells": cells,
    "walls": named,
    "islands": islands,
}
json.dump(model, open(OUT, "w"), indent=1)
print(f"cells={len(cells)} walls={len(named)} islands={len(islands)}")
by_tier = {}
for c in cells:
    by_tier[c["tier"]] = by_tier.get(c["tier"], 0) + 1
print("cells per tier:", dict(sorted(by_tier.items())))
