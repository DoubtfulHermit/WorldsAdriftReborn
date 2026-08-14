# Island pipeline audit: PR 1 island identity

Date: 2026-08-14

## Authoritative Haven facts

The current server has one authoritative Haven placement. `SpawnPolicy` identified the
terrain as `1431299145@Island` and seeded its first `190602 TransformState` at fixed-point
`(69650145, -1305269, -4645549)`. The comments trace that position to Haven instance #5 in
`docs/research/world-data/wamap-islands.json`. `WorldEntities.Island` sent the same asset,
the literal asset context `notNeeded?`, and that position as the first `BeforePlayer`
world entity. No conflicting production origin or asset/context was found.

PR 1 preserves those facts in `Islands/IslandCatalog.Haven`. `SpawnPolicy` keeps its old
public names as compatibility aliases, so player spawning and existing policy callers do
not change. Terrain startup now constructs the Haven entity from the definition obtained
from `IslandRegistry`.

## Existing one-island assumptions found

- `WorldEntities.Island` and `WorldEntities.Default` have one legacy terrain registration
  key (`EntityIdAllocator.IslandKey`) and place it before the player. PR 1 routes Haven's
  definition through `IslandRegistry` but intentionally does not invent ordering or entity
  keys for a second terrain entity.
- `SpawnPolicy.PlayerSpawnPosition`, `FallPolicy.RearmY` and `TeleportPolicy` describe one
  Haven spawn/rescue destination. Player spawn is now derived exactly from Haven local
  `(208, 6.70, 4)`; fall and teleport policy otherwise remain Haven-specific.
- `MetalNodes`, `MetalDeposits`, `FuelPods`, `Databanks`,
  `WorldEntities.DistributedTrees`, and `Resources.HavenSurface` contained direct uses of
  the singleton origin or its old conversion helper. Their production placement paths now
  use `IslandCatalog.Haven.LocalToGlobal`. Compatibility `IslandOrigin` members and
  `MetalNodes.IslandLocalToWorldFixed` remain for callers/tests, with identical arithmetic.
- `IslandBounds.Haven` is a named Haven surface safety box and now takes its origin from
  the Haven definition. The measured local AABB remains Haven data and is not generalized.
- `ResourceInterestService.ObserveIslandLocalPosition` interprets client component 1073
  in the active Haven coordinate frame. It now reads the Haven definition's origin, but
  the service still has no per-player active-island identity. The aboard-ship conversion
  in `ClientAuthoritativePlayerState_Handler` has the same intentional constraint.
- `WorldEntities.GlobalEntity` uses the island origin only as a harmless parking position;
  it now reads Haven's definition. Its biome Voronoi centres are already global data.
- `WorldEntityRegistry.KindOf` and `SeededEntityKind.Island` distinguish terrain entities
  by the shipped `@Island` asset contract, rather than by Haven's legacy key. They still
  identify a kind, not a stable `IslandId`.
- Placed deployables and loose parts use client-reported/persisted positions. They are not
  Haven population tables and were deliberately not rewritten.
- `WorldEntities.ProofIsland` is an opt-in development copy behind
  `WAREBORN_SPAWN_PROOF_ISLAND`. It is not treated as a supported second island: it has no
  distinct population, spawn, interest, rescue, or stable island definition.
- `ComponentsSerializer` retains `SpawnPolicy.IslandAssetName` as a compatibility fallback
  when serializing the legacy island component. Because the alias points to the catalog,
  its wire value is unchanged.

## Exact coordinate preservation

The old placement formula was:

```text
global fixed axis = encoded origin axis + (long)(local metres * 4096)
```

The cast truncates each local axis toward zero before addition. The new
`IslandDefinition.LocalToGlobal` deliberately performs that same operation; it does not
sum decoded metres and re-encode them. Regression tests compare the old formula with the
registry-derived result for every deterministic Haven nugget, deposit, fuel pod, databank,
and distributed-tree placement, plus pinned player/tree examples and the exact origin.

## Assumptions removed in PR 1

- Island identity no longer depends on a lazily allocated world entity id or a WAMap row.
- Haven's origin, terrain asset, display name and context have one definition.
- Known islands can be registered, looked up by stable id, rejected on duplicate id, and
  enumerated deterministically independent of registration order.
- Haven terrain startup and deterministic resource coordinate conversion consume that
  definition instead of owning separate origin literals.

## Assumptions intentionally remaining

This PR does not choose how several terrain entities are ordered in the spawn barrier, how
their world-entity keys are encoded, which island a player currently occupies, how interest
crosses between islands, or which rescue/spawn belongs to which player. It also does not
assign the opt-in proof terrain a production identity. Those choices would change runtime
semantics and have no evidence from a real second supported island.

## Evidence gate for island #2 (cleared by PR 3)

Before registering a second supported island, recover and verify its exact client terrain
asset/context and authoritative global placement; establish its collision/surface bounds;
recover or explicitly define its resource/population profile; decide a stable, collision-free
world-entity key and spawn-chain order; and capture how the client reports active-island/local
coordinates during a crossing. Spawn, fall rescue, teleport, placed-object persistence and
resource interest then need explicit island selection. The terrain identity/asset/position,
surface bounds, key and spawn order are now pinned for The Trades Challenge in
`findings-second-island-pr3.md`. Population, player island selection, rescue and interest
remain intentionally deferred until after visual terrain acceptance.

## Manual acceptance status

The automated suite proves component/spawn registration order and all deterministic positions
remain unchanged. Live client acceptance (login, terrain, streaming/harvesting, placed objects,
ship flight and reconnect) is still required at deployment time; PR 1 itself does not deploy,
restart, or alter the client protocol.
