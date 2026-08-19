#!/usr/bin/env python3
"""
Traces the four object sheets into the same vector form the fifty devices use.

WHY A SECOND SCRIPT AND NOT A SECOND TRACER. The maths is not repeated: this
imports contours(), simplify(), check_winding(), normalise(), quantise(),
path_data(), components() and rasterise() straight out of
tools/emblem-devices/trace_devices.py and calls them unchanged. What is new here
is ONLY the slicing, because these four sheets do not slice the way the device
sheet does, and the output form, because these are not going into the frozen
fifty. If the marching-squares core ever needs a fix it gets fixed in one place.

WHY THE SLICING HAD TO CHANGE. The device sheet's icons are each one connected
blob, so it groups them by dilating the ink four pixels and labelling what joins
up. These sheets are drawn in a dashed, broken-stroke style - a single torii is
forty separate marks with twelve-pixel gaps between them - so dilation either
fails to gather one icon or gathers two. Worse, on japan.png rows 1 and 5 the
printed numbers share a scanline band with the artwork, so the five-blank-row
split does not find five rows either.

So the grid is recovered from the printed numbers instead, which are the one
thing on these sheets that IS regular:

  rows     the ink bands grouped into five by their four largest vertical gaps,
           which does not care whether a number touches the art below it.
  columns  the nine widest ink-free vertical corridors in a row, AFTER the
           numbers are erased. Those nine are the cell walls: the tenth widest
           corridor is a gap between two dashes and is always at least twelve
           pixels narrower, and each of the nine is checked to fall between two
           consecutive printed numbers before it is used.

Every icon is then traced as ONE device: all the marks in its cell, together,
with no dilation and no merging. A dashed circle stays fifty dashes.

Dependencies: Pillow and numpy, the same two the device tracer needs.

Usage:  python3 tools/emblem-objects/trace_objects.py
"""

import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

sys.path.insert(0, os.path.join(ROOT, "tools", "emblem-devices"))
import trace_devices as td  # noqa: E402  - path has to be set first

SHEET_DIR = os.path.join(HERE, "sheets")
SVG_DIR = os.path.join(HERE, "svg")
NAMES = os.path.join(HERE, "object-names.json")
OUT_JSON = os.path.join(HERE, "emblem-objects.json")

COLUMNS = 10
ROWS = 5

# The four sheets, as (category, sheet file). The category is what a caller
# filters on and is part of every object's identity, so it does not change.
SHEETS = [
    ("japan", "japan.png"),
    ("objects", "objects.png"),
    ("shapes-outline", "shapes-empty.png"),
    ("shapes-solid", "shapes-full.png"),
]

# A printed number is at most this tall and this wide. Two digits are two
# components, so the width bound is per digit with room for a wide '0'.
LABEL_HEIGHT = 26
LABEL_WIDTH = 34

# How far below the top of a row group a printed number may start. The numbers
# sit on the group's first scanline; the artwork below them starts thirty-odd
# pixels down, which is what stops a grey icon's top mark being read as a label.
LABEL_TOP = 4

# Digits of one number are never further apart than this; two different numbers
# always are, because a whole icon sits between them.
LABEL_GAP = 12

# A corridor narrower than this is a gap between two dashes of one icon, not a
# cell wall. Measured: the narrowest real wall on these four sheets is 13px and
# the widest intra-icon gap is 20px, so the split is made by RANK - the nine
# widest - and this only guards against a sheet where that ranking is not clean.
MIN_WALL = 8


# ---------------------------------------------------------------- sheet slicing


def runs(flags):
    """The maximal runs of True in a boolean vector, as (start, end) inclusive."""
    out = []
    start = None
    for index, value in enumerate(flags):
        if value and start is None:
            start = index
        elif not value and start is not None:
            out.append((start, index - 1))
            start = None
    if start is not None:
        out.append((start, len(flags) - 1))
    return out


def row_groups(mask):
    """
    The five printed rows, as (top, bottom) inclusive.

    Split by the FOUR LARGEST vertical gaps rather than by counting blank
    scanlines, because on japan.png rows 1 and 5 the printed numbers touch the
    artwork underneath them and a blank-scanline count finds eight bands, not
    ten. The four largest gaps are the four between rows on every sheet: the
    widest gap inside a row - between a number and the art below it - is 15px,
    the narrowest gap between two rows is 30px.
    """
    bands = runs(mask.any(axis=1))
    if len(bands) < ROWS:
        raise SystemExit("found %d ink bands, cannot make %d rows" % (len(bands), ROWS))

    gaps = sorted(range(len(bands) - 1),
                  key=lambda i: bands[i + 1][0] - bands[i][1], reverse=True)
    cuts = sorted(gaps[:ROWS - 1])

    groups = []
    start = 0
    for cut in cuts:
        groups.append((bands[start][0], bands[cut][1]))
        start = cut + 1
    groups.append((bands[start][0], bands[-1][1]))
    return groups


def find_labels(band, rgb, top):
    """
    The printed numbers in a row group, as (x0, x1) spans in sheet coordinates
    plus the component ids to erase.

    A component is a number if it starts on the group's first scanlines, is
    small, and is neutral grey. All three are needed, and the reason is the same
    one the device tracer gives: seven of the shapes-empty icons are drawn in the
    same grey the numbers are set in, so grey alone would eat them.
    """
    labels, count = td.components(band)

    digits = []
    for index in range(1, count + 1):
        ys, xs = np.nonzero(labels == index)
        y0, y1 = int(ys.min()), int(ys.max())
        x0, x1 = int(xs.min()), int(xs.max())

        if y0 > LABEL_TOP or (y1 - y0) > LABEL_HEIGHT or (x1 - x0) > LABEL_WIDTH:
            continue

        patch = rgb[top + y0:top + y1 + 1, x0:x1 + 1]
        sub = (labels == index)[y0:y1 + 1, x0:x1 + 1]
        ink = patch[sub]
        # Neutral per pixel, not overall: antialiasing spreads a black glyph
        # across the whole ramp, so an overall range test passes anything.
        if int((ink.max(axis=1) - ink.min(axis=1)).max()) > 24:
            continue

        digits.append((x0, x1, index))

    if not digits:
        raise SystemExit("no printed numbers in the row group at y=%d" % top)

    digits.sort()
    groups = [[digits[0]]]
    for digit in digits[1:]:
        if digit[0] - groups[-1][-1][1] > LABEL_GAP:
            groups.append([digit])
        else:
            groups[-1].append(digit)

    if len(groups) != COLUMNS:
        raise SystemExit("the row group at y=%d has %d printed numbers, expected %d"
                         % (top, len(groups), COLUMNS))

    spans = [(min(d[0] for d in g), max(d[1] for d in g)) for g in groups]
    ids = [d[2] for g in groups for d in g]
    return spans, ids, labels


def column_walls(band, spans):
    """
    The nine cell walls of a row, as x coordinates.

    Taken as the NINE WIDEST ink-free corridors, then checked: wall k has to sit
    between printed number k and printed number k+1. That check is the whole
    point. Ranking alone would be a guess about how these sheets are drawn;
    ranking plus the check is a guess that fails loudly on the sheet where it
    stops being true, instead of quietly cutting an airship in half.
    """
    occupied = band.any(axis=0)
    corridors = [c for c in runs(~occupied) if c[0] > 0 and c[1] < len(occupied) - 1]
    corridors = [c for c in corridors if (c[1] - c[0] + 1) >= MIN_WALL]

    if len(corridors) < COLUMNS - 1:
        raise SystemExit("only %d candidate cell walls, need %d"
                         % (len(corridors), COLUMNS - 1))

    widest = sorted(corridors, key=lambda c: c[1] - c[0], reverse=True)[:COLUMNS - 1]
    widest.sort()

    walls = []
    for index, (a, b) in enumerate(widest):
        # Put the wall where the PRINTING says the cell starts - just left of the
        # next number - rather than in the middle of the corridor. The two are
        # the same when both neighbours are wide icons, but where a narrow icon
        # leaves a fifty-pixel corridor the middle of it drifts past the next
        # number and lands in the next cell. Clamping into the corridor keeps the
        # wall on ink-free pixels whatever the artwork does.
        wall = min(max(spans[index + 1][0] - 2, a), b)

        left = (spans[index][0] + spans[index][1]) / 2.0
        right = (spans[index + 1][0] + spans[index + 1][1]) / 2.0
        if not (left < wall < right):
            raise SystemExit(
                "cell wall %d at x=%d does not separate printed numbers %d and %d"
                % (index + 1, wall, index + 1, index + 2))
        walls.append(wall)

    return walls


def slice_sheet(rgb):
    """Cuts a sheet into 50 (x, y, mask, ink) cells in printed order."""
    mask = td.ink_mask(rgb)
    cells = []

    for top, bottom in row_groups(mask):
        band = mask[top:bottom + 1].copy()
        spans, ids, labels = find_labels(band, rgb, top)
        for index in ids:
            band[labels == index] = False

        walls = column_walls(band, spans)
        edges = [0] + walls + [band.shape[1]]

        for column in range(COLUMNS):
            piece = np.zeros_like(band)
            piece[:, edges[column]:edges[column + 1]] = band[:, edges[column]:edges[column + 1]]

            ys, xs = np.nonzero(piece)
            if len(ys) == 0:
                raise SystemExit("empty cell at row y=%d column %d" % (top, column + 1))

            y0, y1 = int(ys.min()), int(ys.max())
            x0, x1 = int(xs.min()), int(xs.max())
            sub = piece[y0:y1 + 1, x0:x1 + 1]

            colours = rgb[top + y0:top + y1 + 1, x0:x1 + 1][sub]
            ink = tuple(int(v) for v in np.median(colours, axis=0))

            cells.append({"x": x0, "y": top + y0, "mask": sub, "ink": ink})

    if len(cells) != COLUMNS * ROWS:
        raise SystemExit("sliced %d cells" % len(cells))
    return cells


# --------------------------------------------------------------------- output


def write_svg(path, name, category, index, loops, ink):
    data = td.path_data(loops)
    colour = "#%02X%02X%02X" % ink
    svg = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<!-- Wareborn alliance emblem object: %s (%s, sheet index %02d).\n'
        '     Traced by tools/emblem-objects/trace_objects.py.\n'
        '     Coordinates are thousandths of the painter\'s [-1, 1] box, y down,\n'
        '     filled NON-ZERO. The colour here is the source ink and is NOT part\n'
        '     of the object - the emblem system colours every layer itself. -->\n'
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-1000 -1000 2000 2000"\n'
        '     width="256" height="256">\n'
        '  <path fill="%s" fill-rule="nonzero" d="%s"/>\n'
        '</svg>\n'
    ) % (name, category, index, colour, data)

    with open(path, "w", encoding="utf-8") as handle:
        handle.write(svg)


def contact_sheet(objects, path, cell=150):
    sheet = Image.new("RGB", (COLUMNS * cell, ROWS * cell), (255, 255, 255))
    for index, obj in enumerate(objects):
        raster = td.rasterise(obj["loops"], cell - 10)
        pixels = np.full((cell - 10, cell - 10, 3), 255, dtype=np.uint8)
        pixels[raster > 0] = np.array(obj["ink"], dtype=np.uint8)
        sheet.paste(Image.fromarray(pixels),
                    ((index % COLUMNS) * cell + 5, (index // COLUMNS) * cell + 5))
    sheet.save(path)


# ---------------------------------------------------------------------- driver


def main():
    if not os.path.exists(NAMES):
        raise SystemExit("missing %s - name the objects first" % NAMES)

    with open(NAMES, encoding="utf-8") as handle:
        names = json.load(handle)

    catalogue = []
    for category, filename in SHEETS:
        if category not in names or len(names[category]) != COLUMNS * ROWS:
            raise SystemExit("object-names.json needs %d names for %s" %
                             (COLUMNS * ROWS, category))

        rgb = np.asarray(
            Image.open(os.path.join(SHEET_DIR, filename)).convert("RGB")).astype(np.int16)
        cells = slice_sheet(rgb)

        out_dir = os.path.join(SVG_DIR, category)
        os.makedirs(out_dir, exist_ok=True)

        traced = []
        for index, cell in enumerate(cells):
            number = index + 1
            label = "%s %02d" % (category, number)

            field = td.coverage(rgb, cell)
            loops = [loop for loop in td.contours(field)
                     if abs(td.area(loop)) >= td.MIN_CONTOUR_AREA]
            loops = [td.simplify(loop, td.SIMPLIFY_TOLERANCE) for loop in loops]
            td.check_winding(loops, label)
            loops = td.normalise(loops, *reversed(cell["mask"].shape))
            loops = td.quantise(loops)

            if not loops:
                raise SystemExit("%s traced to nothing" % label)

            name = names[category][index]
            traced.append({"loops": loops, "ink": cell["ink"], "name": name})

            write_svg(os.path.join(out_dir, "%02d-%s.svg" % (number, name)),
                      name, category, number, loops, cell["ink"])

            entry = {
                "name": name,
                "category": category,
                "sheet": filename,
                "index": number,
                "contours": len(loops),
                "points": sum(len(loop) for loop in loops),
                "path": td.path_data(loops),
            }

            # The two shape sheets are the SAME fifty forms drawn twice, once in
            # stroke and once filled, and an editor that knows that can offer
            # "the solid version of this" as one control instead of making
            # somebody hunt through a second list of fifty. That is only usable
            # if the pairing is data rather than a naming convention a reader has
            # to infer, so the base form is written out and the two variants are
            # joined on it. verify_objects.py checks the pairing actually holds.
            if category.startswith("shapes-"):
                entry["form"] = name.rsplit("-", 1)[0]
                entry["variant"] = category.split("-", 1)[1]

            catalogue.append(entry)

            print("%-14s %02d %-24s %3d contours %5d points" % (
                category, number, name, len(loops), sum(len(loop) for loop in loops)))

        contact_sheet(traced, os.path.join(HERE, "contact-%s.png" % category))

    seen = {}
    for entry in catalogue:
        if entry["name"] in seen:
            raise SystemExit("duplicate name %r in %s and %s"
                             % (entry["name"], seen[entry["name"]], entry["category"]))
        seen[entry["name"]] = entry["category"]

    document = {
        "schema": "wareborn.emblem-objects/1",
        "unit": td.FIXED,
        "viewBox": [-td.FIXED, -td.FIXED, 2 * td.FIXED, 2 * td.FIXED],
        "extent": td.DEVICE_EXTENT,
        "fillRule": "nonzero",
        "axis": "y-down",
        "origin": "centre",
        "objects": catalogue,
    }

    with open(OUT_JSON, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=1)
        handle.write("\n")

    total = sum(entry["points"] for entry in catalogue)
    print("\n%d objects, %d points total" % (len(catalogue), total))
    print("json    -> %s" % OUT_JSON)
    print("svg     -> %s" % SVG_DIR)


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    main()
