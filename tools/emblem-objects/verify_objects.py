#!/usr/bin/env python3
"""
Checks emblem-objects.json against the sheets it was traced from, and looks at it.

THIS IS THE GATE, and it exists because the failure mode here is silent. A path
that traced to nothing, or to a lump, does not throw: it renders as a blank or
wrong LAYER on somebody's crest, which nobody notices until the crest is live.
So three things are checked, in order of how quietly they fail:

  1. Every path PARSES, from the committed JSON, with a parser written here and
     not shared with the writer. If the only code that can read the format is the
     code that wrote it, the format is not machine-readable, it is a private
     encoding - and the whole point of shipping JSON is that someone else wires
     it in.
  2. Every path has a NON-DEGENERATE bounding box, at least one contour and at
     least three points per contour. This is the "silently empty path" check.
  3. Every path still LOOKS LIKE its source icon, measured as the intersection
     over union between the traced fill and the source ink, both normalised into
     the same box. This is the one that catches a trace that is valid, non-empty,
     and wrong.

Check 3 is a MEASUREMENT, not a pass mark. An icon drawn in a hairline stroke
loses a larger fraction of its area to a half-pixel of edge error than a solid
one does, so a thin icon scores lower than a fat icon at identical quality. The
score is here to RANK, so that the worst are looked at first, and the sheets it
writes are the thing that actually decides.

Usage:  python3 tools/emblem-objects/verify_objects.py
"""

import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

sys.path.insert(0, os.path.join(ROOT, "tools", "emblem-devices"))
sys.path.insert(0, HERE)
import trace_devices as td  # noqa: E402
import trace_objects as to  # noqa: E402

CATALOGUE = os.path.join(HERE, "emblem-objects.json")

# Side of the square the comparison is rasterised at. Well above the ~130px a
# device is drawn at in a 256px emblem, so the score measures the trace rather
# than the rasteriser.
RASTER = 192

# The size the legibility pass renders at: roughly the pixels a device gets when
# a crest is shown in a roster list rather than on its own.
LEGIBLE = 40

# Below this the trace and the source disagree enough to be worth an eye. Chosen
# from the distribution, not in advance: it is roughly where the ranked list
# stops being hairline-stroke icons and starts being icons worth arguing about.
MARGINAL = 0.80


def parse_path(data):
    """
    'M x y L x y ... Z' back into loops of integer points.

    Deliberately strict. Anything the writer emits that this does not understand
    is a bug in the format, not something to skip past.
    """
    loops = []
    current = None
    tokens = data.split()
    i = 0
    while i < len(tokens):
        token = tokens[i]
        if token[0] in "ML":
            x = int(token[1:])
            y = int(tokens[i + 1])
            if token[0] == "M":
                if current is not None:
                    raise ValueError("M inside an unclosed contour")
                current = [(x, y)]
            else:
                if current is None:
                    raise ValueError("L outside a contour")
                current.append((x, y))
            i += 2
        elif token == "Z":
            if current is None:
                raise ValueError("Z outside a contour")
            loops.append(current)
            current = None
            i += 1
        else:
            raise ValueError("unexpected token %r" % token)
    if current is not None:
        raise ValueError("path does not close its last contour")
    return loops


# The three shape indices where the two sheets are NOT a stroke and a fill of one
# form. They are still the same form and they still pair, so they keep the paired
# names - but an editor offering "outline or solid" is offering a choice that
# does nothing on these three, and that is worth knowing before it ships.
#
# Declared rather than derived, so a FOURTH one appearing - because a sheet was
# redrawn - is a failure here instead of a shrug.
NOT_FILLED = {
    11: "dashed-ring: shapes-full.png draws it dashed too, so there is no fill",
    38: "diamond-ring: shapes-full.png draws the same diamonds and arcs, unfilled",
}
NOT_STROKED = {
    24: "vesica-leaf: shapes-empty.png draws it already filled, so there is no stroke",
}

# A form's stroked variant should sit on its filled variant's RIM. Below this it
# is somewhere else, which means the two sheets do not agree about what form
# index N is - the thing the maintainer asked to have checked rather than
# assumed.
ON_RIM = 0.70

# ... unless the stroked variant is drawn INSIDE the filled one rather than
# around it, which is what a corner-bracket square or an interior detail line
# does. Still the same form; only the rim test is the wrong question for it.
IN_BODY = 0.88

# How far a mask is eaten in from its edges to ask "is this filled or is it a
# stroke?". One and a half times the width the outline sheet is stroked at, at
# RASTER, so a stroke is eaten away completely and a fill is not.
FILL_PROBE = 12

# What has to survive that. The separation is categorical rather than marginal:
# on these four sheets the two unfilled "solid" entries leave EXACTLY nothing,
# the thinnest genuinely filled one (the gear, which is mostly bore and teeth)
# leaves 0.013, and the one already-filled "outline" entry leaves 0.086 against
# 0.004 for the next thickest stroke.
IS_FILLED = 0.005
IS_STROKE = 0.02


def normalised_mask(field, size, margin=0.06):
    """Resamples a coverage field into the [-1, 1] box, matching normalise()."""
    solid = field >= td.THRESHOLD
    ys, xs = np.nonzero(solid)
    if len(ys) == 0:
        return np.zeros((size, size), dtype=bool)

    # normalise() works off the CONTOUR bbox, which sits half a pixel outside the
    # pixel bbox on every side because a contour runs along pixel edges, not
    # centres. Matching that here is what keeps the two rasters registered.
    minx, maxx = xs.min() - 0.5, xs.max() + 0.5
    miny, maxy = ys.min() - 0.5, ys.max() + 0.5
    span = max(maxx - minx, maxy - miny)
    scale = 2.0 * td.DEVICE_EXTENT / span
    cx, cy = (minx + maxx) / 2.0, (miny + maxy) / 2.0

    out = np.zeros((size, size), dtype=bool)
    step = (2.0 * (1.0 + margin)) / size
    for py in range(size):
        wy = (py + 0.5) * step - (1.0 + margin)
        sy = int(round(wy / scale + cy))
        if sy < 0 or sy >= field.shape[0]:
            continue
        for px in range(size):
            wx = (px + 0.5) * step - (1.0 + margin)
            sx = int(round(wx / scale + cx))
            if 0 <= sx < field.shape[1]:
                out[py, px] = solid[sy, sx]
    return out


def erode(mask, radius):
    return ~td.dilate(~mask, radius)


def check_pairing(by_category):
    """
    Holds the claim that the two shape sheets are one set of fifty forms.

    Measured by asking where the stroked variant's ink LANDS on the filled one:
    on its rim for a normal outline, anywhere in its body for a form whose stroke
    is drawn inside rather than around it. Silhouette overlap was tried first and
    is useless here, because a dashed outline has no enclosed interior at all -
    a flood fill walks straight out through the gaps - so a dashed hexagon and a
    dashed octagon score identically. Landing position does not care about gaps.
    """
    problems = []
    outline = {e["index"]: e for e in by_category["shapes-outline"]}
    solid = {e["index"]: e for e in by_category["shapes-solid"]}

    if set(outline) != set(solid):
        return ["the two shape sheets do not cover the same indices"]

    for index in sorted(outline):
        if outline[index].get("form") != solid[index].get("form"):
            problems.append("shape %02d: the two variants disagree about the form name"
                            % index)
            continue

        stroked = td.rasterise(parse_path(outline[index]["path"]), RASTER) > 0
        filled = td.rasterise(parse_path(solid[index]["path"]), RASTER) > 0

        rim = td.dilate(filled & ~erode(filled, 7), 7)
        body = td.dilate(filled, 7)
        total = max(np.count_nonzero(stroked), 1)

        on_rim = np.count_nonzero(stroked & rim) / total
        in_body = np.count_nonzero(stroked & body) / total

        if on_rim < ON_RIM and in_body < IN_BODY:
            problems.append(
                "shape %02d %s: the stroked variant does not lie on the filled one "
                "(rim %.2f, body %.2f) - the sheets may not agree about this form"
                % (index, outline[index]["form"], on_rim, in_body))

        # And that the names mean what they say: the solid one is filled, the
        # outline one is not. A name that lies is worse than a missing name,
        # because an editor builds an "outline or solid" control out of it.
        form = outline[index]["form"]
        fill_of_solid = survives(filled)
        fill_of_outline = survives(stroked)

        if (fill_of_solid < IS_FILLED) != (index in NOT_FILLED):
            problems.append(
                "shape %02d %s: -solid is %sfilled (%.4f survives erosion) but "
                "NOT_FILLED says otherwise"
                % (index, form, "not " if fill_of_solid < IS_FILLED else "", fill_of_solid))

        if (fill_of_outline > IS_STROKE) != (index in NOT_STROKED):
            problems.append(
                "shape %02d %s: -outline is %sa stroke (%.4f survives erosion) but "
                "NOT_STROKED says otherwise"
                % (index, form, "not " if fill_of_outline > IS_STROKE else "",
                   fill_of_outline))

    return problems


def survives(mask):
    """The fraction of a mask left after eating FILL_PROBE in from every edge."""
    return np.count_nonzero(erode(mask, FILL_PROBE)) / max(np.count_nonzero(mask), 1)


def tint(mask, ink):
    pixels = np.full(mask.shape + (3,), 255, dtype=np.uint8)
    pixels[mask] = np.array(ink, dtype=np.uint8)
    return pixels


def main():
    with open(CATALOGUE, encoding="utf-8") as handle:
        document = json.load(handle)

    if document["schema"] != "wareborn.emblem-objects/1":
        raise SystemExit("unexpected schema %r" % document["schema"])

    entries = document["objects"]
    by_category = {}
    for entry in entries:
        by_category.setdefault(entry["category"], []).append(entry)

    failures = []
    scores = []

    for category, filename in to.SHEETS:
        rgb = np.asarray(Image.open(os.path.join(to.SHEET_DIR, filename))
                         .convert("RGB")).astype(np.int16)
        cells = to.slice_sheet(rgb)
        rows = sorted(by_category[category], key=lambda e: e["index"])
        if len(rows) != len(cells):
            raise SystemExit("%s has %d entries for %d cells" % (category, len(rows), len(cells)))

        sheet = Image.new("RGB", (to.COLUMNS * 2 * 100, to.ROWS * 100), (255, 255, 255))
        legible = Image.new("RGB", (to.COLUMNS * LEGIBLE * 3, to.ROWS * LEGIBLE * 3),
                            (255, 255, 255))

        for position, (entry, cell) in enumerate(zip(rows, cells)):
            label = "%s/%s" % (category, entry["name"])

            loops = parse_path(entry["path"])
            if not loops:
                failures.append("%s: path has no contours" % label)
                continue
            if any(len(loop) < 3 for loop in loops):
                failures.append("%s: a contour has fewer than 3 points" % label)
            if len(loops) != entry["contours"]:
                failures.append("%s: says %d contours, path has %d"
                                % (label, entry["contours"], len(loops)))
            if sum(len(loop) for loop in loops) != entry["points"]:
                failures.append("%s: point count disagrees with the path" % label)

            xs = [p[0] for loop in loops for p in loop]
            ys = [p[1] for loop in loops for p in loop]
            width, height = max(xs) - min(xs), max(ys) - min(ys)
            if width <= 0 or height <= 0:
                failures.append("%s: degenerate bounding box %dx%d" % (label, width, height))
                continue
            if max(width, height) < int(1.5 * td.DEVICE_EXTENT * td.FIXED):
                failures.append("%s: bounding box %dx%d does not fill the box"
                                % (label, width, height))
            if max(abs(v) for v in xs + ys) > td.FIXED:
                failures.append("%s: a point escapes the [-1, 1] box" % label)

            traced = td.rasterise(loops, RASTER) > 0
            source = normalised_mask(td.coverage(rgb, cell), RASTER)

            union = np.count_nonzero(traced | source)
            iou = np.count_nonzero(traced & source) / union if union else 0.0
            scores.append((iou, category, entry["index"], entry["name"]))

            pair = Image.new("RGB", (200, 100), (255, 255, 255))
            pair.paste(Image.fromarray(tint(source, cell["ink"])).resize((96, 96)), (2, 2))
            pair.paste(Image.fromarray(tint(traced, cell["ink"])).resize((96, 96)), (102, 2))
            sheet.paste(pair, ((position % to.COLUMNS) * 200, (position // to.COLUMNS) * 100))

            # The legibility pass. A device is drawn about 130px across in a
            # 256px emblem, but a crest in a list is shown far smaller than that,
            # and THIS is where these four sheets differ from the original fifty:
            # they are drawn in a broken-stroke style whose gaps are a couple of
            # source pixels wide. Trace fidelity does not save an icon whose
            # dashes close up into a blob at the size it is actually seen, so it
            # is rendered at that size and looked at, blown back up with nearest
            # neighbour so what is lost is visible rather than resampled away.
            small = td.rasterise(loops, LEGIBLE) > 0
            legible.paste(
                Image.fromarray(tint(small, cell["ink"])).resize(
                    (LEGIBLE * 3, LEGIBLE * 3), Image.NEAREST),
                ((position % to.COLUMNS) * LEGIBLE * 3, (position // to.COLUMNS) * LEGIBLE * 3))

        sheet.save(os.path.join(HERE, "compare-%s.png" % category))
        legible.save(os.path.join(HERE, "legibility-%s.png" % category))

    failures.extend(check_pairing(by_category))

    scores.sort()
    print("worst 30 by intersection-over-union with the source ink:")
    for iou, category, index, name in scores[:30]:
        print("  %.3f  %-15s %02d  %s" % (iou, category, index, name))

    below = [s for s in scores if s[0] < MARGINAL]
    print("\n%d objects, %d below %.2f, median %.3f"
          % (len(scores), len(below), MARGINAL,
             sorted(s[0] for s in scores)[len(scores) // 2]))

    if failures:
        print("\n%d FAILURES:" % len(failures))
        for line in failures:
            print("  " + line)
        raise SystemExit(1)

    print("\nall %d paths parse, are non-empty and fill the device box" % len(entries))


if __name__ == "__main__":
    main()
