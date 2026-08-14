# Wareborn world-reference importer

This tool parses and validates an **external** checkout of
[`Jerodar/WAMap`](https://github.com/Jerodar/WAMap). It exists for historical
sector, weather-wall and island research; it is not a production-server data
loader and it has no runtime network dependency.

Jerodar's 303 island rows describe an older closed-beta world. Wareborn's
release-era island placement source remains Bossa's preserved `MapFile` at
`docs/research/world-data/wamap-islands.json` (266 placements, exactly matching
all 255 shipped island bundles). Do not combine the two layouts silently.

## Run

```bash
git clone https://github.com/Jerodar/WAMap.git /path/to/WAMap
revision=$(git -C /path/to/WAMap rev-parse HEAD)

dotnet run --project tools/world-import/Wareborn.WorldImport -- \
  --wamap /path/to/WAMap \
  --source-revision "$revision" \
  --out tools/world-import/output/jerodar-summary.json
```

The generated output directory is ignored. The tool does not copy the WAMap
dataset or map imagery into Wareborn.

Validation fails on duplicate identifiers, missing sector/wall point
references, missing island sectors, malformed coordinates, malformed zone
labels, missing required files, and invalid CSV/JSON. Polygon containment is
reported as a source anomaly rather than silently rewriting an island.

`wall_data.csv` repeats its `ID` for the multiple segments of one wall group.
The neutral model preserves that group ID and assigns deterministic segment IDs
such as `X3:001`, `X3:002`; it does not incorrectly reject the repeated group.

Coordinate evidence comes from WAMap's own `main.js`: CSV `X` is map X, CSV
`Z` is the other horizontal axis, and CSV `Y` is altitude (with
`ZtoAltitude` used only for display colouring). The importer preserves these
numbers and does not claim a production Wareborn transform.
