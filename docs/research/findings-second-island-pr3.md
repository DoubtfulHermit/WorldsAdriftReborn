# PR 3: first distinct production island

Date: 2026-08-14

## Decision

The release-era Bossa MapFile in `world-data/wamap-islands.json` is the placement
authority. Jerodar's older closed-beta map is not used for island placement; it remains
useful only for sector/wall topology absent from the release artefact.

The first distinct island selected for runtime acceptance is **The Trades Challenge**:

| Fact | Evidence-backed value |
|---|---|
| Stable island id | `the-trades-challenge` |
| Runtime entity key | `island-the-trades-challenge` |
| Release MapFile row | `(13253.5547, -193.321426, -1972.03845)` metres |
| Exact Q52.12 seed | `(54286560, -791844, -8077469)` |
| Client terrain asset | `1206286558@Island` |
| Asset context | `notNeeded?`, the existing single-variant island dispatch context |
| Spawn order | `AfterPlayer` |

It is the closest distinct asset to the active Haven instance, about 3.84 km away.
The corresponding client files are present as
`Assets/unity/1206286558@island_unityclient` and its manifest (5.26 MiB). The extracted
surface table `world-data/island-surfaces/1206286558.json` is TRS-composed and contains
37,002 vertices, 14,646 upward vertices and 2,159 placement candidates over a local
AABB of approximately 403 x 104 x 403 metres.

Cardinal Guild's gameplay metadata independently maps workshop id `1206286558` to The
Trades Challenge (Saborian, tier 3, five databanks, Aluminium Q4). That metadata is
descriptive evidence; PR 3 does **not** fabricate a population from it.

## Runtime boundary

`IslandRegistry.CreateDefault` knows both definitions, but `WorldEntities.Default`
registers the second one only when `WAREBORN_SPAWN_SECOND_ISLAND=1`. Haven retains the
legacy `island` key, exact transform, and `BeforePlayer` loading-barrier position. The
new terrain has a collision-free key, loads after the player, and is correctly classified
as an island so component 1041 reads that entity's own asset name.

The old `WAREBORN_SPAWN_PROOF_ISLAND` duplicate-Haven diagnostic remains separate for
compatibility. It is not the production island.

## Deliberate omissions

This PR does not reuse Haven's surface bounds, resources, spawn/rescue position, or local
coordinate interpretation. Those would be plausible-looking but false. Multi-island
player location and per-island resource activation belong to the next runtime slice after
terrain acceptance.

## Required visual acceptance

Enable `WAREBORN_SPAWN_SECOND_ISLAND=1` on a test server, then verify:

1. Haven login and loading time remain normal.
2. `1206286558@Island` receives an AssetLoad request, AddEntity, 190602 and 1041 without
   rescue/error logging.
3. The terrain appears at the exact release position and has working collision.
4. Disconnect/reconnect does not duplicate or move either island.
5. A second peer sees the same entity id and transform.

Do not enable it by default or populate it until those checks pass.
