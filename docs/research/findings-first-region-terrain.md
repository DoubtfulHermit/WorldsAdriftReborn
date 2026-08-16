# First release-region terrain seed

Status: implemented behind an off-by-default server setting; not deployed or
visually accepted.

## Proven source and rollout

The source is the preserved Bossa release MapFile in
`world-data/wamap-islands.json`, not the older 303-island closed-beta import.
The first bounded cluster is release district C6:

| Rollout | Island | Asset | Q52.12 world position |
|---:|---|---:|---|
| 0 | Haven | 1431299145 | 69650145, -1305269, -4645549 |
| 1 | The Trades Challenge | 1206286558 | 54286560, -791844, -8077469 |
| 2 | Anchorage Isle | 650186469 | 53748326, -1229240, 1475919 |
| 3 | The Old Military Academy | 1673355094 | 58796532, 277533, 7307445 |
| 4 | Shattered Mausoleum | 949069116 | 58660618, -2158603, -19035735 |

All four optional client bundles/manifests and extracted island surfaces exist
locally. The order deliberately puts the already-proven Trades terrain first,
then the smaller/closer Anchorage and Old Military assets, with the much larger
Shattered Mausoleum last.

## Runtime contract

`WAREBORN_FIRST_REGION_TERRAIN_COUNT` selects a clamped prefix from 0 through 4.
Zero is the production default and changes no spawned terrain. The older
`WAREBORN_SPAWN_SECOND_ISLAND` remains independent and retains its proven Trades
resource profile. The new setting registers terrain only: it does not invent
metal, tree, databank, fauna or fuel populations for unsurveyed islands.

When enabled, spawn, resource routing, the world directory, local domain host,
databank parent lookup and admin topology share one configured island/region
registry. The selected islands form one ownership region (`first-c6-region`),
while the existing zero-count topology remains Haven and Trades as separate
regions for compatibility.

## Acceptance boundary

This is infrastructure, not a production enablement. Terrain currently enters
the joining peer's paced spawn plan and is not continuously checked out by
distance. Enabling all four adds roughly 44.5 MiB of compressed bundles and can
also make distant terrain remain visible. Accept one count at a time while
recording login duration, asset acknowledgements/timeouts, client memory/frame
time, collision, reconnect and two-client behavior. Terrain interest/unload is
the next prerequisite before treating the whole cluster as a normal live world.
Only Trades currently has a proven named teleport destination. Do not invent
landing coordinates for Anchorage, Old Military or Shattered; recover/validate
safe surface points before exposing them in the admin allowlist.

## Wall correction

The preserved release MapFile does contain exact wall geometry and wall types.
The closest Haven boundary is a type-5 `WorldEndWall` at x=15943.6523, about
1.061 km west of the active Haven instance; it is not a guessed first WindRift.
Wind/storm-wall simulation is still unimplemented, but its placement evidence
is no longer considered missing. Wall work must use this release dataset and
must not mix in the older closed-beta topology.
