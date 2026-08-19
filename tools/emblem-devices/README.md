# Emblem devices

The fifty drawn devices an alliance can wear on its crest, and the pipeline that
turns the artwork sheet into something the login server can fill.

## What is here

| Path | What it is |
| --- | --- |
| `device-sheet.png` | The source artwork. Ten columns by five rows, numbered 01–50, one flat single-colour icon per cell. The **numbers are labels, not artwork.** |
| `device-names.json` | What each icon is called in the builder, in sheet order. Named for what the icon looks like — nothing else. |
| `trace_devices.py` | The tracer. Build-time only; the server never runs it. |
| `svg/NN-name.svg` | One SVG per device, generated. Committed so the vectors exist as files, not only as a table. |
| `contact-sheet.png` | Every traced device rendered back out, generated. This is the thing to look at when judging whether a trace is faithful. |

The tracer also writes `WorldsAdriftServer/Emblems/EmblemDeviceGeometry.cs`, which
is the table the server actually reads. It is generated — do not hand-edit it.

## Regenerating

```
python3 tools/emblem-devices/trace_devices.py
```

Needs Python 3, Pillow and numpy. Nothing else: the connected-component and
dilation passes are written out rather than pulled from scipy, and **no tracing
tool is involved at all** — `potrace`, `autotrace` and `inkscape` are not
installed, and more to the point the login server publishes self-contained, so
this pipeline must never become a runtime dependency. Only its output is
committed.

The run is deterministic. Re-running it on an unchanged sheet produces
byte-identical SVGs and an identical C# table, so a diff after a re-run means the
sheet or the tracer changed.

## How the trace works

1. **Slice.** Five printed rows are found by their blank scanlines; the ten icons
   in a row are separated by dilating the ink and labelling what joins up. The
   printed numbers are erased first, as components that are small, at the very
   top of the band, and neutral grey — all three, because six of the icons are
   themselves drawn in grey and a colour test alone would eat them.
2. **Read the coverage.** The artwork is antialiased, so the sheet already
   carries sub-pixel edge information: a pixel 40% covered is 40% of the way from
   paper to that icon's own ink colour. That ramp is read as a continuous field
   rather than thresholded to a bitmap.
3. **Marching squares.** The 50% isoline is extracted with linear interpolation
   along each cell edge, which puts every vertex on the sub-pixel position the
   artist's antialiasing implies. No curve fitting and no smoothing pass — only
   Douglas-Peucker, to drop the vertices sitting on a straight run.
4. **Check.** Every contour must close, exactly one segment may leave any
   crossing point, and every contour's winding must match its nesting depth. All
   three are hard failures. The second one is not theoretical: nine segments per
   icon were being dropped silently where the isoline passed exactly through a
   grid vertex, and the only symptom was a torn contour.
5. **Normalise and quantise.** Each icon is scaled about its own centre into the
   painter's `[-1, 1]` device box, keeping its drawn proportions, and rounded to
   integer thousandths. The quantising happens once, here, so the SVG a player
   downloads and the table the server rasterises are the *same numbers*.

## Fill rule

**Non-zero**, and the check in step 4 is what earns it. Marching squares walks
every contour with the ink on the same hand, so an outer boundary and a hole
inside it come out wound opposite ways — which is what non-zero needs. Even-odd
would work for the traced artwork too, but not for the devices drawn in code:
the cross is two overlapping bars and the saltire is two, and under even-odd
their overlaps would punch holes through the middle of each. One rule has to
serve both halves of the table, and this is the one that can.

## Adding to the sheet

Appending devices is safe — old indices keep their meaning, so no version bump.
**Reordering or removing one is not.** That shifts every index after it, which
silently changes the device on every saved crest, and it is what
`EmblemSpec.Version` and `EmblemVocabulary.MigrateCharge` exist for. See
`WorldsAdriftServer.Tests/EmblemVersionTests.cs`, which holds that every version
1 code still draws the crest it always drew.
