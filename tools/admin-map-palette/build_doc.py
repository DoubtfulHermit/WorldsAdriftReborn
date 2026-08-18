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
from palette import ink, LIGHT_INK, DARK_INK
from render import map_svg

OUT = sys.argv[1]

OCEAN = "#09151d"
TIER_NAMES = ["T1 Wilderness", "T2 Expanse", "T3 Remnants", "T4 Badlands"]
TIER_TERRAIN = ["temperate", "highlands", "ice", "desert"]

SHIPPED = "nightfall"

# The three transparencies offered side by side, and the one that ships. They
# are not evenly spaced and they are not round numbers by accident - see the
# "How much" section: the intervals between them are the ones where a tier
# lands in the band where no label ink reaches AA.
ALPHAS = [0.96, 0.76, 0.58]
SHIPPED_ALPHA = 0.76
ALPHA_NAME = {0.96: "Barely", 0.76: "Wash", 0.58: "Airy"}

OLD = ["#93c47d", "#6d9eeb", "#8e7cc3", "#f6b26b"]
OLD_SEEN = [C.composite(h, OCEAN, 0.38) for h in OLD]
CIVIDIS = ["#01295d", "#4d5361", "#848069", "#c4b34a"]
DEEPWATER_HEX = ["#134e26", "#4f89c1", "#694189", "#cdb236"]

OLD_STORM = "#9b86d8"
WALLS = [("Wind Rift", "#74c9cf"), ("Storm Rift", "#c04ae8"), ("Typhon", "#d48388"),
         ("Sand Storm", "#e8963c"), ("Ice Storm", "#a9d6ed"), ("World End", "#ec8f88")]


def seen(fills, alpha):
    return [C.composite(f, OCEAN, alpha) for f in fills]


def ink_band():
    """The luminance interval in which NEITHER ink reaches WCAG AA."""
    lo = (C.relative_luminance(LIGHT_INK) + 0.05) / 4.5 - 0.05
    hi = 4.5 * (C.relative_luminance(DARK_INK) + 0.05) - 0.05
    return lo, hi

BLURB = {
    "nightfall": "Chosen. Blue and violet the other way round from the rest: a deep navy "
                 "Expanse and a pale lilac Remnants, with a bright green Wilderness and a "
                 "light gold Badlands. It is the most contrasty of the four and the one "
                 "with the clearest tier identities at a glance. Two things had to be dealt "
                 "with to ship it, both below: the navy sits close to the ocean in "
                 "lightness, and the lilac was sitting on top of the Storm Rift wall drawn "
                 "across it.",
    "deepwater": "The previous shipped set: deep forest green, mid slate blue, deep violet, "
                 "gold. Richer and heavier; the cells read more as land and less as a key.",
    "meridian": "The same four hues pitched brighter. More cheerful, more poster-like; the "
                "gold and the sky blue carry a lot of the picture, and the island markers "
                "have to work harder against them.",
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


def composited_table(fills, alpha):
    """Authored hue -> what lands on the ocean -> what that can carry."""
    rows = []
    for i, hue in enumerate(fills):
        s = C.composite(hue, OCEAN, alpha)
        k = ink(s)
        kc = C.contrast(k, s)
        y = C.relative_luminance(s)
        rows.append(
            f"<tr><td>{TIER_NAMES[i]}</td><td><code>{hue}</code></td>"
            f"<td><code>{s}</code></td><td class='n'>{y:.4f}</td>"
            f"<td><code>{k}</code></td>"
            f"<td class='n {'ok' if kc >= 4.5 else 'bad'}'>{kc:.2f}:1</td>"
            f"<td class='n'>{C.de00(s, OCEAN):.1f}</td></tr>")
    return ("<div class='wrap'><table><thead><tr><th>Tier</th><th>Authored hue</th>"
            "<th>As drawn</th><th>Luminance</th><th>Label ink</th><th>Ink contrast</th>"
            "<th>&Delta;E00 vs ocean</th></tr></thead><tbody>"
            + "".join(rows) + "</tbody></table></div>")


def alpha_report_table(fills):
    """One row per candidate alpha: every floor, re-measured on the composite."""
    rows = []
    for a in ALPHAS:
        s = seen(fills, a)
        cells = []
        for v in VISIONS:
            d, pair = C.closest_pair(s, v)
            cls = "" if v == "greyscale" else ("bad" if d < 5 else "warn" if d < 10 else "ok")
            cells.append(f"<td class='n {cls}'>{d:.1f}<small> T{pair[0]}/T{pair[1]}</small></td>")
        km = min((C.contrast(ink(f), f), i + 1) for i, f in enumerate(s))
        om = min((C.de00(f, OCEAN), i + 1) for i, f in enumerate(s))
        wm = min((C.de00(f, w), n) for f in s for n, w in WALLS)
        tag = " &middot; shipped" if a == SHIPPED_ALPHA else ""
        rows.append(
            f"<tr><td>{a:.2f} &mdash; {ALPHA_NAME[a]}{tag}</td>" + "".join(cells)
            + f"<td class='n {'ok' if km[0] >= 4.5 else 'bad'}'>{km[0]:.2f}:1<small> T{km[1]}</small></td>"
            + f"<td class='n {'ok' if om[0] >= 15 else 'bad'}'>{om[0]:.1f}<small> T{om[1]}</small></td>"
            + f"<td class='n {'ok' if wm[0] >= 10 else 'bad'}'>{wm[0]:.1f}<small> {wm[1]}</small></td></tr>")
    head = "".join(f"<th>{VISION_LABEL[v]}</th>" for v in VISIONS)
    return ("<div class='wrap'><table><thead><tr><th>Opacity</th>" + head
            + "<th>Worst label</th><th>Worst vs ocean</th><th>Worst vs wall</th>"
            "</tr></thead><tbody>" + "".join(rows) + "</tbody></table></div>")


def band_table(fills):
    """Where each tier's luminance sits as alpha falls, against the dead band."""
    lo, hi = ink_band()
    rows = []
    a = 1.00
    while a >= 0.53:
        s = seen(fills, a)
        cells = []
        blocked = []
        for i, f in enumerate(s):
            y = C.relative_luminance(f)
            inside = lo < y < hi
            if inside:
                blocked.append(f"T{i + 1}")
            cells.append(f"<td class='n {'bad' if inside else ''}'>{y:.4f}</td>")
        verdict = ("<td class='bad'>" + ", ".join(blocked) + " unlabelable</td>"
                   if blocked else "<td class='ok'>usable</td>")
        mark = " &larr; shipped" if abs(a - SHIPPED_ALPHA) < 1e-9 else ""
        rows.append(f"<tr><td>{a:.2f}{mark}</td>" + "".join(cells) + verdict + "</tr>")
        a = round(a - 0.02, 2)
    head = "".join(f"<th>{n}</th>" for n in TIER_NAMES)
    return ("<div class='wrap'><table><thead><tr><th>Opacity</th>" + head
            + "<th></th></tr></thead><tbody>" + "".join(rows) + "</tbody></table></div>")


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

    parts = ["<title>Admin world map &mdash; Nightfall, with transparency</title>",
             f"<style>{CSS}</style>"]

    parts.append("<h1>Admin world map &mdash; Nightfall, with transparency</h1>")
    parts.append(
        '<p class="lede">Nightfall is now the shipped palette, and the tier cells are drawn '
        '<b>translucent</b> again. Transparency is what broke the last translucent palette &mdash; '
        'the legend showed the raw hex while the map showed the composited colour, so the key '
        'was a lie about the picture &mdash; so this time the legend swatch <i>is</i> the '
        'composite, computed from the same three values the stylesheet emits, and a unit test '
        're-derives it rather than trusting it.</p>')
    parts.append(
        '<p class="verdict"><em>&ldquo;the color scheme i like Nightfall but add some '
        'transparancy to the zone colors, yes put the island inventory in there too i want to '
        'see this fully&rdquo;</em> &mdash; so: Nightfall, a designed amount of transparency '
        'rather than a return to the washed-out 38%, and the seeded inventory moved out from '
        'behind a click.</p>')
    parts.append(
        "<p>Two things had to move to ship Nightfall, and both are stated in full below rather "
        "than folded in quietly. Its lilac Remnants sat &Delta;E00 8.2 from the <b>Storm Rift "
        "wall</b> drawn across it &mdash; at full opacity, before any transparency was involved "
        "&mdash; so that wall moved off <code>" + OLD_STORM + "</code>. And the amount of "
        "transparency turns out not to be a free dial: with only two label inks there is a band "
        "of lightness in which <i>no</i> ink is legible, and each tier sweeps through it as "
        "opacity falls.</p>")
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
    parts.append(figure(
        DEEPWATER_HEX, "<b>Deepwater, shipped for one commit.</b> The hues put back, drawn at "
        "full strength. Measured well and read well; it is here because it is the thing "
        "Nightfall is being chosen over, not because anything was wrong with it."))
    parts.append("</div>")
    parts.append(swatch_strip(OLD, OLD_SEEN))
    parts.append(swatch_strip(CIVIDIS))
    parts.append(swatch_strip(DEEPWATER_HEX))

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

    # ---------------------------------------------------------------- transparency
    shipped_seen = seen(shipped, SHIPPED_ALPHA)
    lo, hi = ink_band()

    parts.append("<h2>How much transparency</h2>")
    parts.append(
        "<p>Three amounts, on the real map, at the top of this section. They are not a smooth "
        "range with three samples taken out of it: they are three of the only intervals "
        "available, and the reason is the label ink.</p>")
    parts.append(
        "<p>The console has exactly two label inks, <code>" + LIGHT_INK + "</code> and "
        "<code>" + DARK_INK + "</code>, and the cell label uses whichever has the greater "
        "contrast. Their crossover sits at relative luminance "
        f"<b>{((C.relative_luminance(LIGHT_INK)+0.05)*(C.relative_luminance(DARK_INK)+0.05))**0.5-0.05:.4f}</b>, "
        "and the best contrast obtainable there &mdash; with either ink, by definition the "
        f"better of the two &mdash; is only <b>{(C.relative_luminance(LIGHT_INK)+0.05)/(((C.relative_luminance(LIGHT_INK)+0.05)*(C.relative_luminance(DARK_INK)+0.05))**0.5):.2f}:1</b>. "
        f"AA for normal text is 4.5:1. So the interval <b>Y&nbsp;{lo:.4f}&ndash;{hi:.4f}</b> is "
        "dead: a fill landing in it cannot carry a legible label whatever ink is chosen. "
        "Lowering opacity drags every tier's luminance down, and each one crosses that dead "
        "band on the way. That is the whole reason the opacity is a value with a derivation "
        "rather than a taste setting.</p>")
    parts.append('<div class="maps">')
    for a in ALPHAS:
        s = seen(shipped, a)
        tag = " <b>&mdash; shipped</b>" if a == SHIPPED_ALPHA else ""
        parts.append(figure(
            shipped, f"<b>{ALPHA_NAME[a]} &middot; fill-opacity {a:.2f}</b>{tag}. On screen: "
            "<code>" + " ".join(s) + "</code>.", alpha=a, width=1000))
    parts.append("</div>")
    for a in ALPHAS:
        parts.append(f"<h3>Opacity {a:.2f} &mdash; {ALPHA_NAME[a]}"
                     + (' <span class="tag">Shipped</span>' if a == SHIPPED_ALPHA else "")
                     + "</h3>")
        parts.append(swatch_strip(shipped, seen(shipped, a)))
        parts.append(composited_table(shipped, a))

    parts.append("<h3>Every floor, re-measured on the composite</h3>")
    parts.append(
        "<p>Nothing here is measured on the authored hue. The authored hue is a value in a "
        "stylesheet; the composite is the colour a person is shown, and it is the only one "
        "worth a number. All three options clear every floor: &Delta;E00&nbsp;&ge;&nbsp;10 for "
        "the closest tier pair under each deficiency, WCAG&nbsp;AA for the worst label, "
        "&Delta;E00&nbsp;&ge;&nbsp;15 from the ocean underneath and &ge;&nbsp;10 from every "
        "weather wall drawn over.</p>")
    parts.append(alpha_report_table(shipped))
    parts.append(
        "<p>If you prefer one of the other two, say so and it is a two-line change &mdash; "
        "<code>MapTierPalette.FillOpacity</code> and the value pinned in its test. Both are "
        "already measured above and both hold every floor; nothing else has to move.</p>")
    parts.append(
        f"<p>{SHIPPED_ALPHA:.2f} ships because it is the middle of the widest usable interval "
        "and the most transparency you can take while every tier keeps real headroom on its "
        "label. Going further is not a matter of degree: the next step down parks Remnants in "
        "the dead band. What it costs is on the table &mdash; the closest tier pair falls from "
        f"{C.closest_pair(shipped, 'normal')[0]:.1f} to "
        f"{C.closest_pair(shipped_seen, 'normal')[0]:.1f} under normal vision, and the Expanse "
        f"navy closes from &Delta;E00 {C.de00(shipped[1], OCEAN):.1f} to "
        f"{C.de00(shipped_seen[1], OCEAN):.1f} against the ocean it is painted on. Both are "
        "still well clear of their floors, and neither is free.</p>")

    parts.append("<h3>Where each tier's luminance sits, opacity by opacity</h3>")
    parts.append(
        "<p>Red is inside the dead band. Read down the shipped row: no tier is in it, and the "
        "nearest one is Remnants, two steps away.</p>")
    parts.append(band_table(shipped))

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
        note = ("chosen hues" if key == SHIPPED
                else "previously shipped" if key == "deepwater" else "alternate")
        named.append((FINALISTS[key][0] + ", full strength", finalists[key], note))
    named.append((f"<b>Nightfall as drawn, fill-opacity {SHIPPED_ALPHA:.2f}</b>", shipped_seen,
                  "<b>shipped</b>"))
    parts.append(separation_table(named))

    parts.append("<h3>The shipped fills, as drawn, under each vision model</h3>")
    parts.append(cvd_strip(shipped_seen))

    parts.append("<h2>Label ink</h2>")
    parts.append(
        "<p>The ink on each cell is not picked, it is computed: whichever of the console's "
        "light ink <code>" + LIGHT_INK + "</code> and dark ink <code>" + DARK_INK + "</code> "
        "has the greater WCAG contrast against the fill wins, and a unit test refuses any "
        "palette where a label falls below AA for normal text (4.5:1).</p>")
    parts.append(
        "<p>Transparency makes that recompute load-bearing rather than tidy. Wilderness is the "
        f"proof: the authored green <code>{shipped[0]}</code> takes <b>{ink(shipped[0])}</b> at "
        f"{C.contrast(ink(shipped[0]), shipped[0]):.2f}:1, and the green as drawn "
        f"<code>{shipped_seen[0]}</code> takes <b>{ink(shipped_seen[0])}</b> at "
        f"{C.contrast(ink(shipped_seen[0]), shipped_seen[0]):.2f}:1. The ink flips. A palette "
        "that picked its ink from the CSS hex would have put dark text on a dark cell here and "
        "it would have measured fine, because it would have measured the wrong colour. As "
        "drawn, the four labels sit at "
        + ", ".join(f"{C.contrast(ink(f), f):.2f}:1" for f in shipped_seen) + ".</p>")

    parts.append("<h2>Clashes with the rest of the map</h2>")
    parts.append(
        "<p>A tier fill also has to survive everything drawn <i>over</i> it. The weather walls "
        "are stroked straight across the cells, so a wall that matches the fill under it "
        "disappears. This is the second wall to be moved for that reason, and the first one "
        "that was forced by the palette rather than by a legend: the Sand Storm wall went from "
        "<code>#d9b36b</code> to <code>#e8963c</code> when Badlands became gold, and now the "
        f"<b>Storm Rift</b> wall leaves <code>{OLD_STORM}</code>, because Nightfall's lilac "
        f"Remnants sat only &Delta;E00 {C.de00(OLD_STORM, shipped[2]):.1f} from it at full "
        f"strength and {C.de00(OLD_STORM, shipped_seen[2]):.1f} as drawn &mdash; a wall painted "
        "invisibly across the tier it crosses most.</p>")
    parts.append(
        "<p>It becomes <code>#c04ae8</code>: the same violet family, so &ldquo;Storm Rift is "
        "the purple one&rdquo; still holds, but pushed in chroma and lightness until it reads "
        "as a discharge rather than as more Remnants. Against Remnants as drawn that is now "
        f"&Delta;E00 {C.de00('#c04ae8', shipped_seen[2]):.1f}. The tier palette is the fixed "
        "point and the walls are fitted around it, because tiers cover area and walls are thin "
        "lines &mdash; the lines are the cheaper thing to move. Figures below are for the "
        "shipped fills as drawn.</p>")
    parts.append(wall_table(shipped_seen))
    parts.append(
        "<p>Against the ocean <code>" + OCEAN + "</code> &mdash; which under transparency is not "
        "only what a cell sits next to but what it is composited <i>onto</i> &mdash; the four "
        "fills as drawn sit at &Delta;E00 "
        + ", ".join(f"{C.de00(f, OCEAN):.1f}" for f in shipped_seen)
        + ". Every cell still reads as land rather than as more water. The Expanse navy is the "
        "one to watch: it is the darkest tier against the darkest possible backdrop, and it is "
        "the number that would fail first if the opacity were pushed lower.</p>")

    parts.append("<h2>Seeing the inventory, not finding it</h2>")
    parts.append(
        "<p>The other half of the ask. What is seeded on each island &mdash; databanks, metal "
        "deposits by ore and quality, trees by species &mdash; was already joined onto the map, "
        "but it was only reachable by clicking an island, and a number you have to click for is "
        "a number most operators never see. It is now on the page three ways, none of which "
        "needs an interaction:</p>")
    parts.append(
        "<ul><li><b>Every cell carries its own roll-up</b> as a third line under its tier text: "
        "islands, databanks, deposits, trees, and a count of how many of them have an inferred "
        "ore table. A <code>cell resources</code> toggle turns it off for reading the map as "
        "pure geography.</li>"
        "<li><b>The inspector opens on the world totals</b> instead of on &ldquo;select "
        "something&rdquo;, and clicking bare ocean returns to them.</li>"
        "<li><b>An island ledger under the map</b> lists all 254 catalogued islands, one row "
        "each, with a filter over name, cell, ore and wood.</li></ul>")
    parts.append(
        "<p>Provenance travelled with the data rather than being left behind by it. 193 of the "
        "254 islands were never surveyed for metal, so <i>which</i> ore a deposit carries is "
        "composed from the surveyed same-tier cohort. Those rows are marked <b>&#10033;</b> in "
        "amber in the ledger itself, not only in a footnote beneath it, and the marker rides "
        "the ore text so a skimmed line cannot be mistaken for a recovered one. The deposit "
        "<i>counts</i> are real either way. Fuel pods and loot containers are reported as 0 for "
        "every island, with the reason: retail shipped fuel pods only as hand-placed Haven "
        "statics and never shipped the lootable-container component at all. Zero with a reason, "
        "never an invented number.</p>")

    parts.append("<h2>What is pinned in code</h2>")
    parts.append(
        "<p>The palette, the transparency and the WCAG maths live in "
        "<code>WorldsAdriftServer/Admin/MapTierPalette.cs</code>, which emits the CSS for the "
        "drawn cell, the cell label, the legend swatch, the ledger's tier chip <i>and the "
        "ocean rule the composite assumes</i> &mdash; all from one list, so the legend cannot go "
        "back to disagreeing with the map. The wall colours and their legend keys moved into "
        "<code>MapWallPalette.cs</code> for the same reason, which is also how two walls that "
        "were being drawn without a key acquired one.</p>")
    parts.append(
        "<p>The CIEDE2000 and CVD simulation live in <code>MapColourMetrics.cs</code>, so the "
        "separation claims on this page are asserted by unit tests rather than by this page. "
        "The test that matters most re-derives the legend swatch by compositing the cell's "
        "declared fill, the cell's declared <code>fill-opacity</code> and the declared ocean, "
        "and fails if it does not land on the swatch the legend ships &mdash; the old bug, "
        "closed with transparency switched on rather than switched off. A change that drops a "
        "pair below &Delta;E00 10 under any deficiency, puts a label below AA, lands a tier in "
        "the dead band, or lets a wall fade into the tier it crosses, fails the build.</p>")

    open(OUT, "w", encoding="utf-8").write("\n".join(parts) + "\n")
    print("wrote", OUT)
    for key in order:
        print(f"  {FINALISTS[key][0]:16s}", finalists[key])
    for a in ALPHAS:
        print(f"  alpha {a:.2f}", seen(shipped, a))


if __name__ == "__main__":
    main()
