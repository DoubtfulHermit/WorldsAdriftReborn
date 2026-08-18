"""Small colour toolkit: sRGB <-> OKLab/OKLCh <-> CIELAB, CIEDE2000, WCAG,
and Machado 2009 colour-vision-deficiency simulation. No third-party deps."""
import math

import numpy as np

# ---------------------------------------------------------------- sRGB basics


def parse(hex_str):
    h = hex_str.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def to_hex(rgb):
    v = np.clip(np.round(np.asarray(rgb) * 255), 0, 255).astype(int)
    return "#%02x%02x%02x" % tuple(v)


def linearize(c):
    c = np.asarray(c, dtype=float)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def delinearize(c):
    c = np.asarray(c, dtype=float)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.abs(c) ** (1 / 2.4) - 0.055)


def relative_luminance(hex_str):
    r, g, b = linearize(parse(hex_str))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a, b):
    la, lb = relative_luminance(a), relative_luminance(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def composite(fg_hex, bg_hex, alpha):
    """Source-over: what the eye actually sees for an opacity:<alpha> layer."""
    fg, bg = parse(fg_hex), parse(bg_hex)
    return to_hex(fg * alpha + bg * (1 - alpha))


# ------------------------------------------------------------------- OKLab

M1 = np.array([
    [0.4122214708, 0.5363325363, 0.0514459929],
    [0.2119034982, 0.6806995451, 0.1073969566],
    [0.0883024619, 0.2817188376, 0.6299787005],
])
M2 = np.array([
    [0.2104542553, 0.7936177850, -0.0040720468],
    [1.9779984951, -2.4285922050, 0.4505937099],
    [0.0259040371, 0.7827717662, -0.8086757660],
])
M1i = np.linalg.inv(M1)
M2i = np.linalg.inv(M2)


def srgb_to_oklab(rgb):
    lms = M1 @ linearize(rgb)
    return M2 @ np.cbrt(lms)


def oklab_to_srgb(lab):
    lms = M2i @ np.asarray(lab, dtype=float)
    return delinearize(M1i @ (lms ** 3))


def oklch_to_srgb(L, C, h_deg):
    h = math.radians(h_deg)
    return oklab_to_srgb([L, C * math.cos(h), C * math.sin(h)])


def srgb_to_oklch(rgb):
    L, a, b = srgb_to_oklab(rgb)
    return L, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360


def in_gamut(rgb, eps=1e-4):
    return bool(np.all(np.asarray(rgb) >= -eps) and np.all(np.asarray(rgb) <= 1 + eps))


def oklch_hex(L, C, h_deg):
    """Highest-chroma in-gamut colour at (L, h) not exceeding C."""
    lo, hi = 0.0, C
    if in_gamut(oklch_to_srgb(L, C, h_deg)):
        return to_hex(np.clip(oklch_to_srgb(L, C, h_deg), 0, 1)), C
    for _ in range(40):
        mid = (lo + hi) / 2
        if in_gamut(oklch_to_srgb(L, mid, h_deg)):
            lo = mid
        else:
            hi = mid
    return to_hex(np.clip(oklch_to_srgb(L, lo, h_deg), 0, 1)), lo


# ------------------------------------------------------------------- CIELAB

D65 = np.array([0.95047, 1.00000, 1.08883])
RGB2XYZ = np.array([
    [0.4124564, 0.3575761, 0.1804375],
    [0.2126729, 0.7151522, 0.0721750],
    [0.0193339, 0.1191920, 0.9503041],
])


def srgb_to_lab(rgb):
    xyz = (RGB2XYZ @ linearize(rgb)) / D65
    f = np.where(xyz > (6 / 29) ** 3, np.cbrt(xyz), xyz / (3 * (6 / 29) ** 2) + 4 / 29)
    return np.array([116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])])


def ciede2000(lab1, lab2):
    L1, a1, b1 = lab1
    L2, a2, b2 = lab2
    kL = kC = kH = 1.0
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2)
    Cbar = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cbar ** 7 / (Cbar ** 7 + 25 ** 7))) if Cbar > 0 else 0.5
    a1p, a2p = (1 + G) * a1, (1 + G) * a2
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360 if (a1p or b1) else 0.0
    h2p = math.degrees(math.atan2(b2, a2p)) % 360 if (a2p or b2) else 0.0
    dLp = L2 - L1
    dCp = C2p - C1p
    if C1p * C2p == 0:
        dhp = 0.0
    elif abs(h2p - h1p) <= 180:
        dhp = h2p - h1p
    elif h2p - h1p > 180:
        dhp = h2p - h1p - 360
    else:
        dhp = h2p - h1p + 360
    dHp = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dhp) / 2)
    Lbar = (L1 + L2) / 2
    Cbarp = (C1p + C2p) / 2
    if C1p * C2p == 0:
        hbarp = h1p + h2p
    elif abs(h1p - h2p) <= 180:
        hbarp = (h1p + h2p) / 2
    elif h1p + h2p < 360:
        hbarp = (h1p + h2p + 360) / 2
    else:
        hbarp = (h1p + h2p - 360) / 2
    T = (1 - 0.17 * math.cos(math.radians(hbarp - 30))
         + 0.24 * math.cos(math.radians(2 * hbarp))
         + 0.32 * math.cos(math.radians(3 * hbarp + 6))
         - 0.20 * math.cos(math.radians(4 * hbarp - 63)))
    dTheta = 30 * math.exp(-(((hbarp - 275) / 25) ** 2))
    RC = 2 * math.sqrt(Cbarp ** 7 / (Cbarp ** 7 + 25 ** 7)) if Cbarp > 0 else 0
    SL = 1 + (0.015 * (Lbar - 50) ** 2) / math.sqrt(20 + (Lbar - 50) ** 2)
    SC = 1 + 0.045 * Cbarp
    SH = 1 + 0.015 * Cbarp * T
    RT = -math.sin(math.radians(2 * dTheta)) * RC
    return math.sqrt((dLp / (kL * SL)) ** 2 + (dCp / (kC * SC)) ** 2
                     + (dHp / (kH * SH)) ** 2
                     + RT * (dCp / (kC * SC)) * (dHp / (kH * SH)))


def de00(hex1, hex2):
    return ciede2000(srgb_to_lab(parse(hex1)), srgb_to_lab(parse(hex2)))


# ------------------------------------------- Machado 2009 CVD, severity 1.0

MACHADO = {
    "protanopia": np.array([
        [0.152286, 1.052583, -0.204868],
        [0.114503, 0.786281, 0.099216],
        [-0.003882, -0.048116, 1.051998],
    ]),
    "deuteranopia": np.array([
        [0.367322, 0.860646, -0.227968],
        [0.280085, 0.672501, 0.047413],
        [-0.011820, 0.042940, 0.968881],
    ]),
    "tritanopia": np.array([
        [1.255528, -0.076749, -0.178779],
        [-0.078411, 0.930809, 0.147602],
        [0.004733, 0.691367, 0.303900],
    ]),
}


def simulate(hex_str, kind):
    if kind == "normal":
        return hex_str
    if kind == "greyscale":
        y = relative_luminance(hex_str)
        return to_hex(np.clip(delinearize(np.array([y, y, y])), 0, 1))
    lin = linearize(parse(hex_str))
    return to_hex(np.clip(delinearize(MACHADO[kind] @ lin), 0, 1))


VISIONS = ("normal", "protanopia", "deuteranopia", "tritanopia", "greyscale")


def closest_pair(hexes, kind):
    """Smallest CIEDE2000 between any two of `hexes` under `kind` vision."""
    sims = [simulate(h, kind) for h in hexes]
    worst, pair = float("inf"), None
    for i in range(len(sims)):
        for j in range(i + 1, len(sims)):
            d = de00(sims[i], sims[j])
            if d < worst:
                worst, pair = d, (i + 1, j + 1)
    return worst, pair


def all_pairs(hexes, kind):
    sims = [simulate(h, kind) for h in hexes]
    return {(i + 1, j + 1): de00(sims[i], sims[j])
            for i in range(len(sims)) for j in range(i + 1, len(sims))}
