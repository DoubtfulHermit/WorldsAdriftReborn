# FINDINGS — RESOURCES / ENVIRONMENT

(Recorded by the orchestrator; the agent was blocked from writing this file.
Its late self-correction is folded in at the top, since it retracts the MVP.)

## LATE CORRECTIONS — read these first
1. **The Tree MVP is RETRACTED.** `acs/TreeFsimVisualizer.cs:12` is
   `[WorkerType(WorkerPlatform.UnityWorker)]` — **FSim-only**. `TreeSection.Harvest()`
   never runs on a client, so a client-only server cannot harvest a tree without
   reimplementing the cutting sim. Trees stay the cheapest thing to *spawn*
   (2 components), but not to *harvest*.
2. **The real first blocker: the multitool beam cannot fire at all today.**
   `PlayerMultitoolVisualizer` and `SalvagerAimerObserver` `[Require]` **writers** for
   2105 `MultiToolPlayerState`, 2106 `MultitoolSalvagerState`, 2002 and
   1231 `SalvagerAimerState` — **none are seeded in `ComponentsSerializer` nor present
   in `authoritativeComponents`** (`WorldsAdriftRebornGameServer.cs:344`). Unresolvable
   `[Require]` writers mean those visualizers never enable. Prerequisite for ANY
   harvesting, whatever the node type. Also `maxBoltDistance` must be server-seeded on
   1231 or the client's range check is 0 and nothing is ever a valid target
   (`SalvagerAimerObserver.cs:101`).
3. **Revised MVP: a generic salvageable material node**, not a tree —
   `1099 SalvageAndRepairState` (yield + rates) + `1016 ItemHealthState` (the depletion
   counter, with an `ItemDeath` event) + `190602`.
   `MaterialSourceVisualizer : Salvageable` needs only 1099 (`Salvageable.cs:9-12`).
4. **Inventory add is unconditionally server-authored** (strengthened).
   `InventoryModificationStateData` (1082) has **zero data fields** and no `AddItem`
   event — a pure client→server request bus. No `InventoryStateWriter` exists anywhere
   in the client. A harvest grant = the server pushes a full replacement
   `inventoryList` on 1081. **Bonus:** the "+N Iron" collect SFX is derived by the
   client *diffing* successive 1081 lists (`InventoryContents.cs:521-570`) — free.
5. **No SpatialOS commands exist on any harvesting/inventory/interact component** —
   every `ICommandReceiver` there is empty. The whole chain is `COMPONENT_UPDATE_OP`,
   the one op the server already handles.
6. **The relay must be node-scoped.** The existing `RelayToOtherPlayers` routes through
   `RemotePlayerMirror`, which only knows player entities — node state updates
   (1016/1099/12283/2103) need a separate per-peer send to the *node* entity id.
7. **Depletion detail worth keeping:** metal crust persists `shotPoints` as replicated
   *state*, so late joiners replay damage correctly
   (`MetalDepositCrustVisualiser.cs:91-103`). No client path ever destroys the node
   GameObject — confirming entity removal is not needed here.
8. **Newly flagged unknowns:** the damage→yield formula is **unrecoverable** (the GSim
   was Scala; no "harvest" match in `ecs/` or `wasys/`) and must be invented.
   `RawMaterialSourceState` (1030), `HarvesterState` (1031) and
   `SalvageProgressEvent`/1100 have **no client readers** in this build — likely
   vestigial; don't build feedback on them.

## Q1 — Nodes are SpatialOS ENTITIES, not baked decoration. Settled.
- `gencode/Bossa.Travellers.Islands/IslandResourceSpawnerStateData.cs:26` —
  `List<EntityId> spawnedMetalDeposits`. The island tracks deposits as entity ids;
  baked geometry cannot have one. The same struct holds only *parameters*
  (`metalDepositDensity`, `initialMetalRockDeposits`, `metalOnSurfaceProb`, `:10-24`)
  — never positions.
- Nodes cross-reference by `EntityId`: `MetalRockStateData` (`islandId`,
  `surfaceNuggets`), `MetalRockCoreStateData` (`depositId`), `MetalDepositStateData`
  (`coreId`).
- The client cannot render a tree without server-replicated components:
  `acs/TreeClientVisualizer.cs:12-16` `[Require] TreeStateReader` + `TreeFSimStateReader`.
- Counter-evidence properly bounded: islands DO carry **465,571 baked decorative
  props** (`static_objects` TextAssets), but **zero harvestables** — `IslandProps/Trees`
  = 0 instances, no loot chests, no databanks.

**Ids:** 1010/1011 island spawner, 1030 RawMaterialSource, 1031 Harvester,
1032 MetalRock, 1035/1036/1037 Tree/TreeFSim/TreeCutter, 1099 SalvageAndRepair,
1174 Salvageable, 1231 SalvagerAimer, 1255 MetalDeposit, 2101/2103/2106, 12283.

## Q2 — Prefabs, verified in the shipped client
`acs/.../ResourcesGameObjectLoader.cs:34` → `Resources.Load("EntityPrefabs/" + name)`;
only `Traveller`/`ModalErrorPopup`/`Spectator` get an `@Context` suffix
(`DispatchEventHandler.GetPrefabNameWithContext`), so resources use the **bare name**:
send `("Tree","Default")`, `("MetalDepositCore","Default")`,
`("MetalDepositScrap","Default")`. Confirmed present in `resources.assets`
(149 hits for `EntityPrefabs/Environment/Tree*`).

## Q3 — Positions exist NOWHERE locally. The CLIENT generates and offers them.
This inverts the problem. `acs/IslandProxyVisualizer.cs` (the only file touching it)
implements a handshake:
1. Server triggers event `SpawnResources{int number; IslandResourceType}` on **1010**
   (`:58`, `:142-152`) — **a count only**.
2. Client raycasts its own island meshes (`IslandSurfaceData.FindPlace`, random LOD0
   vertex + normal filter) (`:201`).
3. Client replies on **1011** with `SpawnResourcesReply{List<SpawnResourceRequest>}`,
   each a `FabricTransform{position, rotation, scale, metadata}` + variant (`:216-231`).

**The agent disproved its own alternative hypothesis.** `acs/HarvestableEntity.cs`
(a marker with `entityPrefabName`/`harvestableMaterialName`/`harvestableAmount`) has
zero consumers — and an exhaustive scan of **all 255 bundles** found it **0/255**
(MonoScript enumeration + raw byte search, positive controls passing). Stripped at
build. Nothing in bundles / StreamingAssets / GameDB (2 KB) holds spawn tables.

**Caveat that changes the MVP:** only `Metal` and `Egg` are handled in
`OnSpawnResources` (`:144-151`). **Trees have no client spawn path** — they were
server-placed. Useful trick: the reply returns *generic valid surface transforms*, so
the server can request `Metal` and then spawn any entity at those points.

## Q4/Q5 — Harvest chain and depletion
Aim is **already published by the client**: `SalvagerAimerState` (1231, player-side
**writer**) = `{EntityId lookingAt; Coordinates lookHitPoint; float maxBoltDistance}`
(`acs/SalvagerAimerObserver.cs:15,123-127`). Yield lives on the node:
`SalvageAndRepairState` (1099) = `itemTypeId`,
`originalMaterials: List<SlottedMaterial{RawMaterial, amount}>`,
`destroyOnSalvageComplete`, `period`.

Depletion is **state-based**: `amount/maxAmount` (1030), `isDestroyed` (1032/2103),
`exploded` (12283), `respawnTime` (1035). **Therefore entity removal is NOT a
blocker** — the missing `RemoveEntityOp` (`WorldsAdriftRebornGameServer.cs:50-55`)
blocks player despawn but not node depletion. That is what makes this milestone
reachable now.

Inventory grant is **server-authored**, confirmed in
`InventoryModificationState_Handler.cs:34-51`: the server dereferences live components
from `GameState.ComponentMap[peer][entity][id]`, mutates, and calls
`SendComponentUpdateOp`. Item factory:
`ItemHelper.MakeItem(id,"oak",x,y,amount,quality)` (`ItemHelper.cs:72`; ids <100
reserved). The server links `Generated.Code.dll` and builds vtables from the full
`MetaclassMap`, so **any** of the 443 components can be authored.

## Q6 — Minimum viable slice
The salvage tool is **already in the default loadout** (`gauntlet_salvage`, hotbar 0,
`ItemHelper.cs:141`), and wood/metal items already exist in `itemData.json`
(`oak`…`palm`; `iron`…`eternium`).

- **P0** Make `ComponentsSerializer` per-*entity*, not per-component-id — today
  TransformState is hardcoded `(0,100,0)` (`:59`). Copy the `AppearanceStore`
  side-table precedent (`:109`). Spawn one node at a chosen coord; seed only the
  minimum components (rule 7).
- **P1** Grant **one** client authority over 1011; author 1010; fire `SpawnResources`;
  add an 1011 handler iterating `Update.spawnResourcesReply` — structurally identical
  to the 1082 handler's `equipWearable` loop that works today.
- **P2** Grant 1231/2106 (plus 2105/2002, see correction 2); on beam engage decrement
  1099/1016 and grant the item via the existing 1081 path.

## RISKS
Every client runs `IslandProxyVisualizer`, so **multiple 1011 writers would duplicate
the world** — grant exactly one. The asset-ack race (already flaky at 2 players)
worsens with many spawns. Metal deposits are a **4-entity graph** — don't start there.

## UNVERIFIED
Exact tree harvest component (1099 vs tree-specific); the original `SpawnResources`
cadence; whether trees really carried 1099. Nothing was executed — all static analysis.
