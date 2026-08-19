#!/usr/bin/env python3
"""
Turns the hand-drawn device sheet into vector paths.

WHY THIS EXISTS RATHER THAN A TRACING TOOL. potrace, autotrace and inkscape are
all absent from this machine, and the emblem renderer ships inside a
self-contained login server that must not grow a system dependency. So the trace
is written here, it runs at BUILD time, and only its OUTPUT is committed - the
SVGs under svg/ and the generated C# table. Nothing in this file is needed to run
the server.

WHY MARCHING SQUARES ON THE COVERAGE FIELD. The artwork is flat single-colour ink
on white with antialiased edges, which means the sheet already carries sub-pixel
edge information: a pixel that is 40% covered is 40% of the way from white to the
ink colour. Thresholding to a bitmap and tracing the staircase throws that away
and then needs curve fitting to put it back. Instead this reads the coverage as a
continuous scalar field and extracts the 50% isoline with linear interpolation
along each cell edge, which lands every vertex on the sub-pixel position the
artist's antialiasing implies. The result needs no smoothing pass - only
Douglas-Peucker to drop the vertices that sit on a straight run.

NON-ZERO, NOT EVEN-ODD. Marching squares walks every contour with the covered
side on the same hand, so an outer boundary and the boundary of a hole inside it
come out wound opposite ways - which is precisely what non-zero needs, and
check_winding below proves it holds for all fifty icons rather than assuming it.
Non-zero is worth that check because it is the rule that also lets a device be
built as OVERLAPPING subpaths: the saltire is two crossed bars and the cross is
two, and under even-odd their overlap would punch a hole through the middle of
each. One fill rule has to serve the traced artwork and the drawn-in-code
devices, and this is the one that can.

Dependencies: Pillow and numpy. Nothing else - the connected-component and
dilation passes below are written out rather than pulled from scipy so this runs
anywhere a Pillow install does.

Usage:  python3 tools/emblem-devices/trace_devices.py
"""

import json
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

SHEET = os.path.join(HERE, "device-sheet.png")
NAMES = os.path.join(HERE, "device-names.json")
SVG_DIR = os.path.join(HERE, "svg")
OUT_CS = os.path.join(ROOT, "WorldsAdriftServer", "Emblems", "EmblemDeviceGeometry.cs")
CONTACT = os.path.join(HERE, "contact-sheet.png")

COLUMNS = 10
ROWS = 5

# The isoline the trace follows: half covered is the edge of the ink.
THRESHOLD = 0.5

# How far a sample sitting exactly on the threshold is pushed off it. Big enough
# that the crossing it produces is still distinct from the grid vertex after the
# rounding that identifies shared points (see key), small enough to be four
# orders of magnitude below a pixel of coverage.
TIE_NUDGE = 1e-5

# A pixel lighter than this is paper. Generous, because the ink colours on the
# sheet run from a pale ochre to near-black and the only thing that has to be
# excluded here is the white surround.
PAPER = 230

# Bounding-box slack, in pixels, used when grouping the scattered strokes of one
# tribal icon into one device. Four is the largest value at which no two adjacent
# icons on this sheet merge, and it is comfortably larger than the widest gap
# inside any single icon.
CLUSTER_SLACK = 4

# Contours smaller than this, in source pixels squared, are antialiasing crumbs
# rather than drawn marks. Two pixels of area is well under the smallest real
# detail on the sheet (the dots in the compass rose are ~30).
MIN_CONTOUR_AREA = 2.0

# Douglas-Peucker tolerance in source pixels. The icons are ~140px across and the
# device is drawn ~130px across in a 256px emblem, so a vertex that moves by a
# fifth of a source pixel cannot move by a whole output pixel.
SIMPLIFY_TOLERANCE = 0.2

# Path coordinates are stored as integer thousandths of the [-1, 1] device box.
# One thousandth is a fifteenth of an output pixel at the size a device is drawn.
FIXED = 1000

# How much of the [-1, 1] device box the artwork fills. Below one so that a
# device's own bounding box never coincides with the box the painter scales into,
# which would put the outermost ink exactly on the boundary the rim test polices.
DEVICE_EXTENT = 0.98


# --------------------------------------------------------------- sheet slicing


def load_sheet():
    image = Image.open(SHEET).convert("RGB")
    return np.asarray(image).astype(np.int16)


def ink_mask(rgb):
    """Anything that is not paper."""
    return rgb.max(axis=2) < PAPER


def row_bands(mask):
    """The five printed rows, as (top, bottom) inclusive."""
    occupied = mask.any(axis=1)
    bands = []
    start = None
    for y, value in enumerate(occupied):
        if value and start is None:
            start = y
        elif not value and start is not None:
            bands.append((start, y - 1))
            start = None
    if start is not None:
        bands.append((start, len(occupied) - 1))

    if len(bands) != ROWS:
        raise SystemExit("expected %d printed rows, found %d" % (ROWS, len(bands)))
    return bands


def strip_labels(band, rgb, top):
    """
    Erases the printed numbers from a row band, in place.

    The numbers are LABELS, not artwork, and the whole job of this function is
    that none of their ink survives into a device. They are found as components
    rather than cut off by a row offset because there is no blank scanline under
    them: the ten icons in a row do not share a top edge, so by the y at which the
    last number ends the tallest icon has already started.

    A component is a number if it sits at the very top of the band, is small, and
    is neutral grey. All three are needed. Grey alone would eat the six icons
    drawn in grey; small-and-high alone would eat a pennant or a horn tip. The
    result is checked - ten groups, one per column - so a change to the sheet that
    breaks this fails loudly instead of quietly cropping an antler.
    """
    labels, count = components(band)
    height = band.shape[0]

    candidates = []
    for index in range(1, count + 1):
        ys, xs = np.nonzero(labels == index)
        y0, y1 = int(ys.min()), int(ys.max())
        x0, x1 = int(xs.min()), int(xs.max())

        if y0 > 4 or (y1 - y0) > 26 or (x1 - x0) > 30:
            continue

        colours = rgb[top + y0:top + y1 + 1, x0:x1 + 1]
        sub = (labels == index)[y0:y1 + 1, x0:x1 + 1]
        ink = colours[sub]
        # Neutral means every pixel's channels agree, not that the component's
        # overall range is narrow - antialiasing spreads a black glyph from 30 to
        # 250, so the test has to be per pixel.
        if int((ink.max(axis=1) - ink.min(axis=1)).max()) > 24:
            continue

        candidates.append((x0, x1, index))

    if not candidates:
        raise SystemExit("found no printed numbers in the band at y=%d" % top)

    candidates.sort()
    groups = 1
    for i in range(1, len(candidates)):
        if candidates[i][0] - candidates[i - 1][1] > 12:
            groups += 1

    if groups != COLUMNS:
        raise SystemExit(
            "the band at y=%d has %d number groups, expected %d" % (top, groups, COLUMNS))

    for _, _, index in candidates:
        band[labels == index] = False

    return height


def dilate(mask, radius):
    """
    Square-structuring-element dilation, separable, no scipy.

    Padded rather than rolled: a wrap-around shift would let the leftmost icon in
    a row touch the rightmost one and merge two devices into one.
    """
    out = np.pad(mask, radius, constant_values=False)
    for axis in (0, 1):
        grown = out.copy()
        for shift in range(1, radius + 1):
            grown |= np.roll(out, shift, axis=axis)
            grown |= np.roll(out, -shift, axis=axis)
        out = grown
    return out[radius:-radius, radius:-radius]


def components(mask):
    """
    8-connected components, returned as a label image and a count.

    Run-based union-find rather than a per-pixel flood fill: the sheet is 1.5
    million pixels and a Python flood fill of it is minutes, where this is
    sub-second.
    """
    height, width = mask.shape
    parent = [0]

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra == rb:
            return
        if ra < rb:
            parent[rb] = ra
        else:
            parent[ra] = rb

    labels = np.zeros((height, width), dtype=np.int32)
    next_label = 1

    for y in range(height):
        row = mask[y]
        if not row.any():
            continue
        x = 0
        while x < width:
            if not row[x]:
                x += 1
                continue
            start = x
            while x < width and row[x]:
                x += 1
            end = x - 1

            found = 0
            if y > 0:
                lo = max(start - 1, 0)
                hi = min(end + 1, width - 1)
                above = labels[y - 1, lo:hi + 1]
                for value in above:
                    if value == 0:
                        continue
                    if found == 0:
                        found = value
                    else:
                        union(found, value)
                        found = min(find(found), find(value))

            if found == 0:
                found = next_label
                parent.append(next_label)
                next_label += 1

            labels[y, start:end + 1] = found

    remap = {}
    for label in range(1, next_label):
        root = find(label)
        if root not in remap:
            remap[root] = len(remap) + 1

    lookup = np.zeros(next_label, dtype=np.int32)
    for label in range(1, next_label):
        lookup[label] = remap[find(label)]

    return lookup[labels], len(remap)


def slice_devices(rgb):
    """
    Cuts the sheet into 50 (bbox, mask, ink) devices in printed order.

    The printed numbers are LABELS and must not end up in the artwork; they are
    dropped by band rather than by colour, because several icons are drawn in the
    same grey the numbers are set in and a colour test would eat them.
    """
    mask = ink_mask(rgb)
    bands = row_bands(mask)

    devices = []
    for row, (top, bottom) in enumerate(bands):
        band = mask[top:bottom + 1].copy()
        strip_labels(band, rgb, top)
        art_top = top

        # Group the scattered strokes of one icon by dilating, labelling, then
        # throwing the dilation away - the pixels kept are the original ink.
        labels, count = components(dilate(band, CLUSTER_SLACK))
        if count != COLUMNS:
            raise SystemExit(
                "row %d clustered into %d devices, expected %d" % (row + 1, count, COLUMNS))

        found = []
        for index in range(1, count + 1):
            piece = (labels == index) & band
            ys, xs = np.nonzero(piece)
            found.append((int(xs.min()), int(xs.max()), int(ys.min()), int(ys.max()), piece))

        found.sort(key=lambda item: item[0])

        for x0, x1, y0, y1, piece in found:
            sub = piece[y0:y1 + 1, x0:x1 + 1]
            colours = rgb[art_top + y0:art_top + y1 + 1, x0:x1 + 1][sub]
            ink = tuple(int(v) for v in np.median(colours, axis=0))
            devices.append({
                "x": x0,
                "y": art_top + y0,
                "mask": sub,
                "ink": ink,
            })

    if len(devices) != COLUMNS * ROWS:
        raise SystemExit("sliced %d devices" % len(devices))
    return devices


def coverage(rgb, device):
    """
    The device's ink coverage as a continuous field in [0, 1].

    Coverage is read off the luminance ramp between paper and the icon's own ink
    colour, so the 50% isoline sits where the artist's antialiasing says the edge
    is rather than wherever a fixed grey threshold happens to fall for this hue.
    """
    mask = device["mask"]
    height, width = mask.shape
    patch = rgb[device["y"]:device["y"] + height, device["x"]:device["x"] + width]

    paper = 255.0
    ink = float(max(device["ink"]))
    span = max(paper - ink, 1.0)

    field = (paper - patch.max(axis=2).astype(np.float64)) / span
    np.clip(field, 0.0, 1.0, out=field)

    # A sample that sits EXACTLY on the threshold puts an isoline vertex exactly
    # on a grid vertex, where two different cell edges produce the same point and
    # the chaining below can no longer tell which segment leaves it. Nudging the
    # tie keeps every crossing strictly inside an edge, which is what makes the
    # "one segment out of every point" invariant hold. This is not cosmetic: it
    # cost nine dropped segments per icon and a torn contour before it was found.
    field[field == THRESHOLD] = THRESHOLD + TIE_NUDGE

    # Everything outside this device's own cluster belongs to a neighbour that
    # happens to share the bounding box. Zeroed, or a stray stroke from the icon
    # next door would be traced into this one.
    field[~mask] = 0.0
    return field


# ------------------------------------------------------------ marching squares


def contours(field, threshold=THRESHOLD):
    """
    The threshold isolines of a scalar field, as closed loops of (x, y).

    Coordinates are in the field's own pixel grid: a value sits at the centre of
    its pixel, so a vertex at x = 3.5 is halfway between pixel 3 and pixel 4.
    """
    padded = np.zeros((field.shape[0] + 2, field.shape[1] + 2), dtype=np.float64)
    padded[1:-1, 1:-1] = field

    height, width = padded.shape
    inside = padded >= threshold

    segments = {}

    def crossing(edge, i, j):
        if edge == "T":
            a, b = padded[i, j], padded[i, j + 1]
            return (j + interpolate(a, b), float(i))
        if edge == "B":
            a, b = padded[i + 1, j], padded[i + 1, j + 1]
            return (j + interpolate(a, b), float(i + 1))
        if edge == "L":
            a, b = padded[i, j], padded[i + 1, j]
            return (float(j), i + interpolate(a, b))
        a, b = padded[i, j + 1], padded[i + 1, j + 1]
        return (float(j + 1), i + interpolate(a, b))

    def interpolate(a, b):
        if a == b:
            return 0.5
        t = (threshold - a) / (b - a)
        return min(max(t, 0.0), 1.0)

    for i in range(height - 1):
        for j in range(width - 1):
            tl = inside[i, j]
            tr = inside[i, j + 1]
            br = inside[i + 1, j + 1]
            bl = inside[i + 1, j]

            case = (1 if tl else 0) | (2 if tr else 0) | (4 if br else 0) | (8 if bl else 0)
            if case == 0 or case == 15:
                continue

            if case == 5 or case == 10:
                middle = (padded[i, j] + padded[i, j + 1]
                          + padded[i + 1, j] + padded[i + 1, j + 1]) / 4.0
                joined = middle >= threshold
                if case == 5:
                    pairs = [("R", "T"), ("L", "B")] if joined else [("L", "T"), ("R", "B")]
                else:
                    pairs = [("T", "L"), ("B", "R")] if joined else [("T", "R"), ("B", "L")]
            else:
                pairs = [CASES[case]]

            for src, dst in pairs:
                a = crossing(src, i, j)
                b = crossing(dst, i, j)

                # Exactly one segment leaves any crossing point. If that is ever
                # false the field has a tie on the threshold and the loops below
                # would tear silently, so it is checked rather than assumed.
                if key(a) in segments:
                    raise SystemExit("two contour segments leave %r" % (key(a),))

                segments[key(a)] = (a, b)

    loops = []
    while segments:
        start_key = min(segments)
        a, b = segments.pop(start_key)
        loop = [a, b]

        while key(loop[-1]) != start_key:
            nxt = segments.pop(key(loop[-1]), None)
            if nxt is None:
                raise SystemExit("contour starting at %r does not close" % (start_key,))
            loop.append(nxt[1])

        if len(loop) >= 4:
            loops.append(loop[:-1])

    return loops


# from-edge to to-edge, oriented so the inside of the region is always on the
# same side of the walk. Only the unambiguous twelve cases; 5 and 10 are saddles
# and are resolved above from the cell's mean.
CASES = {
    1: ("L", "T"),
    2: ("T", "R"),
    3: ("L", "R"),
    4: ("R", "B"),
    6: ("T", "B"),
    7: ("L", "B"),
    8: ("B", "L"),
    9: ("B", "T"),
    11: ("B", "R"),
    12: ("R", "L"),
    13: ("R", "T"),
    14: ("T", "L"),
}


def key(point):
    return (round(point[0], 6), round(point[1], 6))


def check_winding(loops, label):
    """
    Every contour's winding must match its nesting depth.

    This is the invariant NON-ZERO filling rests on: an outer boundary and the
    boundary of a hole inside it have to run opposite ways round, or the hole
    fills in. Marching squares produces that for free - the walk always keeps the
    covered side on the same hand - but "for free" is exactly the kind of claim
    that is worth checking once per icon rather than believing, because the
    failure is a filled-in eye that nobody notices until the crest is live.
    """
    for index, loop in enumerate(loops):
        point = loop[0]
        depth = 0
        for other, ring in enumerate(loops):
            if other != index and inside_loop(ring, point):
                depth += 1

        outer = (depth % 2) == 0
        if outer != (area(loop) < 0):
            raise SystemExit(
                "%s: contour %d is wound the wrong way for depth %d" % (label, index, depth))


def inside_loop(loop, point):
    x, y = point
    hit = False
    for i in range(len(loop)):
        x0, y0 = loop[i - 1]
        x1, y1 = loop[i]
        if (y1 > y) != (y0 > y) and x < (x0 - x1) * (y - y1) / (y0 - y1) + x1:
            hit = not hit
    return hit


def area(loop):
    total = 0.0
    for i in range(len(loop)):
        x0, y0 = loop[i]
        x1, y1 = loop[(i + 1) % len(loop)]
        total += x0 * y1 - x1 * y0
    return total / 2.0


# ------------------------------------------------------------------- simplify


def simplify(loop, tolerance):
    """Douglas-Peucker over a closed loop, anchored on its two extreme points."""
    if len(loop) < 8:
        return loop

    start = 0
    far = max(range(len(loop)), key=lambda i: distance2(loop[start], loop[i]))

    first = _dp(loop[start:far + 1], tolerance)
    second = _dp(loop[far:] + [loop[start]], tolerance)

    out = first[:-1] + second[:-1]
    return out if len(out) >= 3 else loop


def _dp(points, tolerance):
    if len(points) < 3:
        return list(points)

    a, b = points[0], points[-1]
    worst, index = -1.0, 0
    for i in range(1, len(points) - 1):
        d = point_line_distance(points[i], a, b)
        if d > worst:
            worst, index = d, i

    if worst <= tolerance:
        return [a, b]

    return _dp(points[:index + 1], tolerance)[:-1] + _dp(points[index:], tolerance)


def point_line_distance(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    if dx == 0 and dy == 0:
        return math.hypot(p[0] - a[0], p[1] - a[1])
    t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / (dx * dx + dy * dy)
    t = min(max(t, 0.0), 1.0)
    return math.hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy))


def distance2(a, b):
    return (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2


# ------------------------------------------------------------------ normalise


def normalise(loops, width, height):
    """
    Maps the traced pixels into the painter's [-1, 1] device box.

    Uniform scale about the artwork's own centre, so the icon keeps its drawn
    proportions; the box is filled on its longer axis only.
    """
    xs = [p[0] for loop in loops for p in loop]
    ys = [p[1] for loop in loops for p in loop]

    minx, maxx = min(xs), max(xs)
    miny, maxy = min(ys), max(ys)

    span = max(maxx - minx, maxy - miny)
    if span <= 0:
        raise SystemExit("degenerate device")

    scale = 2.0 * DEVICE_EXTENT / span
    cx = (minx + maxx) / 2.0
    cy = (miny + maxy) / 2.0

    out = []
    for loop in loops:
        out.append([((p[0] - cx) * scale, (p[1] - cy) * scale) for p in loop])
    return out


def quantise(loops):
    """
    To integer thousandths, dropping vertices the rounding made coincident.

    Quantising here rather than at write time means the committed SVG and the
    committed C# table are the SAME numbers - the vector a player downloads is
    the vector the server rasterises, not a second rounding of it.
    """
    out = []
    for loop in loops:
        points = []
        for x, y in loop:
            q = (int(round(x * FIXED)), int(round(y * FIXED)))
            if not points or points[-1] != q:
                points.append(q)
        if len(points) > 1 and points[0] == points[-1]:
            points.pop()
        if len(points) >= 3:
            out.append(points)
    return out


# --------------------------------------------------------------------- output


def path_data(loops):
    """The loops as SVG path data, in the same thousandths the C# table uses."""
    parts = []
    for loop in loops:
        parts.append("M%d %d" % loop[0])
        for x, y in loop[1:]:
            parts.append("L%d %d" % (x, y))
        parts.append("Z")
    return " ".join(parts)


def write_svg(path, name, loops, ink):
    data = path_data(loops)
    colour = "#%02X%02X%02X" % ink
    svg = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<!-- Wareborn alliance emblem device: %s.\n'
        '     Traced from tools/emblem-devices/device-sheet.png by trace_devices.py.\n'
        '     Coordinates are thousandths of the painter\'s [-1, 1] device box. -->\n'
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-1000 -1000 2000 2000"\n'
        '     width="256" height="256">\n'
        '  <path fill="%s" d="%s"/>\n'
        '</svg>\n'
    ) % (name, colour, data)

    with open(path, "w", encoding="utf-8") as handle:
        handle.write(svg)


CS_HEADER = '''// <auto-generated>
//     Generated by tools/emblem-devices/trace_devices.py from
//     tools/emblem-devices/device-sheet.png. Do not edit by hand - edit the
//     sheet or the tracer and re-run it.
//
//     Coordinates are integer THOUSANDTHS of the painter's [-1, 1] device box,
//     with y pointing down, so they read the same way they render. Contours are
//     separated by '|' and filled NON-ZERO. The tracer walks every contour with
//     the ink on the same hand, so an outer boundary and a hole inside it are
//     wound opposite ways; it verifies that before writing this file.
// </auto-generated>

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// The traced artwork behind <see cref="EmblemVocabulary.Charge"/>'s drawn
    /// devices. Data only - <see cref="EmblemPath"/> turns a row of this table
    /// into something the painter can fill.
    /// </summary>
    internal static class EmblemDeviceGeometry
    {
        /// <summary>The scale the stored integers are in: 1000 = the box edge.</summary>
        internal const double Unit = %d.0;

        /// <summary>
        /// What each drawn device is called in the builder, in sheet order and
        /// aligned index-for-index with <see cref="Paths"/>. Named for what the
        /// icon looks like, which is the only naming rule this table has.
        /// </summary>
        internal static readonly IReadOnlyList<string> Names = new[]
        {
%s        };

        /// <summary>One entry per drawn device, in sheet order.</summary>
        internal static readonly IReadOnlyList<string> Paths = new[]
        {
'''

CS_FOOTER = '''        };
    }
}
'''


def write_csharp(devices):
    names = "".join('            "%s",\n' % d["name"].replace('"', '\\"') for d in devices)
    lines = [CS_HEADER % (FIXED, names)]
    for device in devices:
        contours_text = "|".join(
            " ".join("%d %d" % point for point in loop) for loop in device["loops"])
        lines.append('            // %02d %s - %d contours, %d points\n' % (
            device["number"], device["name"], len(device["loops"]),
            sum(len(loop) for loop in device["loops"])))
        lines.append('            "%s",\n' % contours_text)
    lines.append(CS_FOOTER)

    with open(OUT_CS, "w", encoding="utf-8") as handle:
        handle.write("".join(lines))


# ----------------------------------------------------------------- verification


def rasterise(loops, size, margin=0.06):
    """
    A quick scanline fill, used ONLY to build the contact sheet.

    Even-odd, not non-zero - crossing parity is three lines where a winding
    count is thirty, and for correctly wound contours the two agree. The server
    fills non-zero; this only has to be good enough to look at.

    The server has its own filler; this one exists so the trace can be looked at
    without a dotnet round trip, and so a bad trace is caught here rather than
    three steps later.
    """
    image = np.zeros((size, size), dtype=np.uint8)
    scale = size / (2.0 * (1.0 + margin))

    edges = []
    for loop in loops:
        for i in range(len(loop)):
            x0, y0 = loop[i]
            x1, y1 = loop[(i + 1) % len(loop)]
            edges.append((x0 / FIXED, y0 / FIXED, x1 / FIXED, y1 / FIXED))

    for py in range(size):
        y = ((py + 0.5) / scale) - (1.0 + margin)
        crossings = []
        for x0, y0, x1, y1 in edges:
            if (y0 > y) != (y1 > y):
                crossings.append(x0 + (y - y0) * (x1 - x0) / (y1 - y0))
        crossings.sort()

        for i in range(0, len(crossings) - 1, 2):
            a = int(round((crossings[i] + 1.0 + margin) * scale))
            b = int(round((crossings[i + 1] + 1.0 + margin) * scale))
            image[py, max(a, 0):max(b, 0)] = 255

    return image


def write_contact_sheet(devices, cell=150):
    sheet = Image.new("RGB", (COLUMNS * cell, ROWS * cell), (255, 255, 255))

    for index, device in enumerate(devices):
        raster = rasterise(device["loops"], cell - 10)
        tile = Image.new("RGB", (cell - 10, cell - 10), (255, 255, 255))
        pixels = np.asarray(tile).copy()
        ink = np.array(device["ink"], dtype=np.uint8)
        pixels[raster > 0] = ink
        tile = Image.fromarray(pixels)

        sheet.paste(tile, ((index % COLUMNS) * cell + 5, (index // COLUMNS) * cell + 5))

    sheet.save(CONTACT)


# ---------------------------------------------------------------------- driver


def main():
    if not os.path.exists(NAMES):
        raise SystemExit("missing %s - name the devices first" % NAMES)

    with open(NAMES, encoding="utf-8") as handle:
        names = json.load(handle)

    if len(names) != COLUMNS * ROWS:
        raise SystemExit("device-names.json has %d names, need %d" % (len(names), COLUMNS * ROWS))

    rgb = load_sheet()
    sliced = slice_devices(rgb)

    os.makedirs(SVG_DIR, exist_ok=True)

    devices = []
    for index, device in enumerate(sliced):
        field = coverage(rgb, device)
        loops = [loop for loop in contours(field) if abs(area(loop)) >= MIN_CONTOUR_AREA]
        loops = [simplify(loop, SIMPLIFY_TOLERANCE) for loop in loops]
        check_winding(loops, "device %02d" % (index + 1))
        loops = normalise(loops, *reversed(device["mask"].shape))
        loops = quantise(loops)

        number = index + 1
        name = names[index]
        devices.append({
            "number": number,
            "name": name,
            "loops": loops,
            "ink": device["ink"],
        })

        slug = name.lower().replace(" ", "-").replace("'", "")
        write_svg(os.path.join(SVG_DIR, "%02d-%s.svg" % (number, slug)), name, loops, device["ink"])

        print("%02d %-18s %3d contours %5d points" % (
            number, name, len(loops), sum(len(loop) for loop in loops)))

    write_csharp(devices)
    write_contact_sheet(devices)

    total = sum(sum(len(loop) for loop in d["loops"]) for d in devices)
    print("\n%d devices, %d points total" % (len(devices), total))
    print("svg      -> %s" % SVG_DIR)
    print("c#       -> %s" % OUT_CS)
    print("contact  -> %s" % CONTACT)


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    main()
