#!/usr/bin/env python3
"""Synthesise a CustomShipHullState.hullData blob (component 1209) from scratch.

Byte format transcribed from the decompiled client (paths under
/home/ttanurhan/Games/WAReborn-decompiled):

  acs/ShipPlan.cs:114-131   Save()   -> int16 cellCount, then per cell:
                                        int16 cellNumber(x), int16 deckNumber(y), ShipCell
  acs/ShipCell.cs:83-95     Write()  -> ShipSection Front,
                                        bool hasBack (1 byte, true when no astern neighbour),
                                        [ShipSection Back]
  acs/ShipSection.cs:112-125 Write() -> Top[0], Top[1], Bottom[0], Bottom[1]   (ShipVertexVec)
                                        CurvePoints[0,0],[0,1],[1,0],[1,1]     (ShipCurveVertex)
  acs/ShipVertexVec.cs:44-49 Write() -> sbyte x (range 16), sbyte y (range 1.7), sbyte z (range 2)
  acs/ShipCurveVertex.cs:14-17       -> sbyte Offset (range 1)
  acs/Assets.Scripts.Utils/MathUtils.cs:518-521
        SerializeFloat(v, range) = (sbyte)round(clamp(v/range, -1, 1) * 127)

Default section geometry is the ShipSection constructor
(acs/ShipSection.cs:16-38): Top/Bottom = (-3,0,0) and (+3,0,0), all curves 0.
The smallest legal plan is ShipPlan.MakeDefault() = AddCell(0,0)
(acs/ShipPlan.cs:133-136).

NOT EXECUTED AGAINST A CLIENT. This is a transcription of the serialiser; it has
never been fed to CustomShipFrameVisualizer.OnHullDataUpdated.
"""
import base64, struct, sys


def ser_float(v: float, rng: float) -> int:
    x = max(-1.0, min(1.0, v / rng))
    # C# Mathf.RoundToInt is banker's rounding; none of the default values are
    # half-way cases, so plain round-half-away is equivalent here.
    n = int(x * 127 + (0.5 if x >= 0 else -0.5))
    return max(-128, min(127, n))


def vertex(x: float, y: float, z: float) -> bytes:
    return struct.pack("bbb", ser_float(x, 16.0), ser_float(y, 1.7), ser_float(z, 2.0))


def curve(offset: float = 0.0) -> bytes:
    return struct.pack("b", ser_float(offset, 1.0))


def section(half_width: float = 3.0) -> bytes:
    """One ShipSection with the constructor's default geometry."""
    out = b""
    for _ in range(2):                       # Top then Bottom
        out += vertex(-half_width, 0, 0)
        out += vertex(+half_width, 0, 0)
    out += curve() * 4
    return out


def cell(cell_number: int, deck_number: int, has_back: bool, half_width: float = 3.0) -> bytes:
    out = struct.pack("<hh", cell_number, deck_number)
    out += section(half_width)               # Front
    out += struct.pack("?", has_back)
    if has_back:
        out += section(half_width)           # Back
    return out


def plan(cells, half_width: float = 3.0) -> bytes:
    """cells: list of (cellNumber, deckNumber). Back is written for the cell with
    no astern neighbour, matching ShipCell.Write()."""
    have = set(cells)
    out = struct.pack("<h", len(cells))
    for (c, d) in cells:
        out += cell(c, d, has_back=((c - 1, d) not in have), half_width=half_width)
    return out


if __name__ == "__main__":
    variants = {
        "one_cell": [(0, 0)],
        "three_cells": [(0, 0), (1, 0), (2, 0)],
        "three_by_two": [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)],
    }
    for name, cells in variants.items():
        b = plan(cells)
        print(f"{name}\tcells={len(cells)}\tbytes={len(b)}\thex={b.hex()}")
        print(f"{name}\tbase64={base64.b64encode(b).decode()}")
