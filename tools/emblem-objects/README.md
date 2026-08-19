# Emblem objects

Two hundred more shapes an alliance crest could wear, traced off four artwork
sheets into the same vector form the fifty devices already use.

**Nothing here is wired into the server yet.** This directory is data plus the
two scripts that produce and check it. `WorldsAdriftServer` does not read it, no
C# file was added or changed to land it, and no existing device or charge index
moved. See [Integrating](#integrating) for what a follow-up has to do.

## What is here

| Path | What it is |
| --- | --- |
| `sheets/*.png` | The four source sheets, committed so the trace is reproducible from the repo alone. Ten columns by five rows, numbered 01–50. **The numbers are labels, not artwork.** |
| `object-names.json` | What each icon is called, per sheet, in sheet order. |
| `trace_objects.py` | The tracer. Build-time only; the server never runs it. |
| `verify_objects.py` | The gate. Re-reads the committed JSON and checks it against the sheets. |
| `emblem-objects.json` | **The deliverable.** Every object, with its path data. |
| `svg/<category>/NN-name.svg` | One SVG per object, generated, so the vectors exist as files and not only as a table. |
| `contact-<category>.png` | Every traced object rendered back out. |
| `compare-<category>.png` | Source icon beside traced result, per object. This is the sheet that says whether a trace is faithful. |
| `legibility-<category>.png` | Every object rendered at 40px and blown back up. This is the sheet that says whether an icon is *usable*, which is a different question. |

## Regenerating

```
python3 tools/emblem-objects/trace_objects.py     # writes the JSON and the SVGs
python3 tools/emblem-objects/verify_objects.py    # checks them, exits non-zero on a problem
```

Python 3, Pillow and numpy. Nothing else — no `potrace`, no `inkscape`, no
`cairosvg`, no scipy. The marching-squares core, the connected-component pass and
the dilation are all imported from `tools/emblem-devices/trace_devices.py` and
run unchanged, so there is one tracer in this repo, not two. The run is
deterministic: re-running on unchanged sheets produces byte-identical output.

## The coordinate convention

Stated plainly, because someone else has to wire this in.

- **Coordinates are integer thousandths of a `[-1, 1]` box.** `unit` in the JSON
  is `1000` and is the scale factor: a stored `-1000` is the left edge.
- **The origin is the centre of that box**, and it is also the centre of the
  icon's own bounding box — every object is scaled about its own centre.
- **y points DOWN**, so the numbers read the same way they render. A stored
  `-500` is above the centre.
- **The viewBox is `-1000 -1000 2000 2000`**, which is what the SVGs carry.
- Each object fills **0.98** of the box on its longer axis (`extent` in the
  JSON), and is centred on the other. Below one deliberately, so an object's own
  bounding box never coincides with the box a painter scales into.
- **The fill rule is NON-ZERO.** This is not a preference. Marching squares walks
  every contour with the ink on the same hand, so an outer boundary and the
  boundary of a hole inside it come out wound opposite ways, which is what
  non-zero needs — and `check_winding` proves it for every contour of every one
  of the 200 rather than assuming it. Non-zero is also the rule the existing
  fifty devices and the eleven drawn charges use, so there is one rule for the
  whole vocabulary.
- **Path data is `M x y L x y … Z`, straight segments only.** No curves, no arcs,
  no relative commands. Contours are concatenated in one `d` string.
- **Colour is not part of an object.** The sheets are drawn in various inks and
  the SVGs carry the source ink so they are viewable, but the emblem system
  colours each layer itself. Ignore it.

## The catalogue

`emblem-objects.json`:

```json
{
  "schema": "wareborn.emblem-objects/1",
  "unit": 1000, "extent": 0.98,
  "viewBox": [-1000, -1000, 2000, 2000],
  "fillRule": "nonzero", "axis": "y-down", "origin": "centre",
  "objects": [
    {
      "name": "torii-gate",          // kebab-case, unique across all 200
      "category": "japan",           // japan | objects | shapes-outline | shapes-solid
      "sheet": "japan.png",          // which sheet it came off
      "index": 1,                    // 1-50, its printed number on that sheet
      "contours": 33, "points": 612,
      "path": "M-980 -612 L…Z M…Z"
    }
  ]
}
```

Objects in the two `shapes-*` categories carry two more fields, `form` (the base
name, e.g. `hexagon`) and `variant` (`outline` or `solid`). They exist so the
pairing below is *data* rather than a naming convention a reader has to infer:
join the two categories on `form` to offer "outline or solid" as one control.

`name` + `category` is the stable identity. `index` is only where it sat on its
sheet.

## The outline/solid pairing

**It holds. `shapes-empty.png` and `shapes-full.png` are the same fifty
geometric forms in the same grid order**, and they are named as fifty pairs
rather than a hundred unrelated shapes. This was checked, not assumed:
`check_pairing` in `verify_objects.py` rasterises both variants of every index
and requires the stroked one to lie on the filled one — on its rim for a normal
outline, or at least inside its body for the forms whose stroke is drawn inward,
like the corner-bracket squares (12, 36). All fifty pass.

**Three indices are exceptions, and they are exceptions to *outline versus
solid*, not to the pairing.** The form is the same on both sheets; what is
missing is the contrast between the two drawings:

| Index | Form | What is actually on the sheets |
| --- | --- | --- |
| 11 | `dashed-ring` | Drawn as a dashed ring on **both**. There is no filled variant. |
| 38 | `diamond-ring` | Four diamonds and dashed arcs on **both**. Neither is filled. |
| 24 | `vesica-leaf` | Drawn already filled on **both**. There is no stroked variant. |

They keep their paired names, so an editor's pairing control stays uniform, but
on these three the outline/solid toggle changes nothing the eye can see. **A UI
should grey the toggle out for them**; the three indices are declared in
`NOT_FILLED` / `NOT_STROKED` in `verify_objects.py`, so a fourth appearing
because a sheet was redrawn fails the gate instead of shipping silently.

Note that `gear` (26) and `square-in-square` (45) are *not* exceptions: their
solid variants have genuine interior holes, which is the artwork, and non-zero
fill renders them correctly.

## How good are they

Two different questions, and they have different answers.

**Is the trace faithful to the source? Yes, for all 200.** `verify_objects.py`
measures intersection-over-union between each traced fill and its source ink,
both normalised into the same box. The worst of the 200 is 0.855, the median is
0.928, and nothing is below 0.80. The residual is the half-pixel of edge error
inherent in the comparison, which costs a hairline stroke proportionally more
than a solid. `compare-*.png` shows source beside trace for every object and the
pairs are difficult to tell apart. Interior detail survived: the gear keeps its
bore and teeth, the daruma keeps its face, the dashed rings keep every gap.

**Is the icon legible at the size a crest is actually seen? Not always.** A
device is drawn about 130px across in a 256px emblem, and far smaller in a
roster list. These four sheets are drawn in a much finer, broken-stroke style
than the original fifty, and some of them are simply too detailed to survive it.
`legibility-*.png` renders every object at 40px. Judged off those:

| | GOOD | MARGINAL | BAD |
| --- | --- | --- | --- |
| japan | 35 | 10 | 5 |
| objects | 41 | 6 | 3 |
| shapes-outline | 49 | 1 | 0 |
| shapes-solid | 50 | 0 | 0 |
| **total** | **175** | **17** | **8** |

- **BAD — does not read at emblem size.** Interior detail collapses into speckle
  and the subject is not recognisable.
  - `japan` 03 `koi`, 11 `turtle`, 23 `komainu`, 28 `shishi-lion`, 41 `scorpion`
  - `objects` 40 `ruins`, 41 `airship`, 46 `crane`
- **MARGINAL — the silhouette reads, the interior does not.** Usable if the
  interior detail is not the point.
  - `japan` 04 `dragon-head`, 06 `samurai-mask`, 10 `great-wave`, 21 `swallow`,
    31 `shrine`, 34 `kabuto-helmet`, 37 `sea-bream`, 43 `phoenix`, 46 `peony`,
    49 `broadleaf-tree`
  - `objects` 05 `octagon-frame`, 21 `cube`, 29 `cannon`, 32 `winged-badge`,
    37 `sail`, 50 `robot-arm`
  - `shapes-outline` 32 `winged-chevron-outline`
- Every one of the 50 solid shapes and 49 of the 50 outline shapes read cleanly.
  If only one category ships, ship those.

**None of the 25 is a tracing failure**, and retracing will not fix any of them —
the vectors already match the artwork closely. The problem is that the artwork
carries more detail than the render size can hold. Fixing them means simplifying
the *drawings*, which is an art decision, not a pipeline one.

## Integrating

Nothing in `WorldsAdriftServer` reads this yet. To expose these as choosable
devices a follow-up needs to:

1. **Append, never insert.** Live crests store devices by index — there is a live
   code `2-0-7-39-9-9-4` in the wild — so the existing 50 traced devices and 11
   drawn charges must keep the indices they have. New objects go on the end.
   `tools/emblem-devices/README.md` and `EmblemVersionTests.cs` are the standing
   statement of this rule; it applies unchanged here.
2. **Decide what ships.** 200 is a large jump in vocabulary size and 8 of them do
   not read at size. The table above is the input to that decision.
3. **Get the paths into the painter.** They are already in the painter's
   coordinate system, fill rule and winding, so this is a data move rather than a
   conversion: emit a table in the shape of `EmblemDeviceGeometry.Paths` from
   `emblem-objects.json`, or read the JSON at build time. Either way the
   generated file should be `// <auto-generated>` and regenerable, and it should
   be a **new** file rather than an edit to `EmblemDeviceGeometry.cs`, so the
   frozen fifty stay a single reviewable unit.
4. **Carry `category` and `form` through to the builder.** Four flat lists of
   fifty is a worse editor than a category filter plus an outline/solid toggle,
   and the data to build the better one is already in the JSON. Remember to grey
   the toggle out on forms 11, 24 and 38.
5. **Bump `EmblemSpec.Version` only if a rendering rule changes.** Appending
   devices does not change what any existing code draws, which is exactly why
   appending is the safe move.

## Adding a 201st icon

The sheets are the source of truth, so a new icon is added to a sheet, not to the
JSON.

1. **Draw it into a sheet**, or add a new sheet. A sheet must be a 10-wide grid,
   each icon numbered above it in **neutral grey or black**, with the number at
   the top of its row and no wider than 34px or taller than 26px per digit. The
   ink may be any colour — colour is ignored — but it must be darker than 230 on
   every channel and it must not be neutral grey at the top of a row, or it will
   be mistaken for a number.
2. **Leave a corridor.** Adjacent icons must be separated by a vertical ink-free
   gap that is wider than any gap *inside* a single icon. The tracer takes the
   nine widest corridors in a row as the cell walls and then checks each one
   separates two consecutive printed numbers, so an icon that runs into its
   neighbour fails loudly rather than being cut in half.
3. **Append, do not insert.** Adding a row of ten to the bottom of a sheet is
   safe. Inserting an icon in the middle renumbers everything after it, which
   silently changes the object on every crest that referenced one — the same
   hazard the fifty devices have.
4. **Add the name** to `object-names.json`, in sheet order, kebab-case, unique
   across all categories. The tracer refuses to run if a sheet's name list is not
   exactly as long as its grid, and refuses to write if two objects share a name.
5. **Run both scripts.** `trace_objects.py`, then `verify_objects.py`.
6. **Look at the sheets.** `compare-*.png` for whether the trace is faithful and
   `legibility-*.png` for whether it is usable at size. The second one is the one
   that will disappoint you, and it is not something the tracer can fix.

For a new *sheet*, add it to `SHEETS` in `trace_objects.py` with its category,
and add a matching key to `object-names.json`.
