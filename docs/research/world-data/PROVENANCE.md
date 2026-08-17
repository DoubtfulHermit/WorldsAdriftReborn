# World layout data — where it came from

`findings-world.md` concluded that no world layout ships with the client (255 island
bundles, every one authored at local origin) and recommended hand-authoring an
`islands.json`. **That recommendation is now obsolete.** Bossa's real `MapFile`
survives in a community repository.

## wamap-islands.json — the real world layout

Bossa's own file from `../../gsim/src/main/resources/islands.json`, the path named in
the decompiled `WorldEditorCore.cs:148-149`. Preserved as `data/wamap.json` in the
Cardinal Guild map's backend.

- Source: <https://github.com/cardinal-guild/wasurveyor>
- Commit: `3fc4352401193e0db721d1478af1ec2ed90db578` (2019-06-20 11:15)
- Re-fetch:
  `gh api repos/cardinal-guild/wasurveyor/contents/data/wamap.json?ref=3fc4352401193e0db721d1478af1ec2ed90db578 --jq .content | base64 -d`

**Take this commit, not HEAD.** Later commits edited it for their importer:
`206e721` (same day) removed the Haven islands, dropping 266 → 254; `b7b2a4f` and
`c23058e` (2019-06-22) applied further import workarounds. Only `3fc4352` is pristine.

Its top-level shape is field-for-field the decompiled `WorldEditorCore.MapFile`:
`WorldInfo`, `Haven`, `Islands[]`, `Biomes[]`, `Walls[]` — matching
`IslandStoreData{x,y,z,Island}`, `ZoneStoreData{x,z,Type,Civ,District}` and
`WallStoreData{x1,z1,x2,z2,Type}`. It is the file, not a reconstruction.

### Verified against this machine's install
Independently re-checked, not taken on trust:

```
placements:        266
unique assets:     255
shipped bundles:   255   (~/Games/WorldsAdrift/Assets/unity/*@island_unityclient)
in map, not shipped: NONE
shipped, not in map: NONE
```

A perfect bijection. The `Island` field is `<steamWorkshopId>.json` and the bundle is
`<steamWorkshopId>@island_unityclient` — a direct string join, no mapping table.
**This settles the open question in findings-world.md: the 255 bundles are exactly
this world.**

Why 266 placements for 255 assets: `1431299145` is the **Haven starter island**,
instanced **12 times** in a north-south row at x≈17000, z from −15775 to +16336 — all
beyond `Haven.xOfVerticalSeparator = 15943.65`. It is the only asset used more than
once. Loading Haven therefore *requires* the entity-aware `InitAndSerialize` from the
findings' step 4, since one asset maps to twelve distinct positions.

Our current seed island `949069116` is **"Shattered Mausoleum"** at
**(14321.44, −527.0027, −4647.396)**.

## TWO CORRECTIONS TO findings-world.md
1. **World extent is ±18000, not ±12000.** `WorldEdgeLength = 36000`,
   `GSIMConfig "36x36"`, walls terminate at exactly ±18000.0, real data spans
   X −16868..17174, Z −16786..16794. The `WorldSize = 12000f` at
   `WorldEditorCore.cs:74` is an editor default the shipped world outgrew.
2. **The altitude band ±600 is right and the real world barely uses it:** Y spans only
   **−527.0 .. +356.8** — the whole world is a thin 884 m slab. Real neighbours in the
   Haven row are ~2900 m apart, so the findings' proposed 900 m test spacing is roughly
   the right order but tighter than the real world.

## Cross-validated independently
`cardinal-guild/wamap/static/islands.json` (the live public map, 604 KB GeoJSON) holds
the same 254 non-Haven workshop ids and reduces to the MapFile by an exact linear
transform — max residual 0.005, i.e. float rounding:

```
mapLat = z/3.85 - 4750     mapLng = x/3.85 + 4750     altitude = y + 2000
```

Confirmed against `ImportIslandsCommand.php::convertLng/convertLat/convertHeight`, which
treats `wamap.json` as the authoritative position source and scrapes Steam only for
names and images. Altitude holds for 254/254. Two independent artefacts, exact
agreement.

## cardinal-guild-islands.json — per-island gameplay data
254 islands with name, creator, tier, databank count, **per-island metal tables with
quality values**, tree species, PvE/PvP variants and workshop URLs.
Source: <https://github.com/cardinal-guild/wamap> (`static/islands.json`).
Useful for populating resource spawners — `findings-resources.md` established that
1010/1011 have no serializer handler today.

**This file has exactly ONE commit in its history** (`bbaebec0`, 2019-09-10,
"Surveyor backend removed, images and json files immortalized forever"). There is no
fuller revision to fetch — checked.

**Its metal coverage is a survey gap, not world data.** 254/254 islands carry a
surveyor name and an exact databank count, but only **38** carry a `pveMetals` table
and **33** a `pvpMetals` one. The backend (`cardinal-guild/wasurveyor`) is a
player-report system with an admin approval queue, its API serialises those arrays
unfiltered, and the map UI renders an empty one as the string "No metals data" —
i.e. *unsurveyed*. Update 31 replaced the whole map on 2019-06-11 and the game shut
down on 2019-07-26, so volunteers had five weeks. Databank counts ARE complete and
exact; metal tables are not. Details and the derived backfill:
`docs/research/findings-island-resource-population.md`.

Enum notes: `Civ` is `0 = Saborian, 1 = Kioki`. `Biomes.Type` is 1–4 across
four tiers and 20 cells total: Tier 1 has 4 cells, Tier 2 has 4, Tier 3 has 6,
and Tier 4 has 6. Eighteen cells carry authored district IDs. The other two are
Tier-4 cells whose `District` field is explicitly `null`; they must not be
invented as E1/E2 or folded into the adjacent E3 cell. `Walls.Type` ∈
{0,1,3,5}, 44 segments.

## Deliberately NOT included
`github.com/Jerodar/WAMap`'s `island_data.csv` (303 islands) is an **earlier
closed-beta world revision** — Update 27, a different layout, extent ±16877. Historical
interest only. **Do not mix it with the release layout.**

Re-examined 2026-08-16 as a possible source of the missing metal tables, since it
covers 271 of 304 islands (89%) and would join to ~47 of ours by Steam URL.
**Still rejected, and now for a second independent reason:** it predates Update 31,
so it knows only 12 metals (no Epilar, Eternium or Orthite) and its qualities come
from the balance pass Update 31 explicitly rewrote — it records Aluminium Q1 and
Tungsten Q1 on tier-4 islands, where the release build's 280 tier-4 observations
never go below Q7. Importing it would write values the release build provably never
produced. It IS cited as corroboration that ~89% of islands carried metal.

## The map-UI question, settled permanently
The client has **no world map UI**. `Bossa.Travellers.Visualisers.WorldMap` contains
exactly one file, `ZoneInfoProvider.cs`, an `IDebugInfoProvider` returning a dev-console
string. The only code referencing `islands.json` / `IslandStoreData` / `MapFile` is the
four `WorldEditor*` files — the internal authoring tool, not runtime.
(`MetalDepositAtlas*` and `AtlasLifter*` are ore and ship-part gameplay classes;
`ImposterSystem/AtlasHandler` is texture atlasing.) Island positions were **always
server-side only** — which no longer matters, because we have the server's file.

## Licence / attribution
Community-preserved data from public repositories, recorded here so the provenance
travels with it. Not our work; credit belongs to the Cardinal Guild map project.

## Community engineering spreadsheets and WAEngenius

The project-owner supplied four historical community spreadsheets plus the
WAEngenius engine calculator on 2026-08-16. The surviving workbooks, every tab as
display/formula CSV, the calculator source, its hidden source-data workbook,
hashes, limitations and attribution are preserved under
[`external/wa-community-2026-08-16/`](external/wa-community-2026-08-16/README.md).

These sources contain valuable measured engine, wing, material, mass and panel
resilience data. They are community evidence rather than Bossa authority and may
describe different game patches. One supplied Google Sheet was already deleted
(HTTP 410); the failed source and recovery attempts are recorded rather than
silently replacing it with guesses.

## island-surfaces/ — regenerated 2026-08-08 (TRS fix)

All 255 files were re-extracted after a bug fix in `tools/sweep_one.py`. The old
walk summed `m_LocalPosition` up the transform hierarchy and dropped the LOD0
grid cell's `m_LocalScale = (4,4,4)`, so every terrain vertex sat at a quarter of
its true offset inside its own 64 m cell. Typical error: **mean |ΔY| ~25 m**, and
the "surface" was disconnected 17 m patches with 47 m holes between them.

Any `island-surfaces/*.json` whose `meta` lacks `"transform": "TRS-composed"` is
from the broken extractor. **Do not use it.**

- Fix: `tools/unity_transform.py` (conventions + self-check) and
  `tools/island_surface.py` (hierarchy walk).
- Validated: `tools/validate_949069116.py` — the one empirically known altitude
  in this research (player at rest, `y = −31.2`) goes from **−23.87 m** error to
  **+0.13 m**.
- Regenerate: `systemd-run --user --scope -p MemoryMax=4G uv run --with UnityPy
  python tools/sweep_all.py ~/Games/WorldsAdrift/Assets/unity island-surfaces`
  — **41 s for all 255, sequentially. Never fan this out in parallel.**

`nodes-949069116.json` is derived from the surface table and was regenerated too;
its `fixedPoint190602` also had a rounding-vs-truncation bug (`ToFixedPoint` is
`(long)(d*4096)`, truncating toward zero).
