# WAMap external-reference importer findings

Date: 2026-08-14

## Scope and source

PR2 adds `tools/world-import`, an offline parser and validator for a
user-supplied checkout of `https://github.com/Jerodar/WAMap`. No WAMap source
data, screenshots, map imagery, or generated report is committed.

The acceptance run used upstream revision
`5afc46d8f5a7190382f6ca058901df570fc0d9e6` (2018-06-19). Its six parsed files
had these SHA-256 values:

| file | SHA-256 |
| --- | --- |
| `settings.json` | `9ce9c3f3f29e381abd02faa08379e8a05e7fdbab1e18620fdad716cc6c9b205a` |
| `point_data.csv` | `1979837aedc48571ce42f825c7370fd49da81d66166b9b45a6e69864f0fd8722` |
| `sector_data.csv` | `db7137f08f88c8ca30aa512105a1631ff25af45b6ab6e9fc43b1e8654e6b1214` |
| `wall_data.csv` | `43c0e08f8d71018c2abde07a919a3bbe2efecd3aa09adfd4b0b288d8fe833896` |
| `island_data.csv` | `0616072f2f8a079acff458949f1e909f8584c742137535063bad89fd9b6565ad` |
| `zone_data.json` | `33b30d54ead855ffb7bbc3d87e5f9790c11a193358f78adbab1e7dda31ecd182` |

The repository has no explicit licence file at this revision. That reinforces
the external-path/generated-output-only boundary; it is not an implicit
licensing decision to redistribute the dataset.

## Actual schema, not the preliminary handover schema

The checked source differs from preliminary summaries:

- points are `ID,X,Z,Sectors`;
- sectors are `ID,Region,Tier,P1..P7` and can be non-quadrilateral polygons;
- walls are `ID,Tier,P1,P2`; `ID` is a repeated wall-group label, not a unique
  segment ID;
- islands have separate `X,Y,Z` columns plus survey metadata rather than one
  comma-packed location field;
- zones are eight named map-label placements, not numeric sector-type records.

The importer models wall group and segment identity separately (`X3:001`,
`X3:002`, etc.). It does not invent a zone lookup relationship that the source
does not contain.

## Validation result

The exact source validates as:

- 43 control points;
- 22 sector polygons;
- 50 wall segments;
- 304 island rows;
- 8 zone labels.

Wall segment types are `1:4`, `2:15`, `3:12`, `4:19`. Island tiers are
`1:64`, `2:83`, `3:81`, `4:76`.

All sector-corner references, wall endpoints, island-sector references,
coordinates, and zone label shapes are internally valid. Polygon containment
finds two source anomalies: `H2_13` and `H2_14` lie outside their declared `H2`
polygon. The importer reports them and does not move or silently “fix” them.

## Coordinate evidence and authority boundary

Jerodar's own `main.js` constructs map markers as `[CSV Z, CSV X]` and computes
display altitude from `CSV Y + settings.ZtoAltitude`. Therefore the neutral
model preserves `X/Y/Z` exactly; the importer has no map-pixel conversion and
does not claim that this older world revision is Wareborn runtime truth.

For production island placement, the stronger source already in this
repository is Bossa's preserved release-era `MapFile`:
`docs/research/world-data/wamap-islands.json`. It has 266 placements for 255
unique assets and matches all 255 shipped island bundles exactly. Jerodar is
still valuable for its explicit polygon and wall graph, but the layouts must
not be mixed without an authored, reviewed mapping.

## Runtime impact

None. PR2 adds tooling and research documentation only. It does not register a
second island, change the spawn plan, change interest, or introduce
`SimulationCore`.
