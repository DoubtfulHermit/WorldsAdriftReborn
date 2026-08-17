# First release-region terrain seed

Status: bounded terrain registration and continuous per-peer terrain checkout
are deployed for a one-island test behind separate off-by-default settings;
real Unity acceptance is pending.

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
| 5 | DrunkRaven Inn | 924807150 | 33756291, 1415496, 18067083 |
| 6 | Beautiful Wildlands | 742077672 | 30901057, 39425, 22887514 |
| 7 | The Three | 1129983108 | 20839827, -667437, 22841061 |
| 8 | Roxborough Isle | 1483206813 | 29404985, 445557, 28800561 |
| 9 | Camps Daurats | 1319380815 | 24433521, -948307, 38831677 |
| 10 | Triphalion City | 1675054039 | 22536482, 205446, 30035513 |
| 11 | Splitpeak Pass | 966489234 | 25630871, 388610, 16943796 |
| 12 | Crimson Paradise | 938282702 | 39857582, -721753, 22106451 |

All twelve optional client bundles/manifests and TRS-correct extracted island
surfaces exist locally. All are surveyed tier-1 Saborian islands with 3–5
databanks; seven report revival chambers. The first four rollout slots remain
frozen so count one still means Mental Facility. The other eight are ordered by
increasing bundle size. The full district is 116.5 MiB compressed; the original
four-island acceptance prefix is roughly 42.5 MiB.

`IslandSurveyCatalog` pins the joined Cardinal gameplay facts for all twelve:
databank count, revival-chamber presence, tree species, turret/danger flags and
surveyed PvE/PvP metal tables. An empty metal table remains explicitly empty; it
is not permission to invent a generic population. Exact original dynamic-node
coordinates did not survive and are not claimed by this topology milestone.

## Runtime contract

`WAREBORN_FIRST_REGION_TERRAIN_COUNT` selects a clamped B3 prefix from 0 through 12.
Zero is the production default and changes no spawned terrain. The older
`WAREBORN_SPAWN_SECOND_ISLAND` remains independent and retains its proven Trades
resource profile. The new setting registers terrain only: it does not invent
metal, tree, databank, fauna or fuel populations for unsurveyed islands.

When enabled, spawn, resource routing, the world directory, local domain host,
databank parent lookup and admin topology share one configured island/region
registry. Haven and Trades retain their existing regions; selected tier-1
islands form `tier1-b3-region`. The zero-count topology remains unchanged.

## Continuous terrain checkout

`WAREBORN_TERRAIN_INTEREST_ENABLED=1` enables optional-island checkout only when
resource interest is also enabled. Haven remains unconditional. Optional terrain
uses extracted collision-surface AABBs plus configurable load/unload hysteresis,
not origin-only distance. The connect plan skips distant optional terrain as a
RequestAsset/AddEntity unit; live movement later adds it after an exact correlated
asset-loaded acknowledgement. New clients carry asset name/context in a marked
channel-0 protobuf; legacy eight-byte acknowledgements use a bounded fallback and
retain visited terrain instead of attempting unsafe remove/re-add.

Resources cannot add before their owning terrain is ready and are drained before
terrain removal. Destination terrain loads before source terrain can leave.
Teleports to optional islands defer until that peer has the destination checkout,
then execute once, or fail safely after a bounded wait. Channel-5 teardown clears
served components, native client-object references and the per-peer AddEntity
ledger before a legal re-entry.

The default tuning is 1200 m load, 1600 m unload and a 30 s cold-bundle ACK
timeout (clamped 10–120 s, with one re-request). The feature remains local
single-process visual checkout; it does not move IslandDomain authority or alter
persistence.

## Acceptance boundary

This is infrastructure, not a production enablement. Continuous checkout has
passed headless policy, protocol, native round-trip and full server tests, but it
still needs real Unity acceptance. Accept one count at a time while recording
login duration, exact asset acknowledgements/timeouts, client memory/frame time,
collision, approach/leave/re-entry, teleport deferral, reconnect and independent
two-client visibility. Do not enable the complete district merely because
headless tests pass; advance the bounded prefix exactly one island at a time.
Mental Facility now has the first guarded visual-test destination:
`mental-facility`. Its island-local surface point is `(120.00, 34.26, -16.00)`
with a 2 m capsule stand-off. The extracted vertex normal is `ny=0.990`, the
surrounding cardinal/diagonal samples support a broad top surface, the nearest
authored static prop is more than 35 m away, and there is no authored static
overhead within 5 m. Runtime and the admin console both refuse the destination
unless Mental Facility terrain is registered. The other three tier-1 islands
still need independently derived and visually accepted landing points.

Build `069a372` and client manifest `2026.08.17-1` are staged in production with
the bounded count set to exactly one and continuous terrain checkout enabled.
Boot verification passed (Mental Facility owned by `tier1-b3-region`, no unowned
or duplicate entities, terrain telemetry mode `on`); player-side terrain,
collision and landing remain pending visual acceptance. The rollout flags are
runtime systemd overrides so a VPS reboot fails safely back to the defaults.

The first Unity run proved visual terrain, collision, exact correlated asset
acknowledgement and safe deferred arrival. Returning to Haven also restored its
resource interest, but revealed that the client's bounded transform confirmation
did not clear terrain's requested-destination pin when the sparse 1073 island
frame was absent. The server follow-up unifies both accepted teleport-arrival
proofs into the same terrain landing transition; unload and re-entry still need
one repeat live pass.
The unified landing transition is deployed in `b52f504`.

Production then completed two Haven → Mental Facility → Haven cycles with the
same v1 client. Both cold/re-entry loads received exact ACKs and reached `READY`;
the second loaded terrain retained correct collision after a real prior removal.
Both returns recorded `remove-ok`, and final telemetry was cleanly `ABSENT` with
no destination pin, pending action, retention, warning or error. This accepts the
one-client teleport-driven load/unload/re-entry lifecycle. Proximity approach
and two-client independence remain pending.

The subsequent ship-proximity run reached The Trades Challenge at about 1,153 m
from its extracted envelope and produced `request`, exact `asset-ack` and
`add-ok`; the player landed and walked on stable terrain. The island's 15-node
profile was present in inventory, but zero resources checked out even at the
island centre. This was not missing survey data or a placement/radius failure.
After disembark, sparse 1073 relative-position fields stopped advancing and left
the spatial-interest centre at the disembark point, while authoritative global
190602 player transforms continued normally.

The pending server-only correction feeds unparented 190602 world poses into both
resource and terrain interest. It uses the existing sparse parent-state
accumulator before accepting the pose, preventing parent-local coordinates from
entering world interest. Multiplayer validation passes 2,556/2,556 and the
Release server build is clean. Live acceptance still requires deployment after
disconnect, walking across Trades until nearby resources/databanks check out,
then leaving beyond the unload boundary.

## Wall correction

The preserved release MapFile does contain exact wall geometry and wall types.
The closest Haven boundary is a type-5 `WorldEndWall` at x=15943.6523, about
1.061 km west of the active Haven instance; it is not a guessed first WindRift.
Wind/storm-wall simulation is still unimplemented, but its placement evidence
is no longer considered missing. Wall work must use this release dataset and
must not mix in the older closed-beta topology.
