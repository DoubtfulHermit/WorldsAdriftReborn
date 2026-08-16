# First release-region terrain seed

Status: implemented behind an off-by-default server setting; not deployed or
visually accepted.

## Proven source and rollout

The source is the preserved Bossa release MapFile in
`world-data/wamap-islands.json`, joined by workshop asset id to the final
Cardinal survey. Geographical proximity to Haven is not progression order:
Haven exit was a server-selected teleport into the main world, and the nearby
C6 islands are tier 3. The first bounded cluster is therefore surveyed
Saborian tier-1 district B3, not C6:

| Rollout | Island | Asset | Q52.12 world position |
|---:|---|---:|---|
| 0 | Haven | 1431299145 | 69650145, -1305269, -4645549 |
| 1 | Mental Facility | 1143725558 | 34121298, 990124, 34175648 |
| 2 | Betrayal of the Copper King | 950242829 | 31506652, 580855, 40190030 |
| 3 | Highlands Hills | 1206946500 | 38919041, 516457, 38365766 |
| 4 | The Land that Man Forgot | 942473835 | 40357265, 37785, 29935290 |

All four optional client bundles/manifests and extracted island surfaces exist
locally. All are tier-1 Saborian islands with surveyed revival chambers and
3–5 databanks. Rollout order is increasing bundle size, keeping the first visual
acceptance inexpensive. The full B3 district has twelve surveyed tier-1 islands;
this is a bounded prefix, not a claim that four islands complete the zone.

## Runtime contract

`WAREBORN_FIRST_REGION_TERRAIN_COUNT` selects a clamped B3 prefix from 0 through 4.
Zero is the production default and changes no spawned terrain. The older
`WAREBORN_SPAWN_SECOND_ISLAND` remains independent and retains its proven Trades
resource profile. The new setting registers terrain only: it does not invent
metal, tree, databank, fauna or fuel populations for unsurveyed islands.

When enabled, spawn, resource routing, the world directory, local domain host,
databank parent lookup and admin topology share one configured island/region
registry. Haven and Trades retain their existing regions; selected tier-1
islands form `tier1-b3-region`. The zero-count topology remains unchanged.

## Acceptance boundary

This is infrastructure, not a production enablement. Terrain currently enters
the joining peer's paced spawn plan and is not continuously checked out by
distance. Enabling all four adds roughly 42.5 MiB of compressed bundles and can
also make distant terrain remain visible. Accept one count at a time while
recording login duration, asset acknowledgements/timeouts, client memory/frame
time, collision, reconnect and two-client behavior. Terrain interest/unload is
the next prerequisite before treating the whole cluster as a normal live world.
Mental Facility now has the first guarded visual-test destination:
`mental-facility`. Its island-local surface point is `(120.00, 34.26, -16.00)`
with a 2 m capsule stand-off. The extracted vertex normal is `ny=0.990`, the
surrounding cardinal/diagonal samples support a broad top surface, the nearest
authored static prop is more than 35 m away, and there is no authored static
overhead within 5 m. Runtime and the admin console both refuse the destination
unless Mental Facility terrain is registered. The other three tier-1 islands
still need independently derived and visually accepted landing points.

Build `07270f1` was staged in production with the bounded count set to exactly
one. Boot verification passed (Mental Facility owned by `tier1-b3-region`, no
unowned or duplicate entities); player-side terrain, collision and landing
remain pending visual acceptance. The production count is a runtime systemd
override so a VPS reboot fails safely back to the default zero-island rollout.

## Wall correction

The preserved release MapFile does contain exact wall geometry and wall types.
The closest Haven boundary is a type-5 `WorldEndWall` at x=15943.6523, about
1.061 km west of the active Haven instance; it is not a guessed first WindRift.
Wind/storm-wall simulation is still unimplemented, but its placement evidence
is no longer considered missing. Wall work must use this release dataset and
must not mix in the older closed-beta topology.
