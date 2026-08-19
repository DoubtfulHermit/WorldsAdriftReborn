# Emblem objects

Two hundred more shapes an alliance crest could wear, traced off four artwork
sheets into the same vector form the fifty devices already use.

**All two hundred are live in the emblem editor.** `emblem-objects.json` is
embedded into `WorldsAdriftServer` verbatim and read by
`Emblems/EmblemObjectSheets.cs`, so **this file is the artwork the server draws**
— there is no generated C# table to keep in step. See
[Integrating](#integrating) for what that means when you change something.

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

**Done.** The four sheets ship as the last two hundred entries of
`EmblemObjects.All`, at indices 83–282, after the 33 hand-authored shapes and the
50 traced devices. What a later change has to know:

- **This JSON is the shipping artwork.** It is an `EmblemedResource` in
  `WorldsAdriftServer.csproj` and `EmblemObjectSheets` parses it at first use.
  Re-run the tracer and the server draws the new icon — there is nothing to
  regenerate, and nothing that can drift from the sheets.
- **Index order is not array order.** `EmblemObjectSheets.Sheets` declares which
  sheet comes after which, and the loader sorts by that and then by the icon's
  printed number. Reordering the JSON therefore cannot renumber anybody's crest.
  A fifth sheet is one row on the **end** of `Sheets`; a sheet the loader has not
  been told about is refused rather than appended blind.
- **Categories became tabs**, one per sheet:
  `japan` → **Eastern**, `objects` → **Salvage**, `shapes-outline` →
  **Outlines**, `shapes-solid` → **Solids**. The names are in
  `EmblemObjectSheets` as four consts; the tab order is in
  `AccountEmblemEditor.AppendObjects` and is browsing order, deliberately
  unrelated to index order.
- **`form` and `variant` are carried into `EmblemObjectSheets.Icon`**, and the
  three no-contrast forms are declared in `Unpaired`, so an outline/solid toggle
  can be built on data rather than on a naming convention. Nothing surfaces one
  yet — two tabs turned out to be the smaller answer — so there is currently no
  control to grey out.
- **`EmblemSpec.Version` did not move**, because nothing about what existing code
  draws changed.

### Hiding or replacing one of the eight

`EmblemObjectSheets.Suppressed` is a list of source names. An entry there stays in
the catalogue at its own index and still renders — a crest that already uses it is
untouched — it simply stops being offered on the panel and stops matching the
search box.

- **To hide one:** uncomment its line. The eight the table below grades BAD are
  already written out, commented, with the reason.
- **To bring it back:** comment the line out again.
- **To replace one:** redraw it *in its own place* on its sheet, re-run
  `trace_objects.py` and `verify_objects.py`, and leave `Suppressed` alone. The
  name and the index do not change, so the new drawing appears under the crests
  that already use the old one as well as under new ones. This is the option to
  prefer: hiding an icon leaves a hole in the panel, replacing it does not.

Either way the catalogue's revision hash moves, so no browser can be left holding
the old copy.

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
