"""Render the real map geometry with an arbitrary tier palette."""
import json
import os

import colour as C
from palette import DARK_INK, LIGHT_INK, ink

GEO = json.load(open(os.path.join(os.path.dirname(__file__), "geometry.json")))

# The per-cell seeded roll-up the console now prints on every cell, keyed by the
# cell's index in geometry.json, so the artefact's maps carry the same third
# label line the real map does. That is the point: a palette has to stay legible
# under the text that is actually on it, not under less text.
#
# Regenerating it (it changes only when the island catalogue does): group
# IslandResourceInventoryCatalog.All by CellId and sum Databanks / Deposits /
# Trees plus a count of OresAreInferred, then map each CellId onto a geometry.json
# index by the cell's district label. The two null-district cells are named
# "unassigned-t<tier>-<n>" by rank in (z, x) ascending, exactly as
# ReleaseWorldMap does it; geometry.json stores label y = -z, so that is
# descending label y, then ascending label x.
STOCK = json.load(open(os.path.join(os.path.dirname(__file__), "cell-stock.json")))


def stock_text(index):
    s = STOCK.get(str(index))
    if not s:
        return ""
    out = (f"{s['islands']} isl · {s['databanks']} db · "
           f"{s['deposits']} dep · {s['trees']} tr")
    if s["inferred"]:
        out += f" · ✱{s['inferred']}"
    return out

# The shipped weather-wall colours, by the name the geometry carries. Only four
# of the six have segments in the release MapFile. Storm Rift moved off #9b86d8
# when Remnants became lilac; keeping the old value here would draw a map that
# looks fine and is not the one the console ships.
WALL_COLOURS = {
    "Wind Rift": "#74c9cf",
    "Storm Rift": "#c04ae8",
    "Typhon": "#d48388",
    "Sand Storm": "#e8963c",
    "Ice Storm": "#a9d6ed",
    "World End": "#ec8f88",
}


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def seen_fills(fills, alpha):
    """What the eye receives: the authored hues composited onto the ocean."""
    if alpha >= 1:
        return list(fills)
    return [C.composite(f, GEO["ocean"], alpha) for f in fills]


def map_svg(fills, width=1100, alpha=1.0, standalone=False, labels=True):
    """One <svg> of the whole release world, tiers painted with `fills`.

    `alpha` draws the cells translucent, exactly as the console does. Anything
    chosen FROM the cell colour - the label ink, the dashed stroke on the two
    unassigned cells - is chosen from the composited result, never from the
    authored hue, because that is the colour those marks actually sit on.
    """
    seen = seen_fills(fills, alpha)
    vb = GEO["viewBox"]
    head = ('<svg xmlns="http://www.w3.org/2000/svg" '
            f'viewBox="{vb[0]} {vb[1]} {vb[2]} {vb[3]}" width="{width}" height="{width}">')
    out = [head]
    out.append('<defs><symbol id="isl" viewBox="-90 -90 180 180">'
               '<path d="M0 -70 62 -22 48 54 -8 72 -67 30 -55 -38Z"></path></symbol>'
               '<symbol id="hav" viewBox="-110 -110 220 220"><circle r="80"></circle>'
               '<path d="M0 -58 51 -18 39 44 -6 59 -55 25 -45 -31Z" fill="#d6fff0"></path>'
               '</symbol></defs>')
    out.append(f'<rect x="{vb[0]}" y="{vb[1]}" width="{vb[2]}" height="{vb[3]}" '
               f'fill="{GEO["ocean"]}"/>')
    hr = GEO["havenRect"]
    out.append(f'<rect x="{hr[0]}" y="{hr[1]}" width="{hr[2]}" height="{hr[3]}" '
               'fill="#17322f" opacity=".72"/>')
    hx = GEO["havenLabelX"]
    out.append(f'<text x="{hx}" y="0" fill="#8dc8b1" font-size="300" font-weight="700" '
               f'text-anchor="middle" letter-spacing="30" transform="rotate(-90 {hx} 0)" '
               'font-family="sans-serif">HAVEN CORRIDOR</text>')

    for cell in GEO["cells"]:
        fill = fills[cell["tier"] - 1]
        stroke = ink(seen[cell["tier"] - 1]) if cell["unassigned"] else "#233a45"
        dash = ' stroke-dasharray="600 400"' if cell["unassigned"] else ""
        # fill-opacity, not opacity: the console dims the fill and leaves the
        # cell stroke alone, and a page that dimmed both would not be the map.
        op = f' fill-opacity="{alpha}"' if alpha < 1 else ""
        out.append(f'<path d="{cell["d"]}" fill="{fill}"{op} stroke="{stroke}"{dash} '
                   'stroke-width="100"/>')

    for p in range(-18000, 18001, 6000):
        out.append(f'<line x1="{p}" y1="-18000" x2="{p}" y2="18000" stroke="#39515d" '
                   'stroke-width="33" opacity=".35"/>')
        out.append(f'<line x1="-18000" y1="{p}" x2="18000" y2="{p}" stroke="#39515d" '
                   'stroke-width="33" opacity=".35"/>')

    for wall in GEO["walls"]:
        out.append(f'<path d="{wall["d"]}" fill="none" stroke="#071017" stroke-width="165" '
                   'opacity=".8" stroke-linecap="round"/>')
        colour = WALL_COLOURS.get(wall["name"], wall["stroke"])
        out.append(f'<path d="{wall["d"]}" fill="none" stroke="{colour}" '
                   f'stroke-width="{wall["width"]}" opacity=".98" stroke-linecap="round"/>')

    for isl in GEO["islands"]:
        sym = "hav" if isl["haven"] else "isl"
        col = "#71d0a5" if isl["haven"] else "#80939c"
        edge = "#d6fff0" if isl["haven"] else "#c0cbd0"
        out.append(f'<use href="#{sym}" x="{isl["x"]}" y="{isl["y"]}" width="180" height="180" '
                   f'fill="{col}" stroke="{edge}" stroke-width="33"/>')

    if labels:
        for index, cell in enumerate(GEO["cells"]):
            lab = cell["label"]
            k = ink(seen[cell["tier"] - 1])
            halo = DARK_INK if k == LIGHT_INK else LIGHT_INK
            top_size = 270 if cell["unassigned"] else 330
            stock = stock_text(index)
            out.append(
                f'<text x="{lab["x"]}" y="{lab["y"]}" text-anchor="middle" '
                'font-family="sans-serif" font-weight="700" paint-order="stroke" '
                f'stroke="{halo}" stroke-width="55" stroke-linejoin="round">'
                f'<tspan x="{lab["x"]}" dy="0" fill="{k}" font-size="{top_size}" '
                f'letter-spacing="33">{esc(lab["district"])}</tspan>'
                f'<tspan x="{lab["x"]}" dy="300" fill="{k}" font-size="210" '
                f'letter-spacing="11">{esc(lab["tierText"])}</tspan>'
                + (f'<tspan x="{lab["x"]}" dy="250" fill="{k}" font-size="165" '
                   f'letter-spacing="3">{esc(stock)}</tspan>' if stock else "")
                + '</text>')

    out.append("</svg>")
    svg = "\n".join(out)
    if standalone:
        svg = '<?xml version="1.0" encoding="UTF-8"?>\n' + svg
    return svg


if __name__ == "__main__":
    import sys
    from palette import FINALISTS, build

    variants = {
        "old": ("Old Sheets defaults @ .38", ["#93c47d", "#6d9eeb", "#8e7cc3", "#f6b26b"], 0.38),
        "cividis": ("Cividis (rejected)", ["#01295d", "#4d5361", "#848069", "#c4b34a"], 1.0),
    }
    for key in FINALISTS:
        label, fills = build(key)
        variants[key] = (label, fills, 1.0)
    for key, (label, fills, alpha) in variants.items():
        open(f"out-{key}.svg", "w").write(
            map_svg(fills, width=1100, alpha=alpha, standalone=True))
        print(key, label, fills)
