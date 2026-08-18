#!/usr/bin/env python3
"""Regenerate docs/admin-map-tier-palette.html.

Every figure on the page is computed here, not typed: WCAG relative luminance and
contrast, CIEDE2000, and Machado 2009 CVD simulation at severity 1.0. Every map on
the page is the REAL release-world geometry, re-rendered with the palette under
discussion.
"""
import sys

import colour as C
from palette import FINALISTS, build
from palette import ink
from render import map_svg

OUT = sys.argv[1]

OCEAN = "#09151d"
TIER_NAMES = ["T1 Wilderness", "T2 Expanse", "T3 Remnants", "T4 Badlands"]
TIER_TERRAIN = ["temperate", "highlands", "ice", "desert"]

SHIPPED = "deepwater"

OLD = ["#93c47d", "#6d9eeb", "#8e7cc3", "#f6b26b"]
OLD_SEEN = [C.composite(h, OCEAN, 0.38) for h in OLD]
CIVIDIS = ["#01295d", "#4d5361", "#848069", "#c4b34a"]

WALLS = [("Wind Rift", "#74c9cf"), ("Storm Rift", "#9b86d8"), ("Typhon", "#d48388"),
         ("Sand Storm", "#e8963c"), ("Ice Storm", "#a9d6ed"), ("World End", "#ec8f88")]

BLURB = {
    "deepwater": "Deep forest green, mid slate blue, deep violet, gold. The richest of the "
                 "four and the one that sits most comfortably on a dark console: the cells "
                 "read as land, the grey island markers and the weather walls stay legible "
                 "on top of them, and no tier shouts over the others.",
    "meridian": "The same four hues pitched brighter. More cheerful, more poster-like; the "
                "gold and the sky blue carry a lot of the picture, and the island markers "
                "have to work harder against them.",
    "nightfall": "Blue and violet swapped over: a deep navy Expanse and a pale lilac "
                 "Remnants. Handsome, but the navy is close to the ocean in lightness and "
                 "the lilac sits near the Storm Rift wall drawn across it.",
    "slate": "The muted option. Lower chroma throughout, so it recedes and lets the live "
             "overlay dominate. Calmest of the four; also the least characterful.",
}

VISIONS = ["normal", "protanopia", "deuteranopia", "tritanopia", "greyscale"]
VISION_LABEL = {
    "normal": "Normal", "protanopia": "Protanopia", "deuteranopia": "Deuteranopia",
    "tritanopia": "Tritanopia", "greyscale": "Greyscale",
}


def swatch_strip(fills, seen=None):
    """A row of four swatches, labelled with the exact hex that lands on the map."""
    seen = seen or fills
    cells = []
    for i, fill in enumerate(seen):
        k = ink(fill)
        cells.append(
            f'<div class="sw" style="background:{fill};color:{k}">'
            f'<span>{TIER_NAMES[i]}</span><code style="color:{k}">{fill}</code></div>')
    return '<div class="strip">' + "".join(cells) + "</div>"


def palette_table(fills):
    rows = []
    for i, fill in enumerate(fills):
        L, ch, hue = C.srgb_to_oklch(C.parse(fill))
        lab = C.srgb_to_lab(C.parse(fill))
        k = ink(fill)
        kc = C.contrast(k, fill)
        rows.append(
            f"<tr><td>{TIER_NAMES[i]}</td><td><code>{fill}</code></td>"
            f"<td class='n'>{L:.3f}</td><td class='n'>{ch:.3f}</td><td class='n'>{hue:.0f}&deg;</td>"
            f"<td class='n'>{lab[0]:.1f}</td>"
            f"<td><code>{k}</code></td><td class='n {'ok' if kc >= 4.5 else 'bad'}'>{kc:.2f}:1</td>"
            f"<td class='n'>{C.contrast(fill, OCEAN):.2f}:1</td></tr>")
    return ("<div class='wrap'><table><thead><tr><th>Tier</th><th>Fill</th><th>OKLCh L</th>"
            "<th>C</th><th>h</th><th>CIE L*</th><th>Label ink</th><th>Ink contrast</th>"
            "<th>vs ocean</th></tr></thead><tbody>"
            + "".join(rows) + "</tbody></table></div>")


def separation_table(named):
    """One row per palette, one column per vision: the closest pair's dE00."""
    head = "".join(f"<th>{VISION_LABEL[v]}</th>" for v in VISIONS)
    rows = []
    for label, fills, note in named:
        cells = []
        for v in VISIONS:
            d, pair = C.closest_pair(fills, v)
            if v == "greyscale":
                cls = ""
            elif d < 5:
                cls = "bad"
            elif d < 10:
                cls = "warn"
            else:
                cls = "ok"
            cells.append(f"<td class='n {cls}'>{d:.1f}<small> T{pair[0]}/T{pair[1]}</small></td>")
        rows.append(f"<tr><td>{label}</td>" + "".join(cells) + f"<td>{note}</td></tr>")
    return ("<div class='wrap'><table><thead><tr><th>Palette</th>" + head
            + "<th></th></tr></thead><tbody>" + "".join(rows) + "</tbody></table></div>")


def cvd_strip(fills):
    figs = []
    for v in VISIONS:
        sim = [C.simulate(f, v) for f in fills]
        d, pair = C.closest_pair(fills, v)
        cells = "".join(f'<div class="sw sw-sm" style="background:{s}"></div>' for s in sim)
        figs.append(
            f'<figure class="cvd"><div class="strip strip-sm">{cells}</div>'
            f'<figcaption>{VISION_LABEL[v]} &mdash; closest pair '
            f'&Delta;E00 <b>{d:.1f}</b> (T{pair[0]}/T{pair[1]})</figcaption></figure>')
    return '<div class="cvds">' + "".join(figs) + "</div>"


def wall_table(fills):
    rows = []
    for name, colour in WALLS:
        cells = []
        for fill in fills:
            d = C.de00(fill, colour)
            cls = "bad" if d < 5 else ("warn" if d < 10 else "ok")
            cells.append(f"<td class='n {cls}'>{d:.1f}</td>")
        rows.append(f"<tr><td>{name} <code>{colour}</code></td>" + "".join(cells) + "</tr>")
    head = "".join(f"<th>{n}</th>" for n in TIER_NAMES)
    return ("<div class='wrap'><table><thead><tr><th>Weather wall</th>" + head
            + "</tr></thead><tbody>" + "".join(rows) + "</tbody></table></div>")


def figure(fills, caption, alpha=1.0, width=1000):
    return (f"<figure>{map_svg(fills, width=width, alpha=alpha)}"
            f"<figcaption>{caption}</figcaption></figure>")


CSS = """
:root{color-scheme:dark}
*{box-sizing:border-box}
body{margin:0;padding:2rem 1.4rem 6rem;background:#080f15;color:#e7eef1;
 font:400 15px/1.65 ui-sans-serif,system-ui,-apple-system,sans-serif;max-width:82rem;margin-inline:auto}
h1{font-size:1.7rem;letter-spacing:-.02em;margin:0 0 .4rem}
h2{font-size:.7rem;font-weight:700;letter-spacing:.18em;text-transform:uppercase;
 color:#7ad0d6;margin:3.4rem 0 1rem;border-top:1px solid #22323c;padding-top:1.5rem}
h3{font-size:1.12rem;margin:2rem 0 .3rem;letter-spacing:-.01em}
h3 .hexes{font:600 .72rem ui-monospace,Consolas,monospace;color:#8aa0aa;margin-left:.6rem;
 letter-spacing:0;white-space:nowrap}
p{max-width:62rem;color:#c6d3d9}
.lede{font-size:1.02rem;color:#aebec6}
.verdict{border-left:3px solid #7ad0d6;padding:.1rem 0 .1rem 1rem;margin:1.4rem 0;color:#cfdde3}
.verdict em{color:#e7eef1}
code{font:600 .78rem ui-monospace,Consolas,monospace}
.strip{display:grid;grid-template-columns:repeat(4,1fr);gap:2px;border:1px solid #33474f;
 border-radius:7px;overflow:hidden;margin:.5rem 0 1rem}
.strip-sm{border-radius:5px;margin:0}
.sw{padding:.85rem .7rem;min-height:3.6rem;display:flex;flex-direction:column;
 justify-content:center;gap:.15rem}
.sw span{font-weight:700;font-size:.78rem}
.sw-sm{min-height:1.9rem;padding:0}
.cvds{display:grid;grid-template-columns:repeat(auto-fit,minmax(15rem,1fr));gap:1rem 1.2rem;margin:1rem 0}
.cvd figcaption{font-size:.72rem;margin-top:.35rem}
.maps{display:grid;grid-template-columns:repeat(auto-fit,minmax(25rem,1fr));gap:1.4rem;align-items:start}
figure{margin:0}
figcaption{font-size:.79rem;color:#8ea0a8;margin-top:.55rem;line-height:1.5}
figcaption b{color:#cfdde3}
svg{width:100%;height:auto;display:block;border:1px solid #22323c;border-radius:9px;background:#071017}
table{border-collapse:collapse;width:100%;margin:.5rem 0 1rem;font-size:.85rem}
th,td{text-align:left;padding:.42rem .6rem;border-bottom:1px solid #1a262e;white-space:nowrap}
th{font-size:.59rem;letter-spacing:.12em;text-transform:uppercase;color:#71838d}
td.n{text-align:right;font-variant-numeric:tabular-nums}
td small{color:#71838d;font-size:.72em;margin-left:.25rem}
.ok{color:#79d3a9}.warn{color:#e8c06a}.bad{color:#f18b8b;font-weight:700}
.wrap{overflow-x:auto;max-width:100%}
.pick{border:1px solid #2b3c46;border-radius:11px;padding:1.2rem 1.3rem 1.4rem;margin:1.6rem 0;
 background:linear-gradient(180deg,rgba(122,208,214,.05),transparent)}
.pick.shipped{border-color:#4d8f95;background:linear-gradient(180deg,rgba(122,208,214,.11),transparent)}
.tag{display:inline-block;font:700 .58rem/1 ui-sans-serif,sans-serif;letter-spacing:.14em;
 text-transform:uppercase;padding:.35rem .5rem;border-radius:4px;background:#7ad0d6;color:#071017;
 vertical-align:.18em;margin-left:.5rem}
.tag.alt{background:#2b3c46;color:#9fb3bc}
@media (max-width:60rem){.maps{grid-template-columns:1fr}}
"""


def main():
    finalists = {key: build(key)[1] for key in FINALISTS}
    shipped = finalists[SHIPPED]

    parts = ["<title>Admin world map &mdash; tier palette, take two</title>",
             f"<style>{CSS}</style>"]

    parts.append("<h1>Admin world map &mdash; tier palette, take two</h1>")
    parts.append(
        '<p class="lede">The last pass replaced the map\'s four colours with a single-axis '
        'sequential ramp (cividis). It measured beautifully and looked like a heatmap of '
        'nothing. This pass puts the <b>hues</b> back &mdash; green, blue, purple and, as '
        'asked, a proper <b>gold</b> for Badlands &mdash; and keeps only the parts of the last '
        'pass that were real wins: the legend swatch is literally the colour on the map, the '
        'label ink is computed rather than picked, and nobody is left unable to tell two tiers '
        'apart.</p>')
    parts.append(
        '<p class="verdict"><em>&ldquo;i liked the old colors i just felt like sand one should '
        'have been yellow, these new colors dont look nice at all. i like that you categorized '
        'but the colors them selves are shiote&rdquo;</em> &mdash; so: same categories, same '
        'hue identities, gold for the sand tier, and the whole set re-pitched so it looks '
        'designed rather than defaulted.</p>')
    parts.append(
        '<p>Every number below is computed, not judged by eye: WCAG&nbsp;2.x relative luminance '
        'and contrast ratio, CIEDE2000 colour difference, and Machado&nbsp;2009 '
        'colour-vision-deficiency simulation at severity&nbsp;1.0. Every map is the real '
        'release-world geometry &mdash; the same 20 tier cells, 266 island placements and 44 '
        'wall segments the console draws &mdash; re-rendered with the palette in question.</p>')

    # ---------------------------------------------------------------- where we are
    parts.append("<h2>Where we started</h2>")
    parts.append('<div class="maps">')
    parts.append(figure(
        OLD, "<b>The palette you liked.</b> Default Google-Sheets swatches "
        "<code>#93c47d #6d9eeb #8e7cc3 #f6b26b</code>, drawn at <code>opacity:.38</code> over "
        "the ocean &mdash; so what actually reached the screen was "
        "<code>" + " ".join(OLD_SEEN) + "</code>, and the legend keys showed the undimmed hex "
        "instead. Two problems, not one: the legend disagreed with the map, and under "
        "protanopia the green and the orange were the same colour "
        f"(&Delta;E00 {C.all_pairs(OLD_SEEN, 'protanopia')[(1, 4)]:.1f}).",
        alpha=0.38))
    parts.append(figure(
        CIVIDIS, "<b>What replaced it, and was rejected.</b> Cividis, drawn at full strength. "
        "Every measurement improved and the map stopped saying anything: four shades of the "
        "same idea, no tier with an identity of its own."))
    parts.append("</div>")
    parts.append(swatch_strip(OLD, OLD_SEEN))
    parts.append(swatch_strip(CIVIDIS))

    # ---------------------------------------------------------------- the reasoning
    parts.append("<h2>Why the four hues land where they do</h2>")
    parts.append(
        "<p>The hues are given: green Wilderness, blue Expanse, purple Remnants, gold Badlands. "
        "What is <i>chosen</i> is where each one sits in lightness, and that turns out to be "
        "forced rather than free.</p>")
    parts.append(
        "<p>Under protanopia and deuteranopia the four hues collapse into two families: green "
        "and gold both land on the yellow side of what is left, blue and purple both on the "
        "blue side. Two colours in the <i>same</i> family can then only be told apart by "
        "lightness. Gold has to be the light member of its family &mdash; a dark yellow is an "
        "olive, and the brief asks for gold &mdash; which forces green dark. Blue and purple "
        "take the same treatment, one light and one dark; which way round is the main "
        "difference between the options below.</p>")
    parts.append(
        "<p>That single constraint is what fixes the old palette's worst defect for free. The "
        "old green and orange sat at nearly the same lightness, which is exactly why "
        f"protanopia fused them at &Delta;E00 {C.all_pairs(OLD_SEEN, 'protanopia')[(1, 4)]:.1f}. "
        "Moving the sand tier to gold and pushing it up in lightness pulls that pair to "
        f"&Delta;E00 {C.all_pairs(shipped, 'protanopia')[(1, 4)]:.1f}.</p>")
    parts.append(
        "<p>Beyond that the target was deliberately modest: &ge;&nbsp;10 for the closest pair "
        "under every deficiency, which is &ldquo;obviously a different colour at a glance&rdquo;, "
        "and no further. Chasing 18+ is what produced the ramp. Every cell also prints "
        "<code>T&lt;n&gt; &middot; Name</code>, so colour was never the only channel carrying "
        "the tier.</p>")

    # ---------------------------------------------------------------- candidates
    parts.append("<h2>The four candidates, on the real map</h2>")
    parts.append(
        "<p>All four use the same hues and clear the same floor. Pick by eye; if you want a "
        "different one from the one shipped, it is a four-line change.</p>")

    order = [SHIPPED] + [k for k in FINALISTS if k != SHIPPED]
    for key in order:
        label = FINALISTS[key][0]
        fills = finalists[key]
        tag = ('<span class="tag">Shipped</span>' if key == SHIPPED
               else '<span class="tag alt">Alternate</span>')
        parts.append(f'<div class="pick{" shipped" if key == SHIPPED else ""}">')
        parts.append(f'<h3>{label}{tag}<span class="hexes">'
                     + "  ".join(fills) + "</span></h3>")
        parts.append(f"<p>{BLURB[key]}</p>")
        parts.append(swatch_strip(fills))
        parts.append(figure(fills, f"<b>{label}</b> on the release world.", width=1400))
        parts.append(palette_table(fills))
        parts.append("</div>")

    # ---------------------------------------------------------------- measurements
    parts.append("<h2>Separation, measured</h2>")
    parts.append(
        "<p>The closest pair of tiers under each vision model, in CIEDE2000. Roughly: 1 is a "
        "just-noticeable difference, 2&ndash;3 is a difference a careful eye finds, 10+ is "
        "obviously a different colour. Anything under 5 is a collapse. Greyscale is reported "
        "but not treated as a floor &mdash; this is a categorical palette on a colour display, "
        "and the tier is printed on every cell.</p>")
    named = [("Old Sheets defaults, as seen on screen", OLD_SEEN,
              "protanopia fused Wilderness and Badlands"),
             ("Cividis ramp (rejected)", CIVIDIS, "measured well, looked like nothing")]
    for key in order:
        note = "shipped" if key == SHIPPED else "alternate"
        named.append((FINALISTS[key][0], finalists[key], note))
    parts.append(separation_table(named))

    parts.append("<h3>The shipped palette under each vision model</h3>")
    parts.append(cvd_strip(shipped))

    parts.append("<h2>Label ink</h2>")
    parts.append(
        "<p>The ink on each cell is not picked, it is computed: whichever of the console's "
        "light ink <code>#edf3f5</code> and dark ink <code>#0a1219</code> has the greater WCAG "
        "contrast against that fill wins, and a unit test refuses any palette where a label "
        "falls below AA for normal text (4.5:1). On the shipped palette that puts light ink on "
        "the green and the violet, dark ink on the blue and the gold, at "
        + ", ".join(f"{C.contrast(ink(f), f):.2f}:1" for f in shipped) + ".</p>")

    parts.append("<h2>Clashes with the rest of the map</h2>")
    parts.append(
        "<p>A tier fill also has to survive everything drawn <i>over</i> it. The weather walls "
        "are stroked straight across the cells, so a wall that matches the fill under it "
        "disappears. The last pass moved the Sand Storm wall from <code>#d9b36b</code> to "
        "<code>#e8963c</code> because it sat &Delta;E00 8.5 from the old tan Badlands swatch in "
        "the same legend; with Badlands now gold that move still holds, at "
        f"&Delta;E00 {C.de00(shipped[3], '#e8963c'):.1f}, so no wall colour needed changing "
        "this time. Figures below are for the shipped palette.</p>")
    parts.append(wall_table(shipped))
    parts.append(
        "<p>Against the ocean <code>#09151d</code> the four fills sit at &Delta;E00 "
        + ", ".join(f"{C.de00(f, OCEAN):.1f}" for f in shipped)
        + " &mdash; every cell reads as land rather than as more water, including the deep "
        "green, whose luminance is close to the ocean's but whose hue is not.</p>")

    parts.append("<h2>What is pinned in code</h2>")
    parts.append(
        "<p>The palette and the WCAG maths live in "
        "<code>WorldsAdriftServer/Admin/MapTierPalette.cs</code>, which emits the CSS for the "
        "drawn cell, the cell label and the legend swatch from one list &mdash; so the legend "
        "cannot go back to disagreeing with the map. The CIEDE2000 and CVD simulation live "
        "next to it in <code>MapColourMetrics.cs</code>, so the separation claims on this page "
        "are asserted by unit tests rather than by this page. A palette change that drops a "
        "pair below &Delta;E00 10 under any deficiency, or puts a label below AA, fails the "
        "build.</p>")

    open(OUT, "w", encoding="utf-8").write("\n".join(parts) + "\n")
    print("wrote", OUT)
    for key in order:
        print(f"  {FINALISTS[key][0]:16s}", finalists[key])


if __name__ == "__main__":
    main()
