"""The four tier-palette candidates, and the measurements they are judged by.

Change FINALISTS (or SHIPPED in build_doc.py) and re-run build_doc.py to
regenerate docs/admin-map-tier-palette.html with a different choice. The shipped
values must then be copied into WorldsAdriftServer/Admin/MapTierPalette.cs, whose
unit tests re-derive these same numbers in C# and will reject a palette that
drops a pair below dE00 10 under any deficiency.
"""
import colour as C

LIGHT_INK, DARK_INK = "#edf3f5", "#0a1219"
TIER_NAMES = ["T1 Wilderness", "T2 Expanse", "T3 Remnants", "T4 Badlands"]


def ink(fill):
    return LIGHT_INK if C.contrast(LIGHT_INK, fill) >= C.contrast(DARK_INK, fill) else DARK_INK


def report(label, fills):
    print("=" * 74)
    print(label, fills)
    for i, f in enumerate(fills):
        L, Cc, h = C.srgb_to_oklch(C.parse(f))
        lab = C.srgb_to_lab(C.parse(f))
        k = ink(f)
        print(f"  {TIER_NAMES[i]:15s} {f}  OKL {L:.3f} C {Cc:.3f} h {h:5.1f} | "
              f"L* {lab[0]:5.1f} | ink {k} {C.contrast(k, f):5.2f}:1 | "
              f"vs ocean {C.contrast(f, '#09151d'):4.2f}:1")
    for vision in C.VISIONS:
        d, pair = C.closest_pair(fills, vision)
        pairs = C.all_pairs(fills, vision)
        worst = " ".join(f"{a}{b}:{v:4.1f}" for (a, b), v in sorted(pairs.items()))
        print(f"  {vision:13s} min dE00 {d:5.1f} (T{pair[0]}/T{pair[1]})   {worst}")
    # adjacent WCAG contrast: is the staircase separable by lightness alone?
    adj = [C.contrast(fills[i], fills[i + 1]) for i in range(3)]
    print("  adjacent-tier contrast", [f"{a:.2f}" for a in adj])
    # clashes with the console's other map vocabulary
    others = {"Wind Rift #74c9cf": "#74c9cf", "Storm Rift #9b86d8": "#9b86d8",
              "Typhon #d48388": "#d48388", "Sand Storm #e8963c": "#e8963c",
              "Ice Storm #a9d6ed": "#a9d6ed", "World End #ec8f88": "#ec8f88",
              "haven #17322f": "#17322f", "live green #71d0a5": "#71d0a5",
              "island grey #80939c": "#80939c", "ocean #09151d": "#09151d"}
    print("  nearest console colour per tier:")
    for i, f in enumerate(fills):
        best = sorted(((C.de00(f, v), n) for n, v in others.items()))[:2]
        print(f"    T{i+1} {f}: " + ", ".join(f"{n} dE {d:.1f}" for d, n in best))



# Structural insight from the search: under protanopia and deuteranopia the four
# hues collapse onto two families - green and gold both land on the yellow side,
# blue and purple both land on the blue side. Members of the SAME family can only
# be told apart by lightness, so each family needs one dark tier and one light
# tier. Gold has to be the light member of its family (a dark yellow is an olive,
# and the brief asks for gold), which forces green dark. Blue/purple can go
# either way, and that choice is the main difference between these finalists.
FINALISTS = {
    "meridian": ("Meridian", [(0.470, 0.110, 148), (0.740, 0.125, 250),
                              (0.535, 0.150, 308), (0.845, 0.145, 96)]),
    "nightfall": ("Nightfall", [(0.600, 0.125, 145), (0.420, 0.115, 258),
                                (0.745, 0.105, 305), (0.860, 0.140, 95)]),
    "slate": ("Slate", [(0.455, 0.075, 150), (0.700, 0.080, 248),
                        (0.545, 0.095, 308), (0.815, 0.105, 97)]),
    "deepwater": ("Deepwater", [(0.375, 0.090, 150), (0.615, 0.105, 249),
                                (0.455, 0.120, 308), (0.765, 0.140, 96)]),
}


def build(key):
    label, rows = FINALISTS[key]
    return label, [C.oklch_hex(L, Cc, h)[0] for L, Cc, h in rows]



if __name__ == "__main__":
    for key in FINALISTS:
        label, fills = build(key)
        report(f"{key} - {label}", fills)
