# FINDINGS — THE AUTHORED FIRST HOUR

Reconstructed from 8 `QuestData` ScriptableObjects that still ship inside
`resources.assets`. Everything quoted with `>` is literal shipped string data,
extracted byte-exactly and committed to `data/quests.json`,
`data/quest-conditions.json`, `data/tutorial-content.json`,
`data/knowledge-tree.json`.

**Extraction method and its proof.** Unity 5.6.4p1 ships no MonoBehaviour
typetrees, so UnityPy cannot read these generically. Field order was taken from
the decompiled C#, all 49,349 MonoBehaviours were indexed by
`m_Script -> m_ClassName`, and the blobs walked with a hand-written reader.
Validated by trailing-byte count, which is an exact-layout proof: **8/8 quests ->
0 trailing bytes; 57/57 condition assets -> 0; 45/45 `TutorialContent` -> exactly
16**, precisely the four `[HideInInspector]` fields the C# declares.

## ⭐ THE HEADLINE: YOU DO NOT LEAVE HAVEN BY SHIP

```
100 Escape the crash site -> 101 Craft and Equip a Scanner Tool
  -> 104 Unlock the Reviver network -> 105 Access the Revival chamber -> TELEPORT OUT
  -> [server clears isNewPlayer] -> 110 Learn Shipbuilding -> 111 Build your first ship
side: 120 Torch (8 trigger volumes) - 130 Eat Berries (auto-given at low health)
```

Quest 100 is hardcoded at `Travellers.Quests/QuestManager.cs:71` —
`AddQuestById(100, 0, sendToSpatial: true)`.

**Act 1, on Haven, contains no ship at all.** It ends in an 11-second group
**teleport**, not a departure by sail. Ship building is Act 2, in "Foundation",
and quest 110 step 0 opens `> Welcome to Foundation!` — authored to fire *after*
the teleport.

This answers the question that prompted the research ("how do players get off the
island without resources?"). In the real game they did not build a ship to leave
Haven. They scanned databanks for knowledge, bought one knowledge node, walked to
the middle of the island and teleported out.

## 1. WHAT THE GAME LITERALLY SAID

**100 — Escape the crash site**
1. `> Use your grapple to escape from your ship.` — `UseGrappleCondition`
2. `> Look for an exit.` — `TriggerEnter{1000}`
3. `> Use your <Grappling Hook> to descend safely to the ground` — `TriggerEnter{1001}`

**101 — Craft and Equip a Scanner Tool** (completion: `> Scanner Tool Crafted!`)
- `> You need to build a <Scanner Tool> to start gaining <Knowledge> from the world around you.<br>First test that your <Salvage Tool> still functions.`
- `> <Salvage> metal from <Raw Metal Deposits>. Break away the outer layer to find the <Ore> inside.<br>TIP: Don't forget to use your <Grappling Hook> to reach deposits on the underside of the island.` — "Collect 200 metal", authored as `{"Metal":[100,100]}`, i.e. **two stacks of 100**
- `> Great work, now that you have enough raw metal you can <Craft> a <Scanner Tool> from your inventory.`
- Select `scannerTool` schematic -> `> Drag the metal in your inventory into the schematic's <Component Slots>.` -> `> With the <Component Slots> full, all that is left to do is <Craft>.`
- `> ...<Equip> your <Scanner Tool> by dragging it into its slot on the character tab.` — `ToolUnlocked{Scan}`

The player **starts already knowing `scannerTool`**, and it is *revoked* on
completion — `QuestGivingManager.cs:97-100` calls
`_schematicSystem.Value.UnlearnSchematic("scannerTool")`.

**104 — Unlock the Reviver network** (completion: `> You are now connected to Foundation's Revivers!`)
- `> Now that you have a <Scanner Tool> you'll need to use it to scan <Databanks> to gather <50 Knowledge>. Scan the terrain to know how many databanks can be found in this island.`
- `> Unlock the Revival Chamber Interface in your Knowledge Tree to be able to use the respawner network that will connect you to the rest of Foundation!`

**105 — Access the Revival chamber located at the center of the island**
- `> You can now enter the <Revival Chamber>, head to the middle of the island either solo or with friends that you want to remain close to.` — `TriggerEnter{1002}`
- `> Interact with the platform inside the Revival Chamber to <activate> the Revival Chamber Interface, and teleport - <together with other players on the platform> - to The Wilderness.`

**110 — Gather Knowledge and learn Shipbuilding** (requires 105, and withheld until `NewPlayerState.isNewPlayer == false`)
- `> Welcome to Foundation! Your next step should be to unlock <Shipbuilding>. Find and <scan> more Databanks to gain <25 more Knowledge>.`
- `> Learn <Shipbuilding> from your Knowledge Tree to unlock what you need to start building ships.`

**111 — Build your first ship solo or with a crew.** (completion: `> Well done, time to set sail!`)
- `> All ships start with a frame, which are built at <Shipyards>. Gather the materials to craft a <Shipyard>, or register with one that already exists.<br>TIP: if you want to use another Traveller's Shipyard, you'll need to ask for their Shipyard Code.` — 160 metal
- Craft, equip and place the Shipyard -> `> ...use it to build a <ship frame> - the foundation on which all ships are built.`
- 100 metal -> craft and place an `<Assembly Station>` -> `> TIP: Place your <Assembly Station> within the bubble around your <Shipyard> so that you can pick up what you make with it.`
- `> Now that you have access to an <Assembly Station> you can start building ship parts! Start off by crafting a <Personal Reviver>.` — `CraftShipPart{Respawner01}`
- `> ...<interact> with your <reviver> to register it to your character. This will let you <respawn> on the ship if you die, and it also makes you an <owner> of this ship.`
- `> Feel free to get creative, but all a ship needs to fly is a <Sky Core> to give your vessel some lift, a <Helm> to steer, and a <Sail> to get moving.`

**Minimum viable ship, in the game's own words: frame + Sky Core + Helm + Sail,
plus a Personal Reviver to own it.**

Side quest 120 (Torch) states the crafting rule outright:
`> When all of a schematic's Component Slots are filled with materials, an item can be crafted.`

### Two authoring bugs in the shipped content — do not copy the data
- **Quest 105 step 1** reads "Teleport to Foundation" but its condition asset is
  `ToolUnlocked-Repair`. It completes when the **Repair** tool unlocks, nothing to
  do with teleporting.
- **Quest 110 step 0** says "25 more Knowledge" twice but binds `HaveKnowledge{225}`.

## 2. WHAT UNLOCKS SHIP KNOWLEDGE

Scanning is the faucet; spending is the unlock. The client→server surface is
exactly three messages: `RequestReferenceData` (6908), `ScanEntityEvent`, and
`UseNode{id}` on 1334 `KnowledgeClientState`. `SchematicsLearnerClientState`
(1079) has **no commands at all** — it is pure server push.

The tree (228 nodes / 198 distinct ids / 20 branches; shape baked into the
prefab, server `graphJson` overriding only `knowledgeCost` and `usesToUnlock`):

```
RevivalChamberInterface  TECHNOLOGY  cost 100  <- NO PARENTS, the root
  |__ Shipbuilding       SCHEMATIC_LIST  cost 20
        |__ PhysicalSlots1 50 - Wings 60(x3) - Campfire/Bandages/Stairs/Trunk/AtlasCore 60
        |__ Cannons 90(x3) - SwivelGun 90(x3) - Engines 150(x3)
        |__ Territory Control Tower 5000
```

**The entire non-combat game hangs off two nodes in one order:
`RevivalChamberInterface` -> `Shipbuilding`** — exactly what quests 104 and 110
walk the player through. The eight weapon roots sit outside that subtree.

`Shipbuilding` gates more than schematics: `InteractAgentObserver.cs:448` refuses
**all** crafting-station and shipyard interaction until it is bought. And
`TutorialStep.LEARN_SHIPBUILDING` is enum member **0** — the designers considered
it step zero of the game.

⚠ The prefab says `RevivalChamberInterface` costs **100**, but quest 104 gates on
**50**. The live server could override cost and probably did. Treat live costs as
unknown.

## 3. THE REVIVAL CHAMBER AND THE BARRIER

`AncientRespawner.cs` is six lines: `public const string DisplayName = "Revival Chamber";`.
There is **no `RevivalChamber` class**. It is *not* a personal respawn point —
that is a ship part with `RespawnPointState`. The ancient respawner is world
furniture (6905) you respawn at via a biome-id event resolved by a server app.

The whole Haven gate is three lines, `HavenIslandManager.cs:63-78`:

```csharp
if (!nodes.ContainsKey("RevivalChamberInterface")) return false;
if (nodes["RevivalChamberInterface"] == 0)         return false;
```

The *value* is server-owned (the client holds a Reader on 1332). The
*enforcement* is not: `BarrierWall` is `[WorkerType(UnityClient)]` and
`HavenTeleporterPreprocessor.cs:20-22` **destroys it outright in the server
build**. It was a client-side soft fence.

### Correction: "Gauntlet" is the wrist tool, not the tutorial
`findings-haven.md` states that Gauntlet is Bossa's internal name for the Haven
tutorial run. **The code does not support that.** All 13 occurrences of "auntlet"
across the decompile refer to the player's wrist tool — `"Gauntlet Salvage Mode"`,
`Character_Gauntlet`, `GauntletSlotGroup`. `CheckIfGauntletInterfaceIsUnlocked`
means *"has the Gauntlet's Revival-Chamber interface been unlocked"*, which is
`NodeType.TECHNOLOGY` on that node, whose in-UI string is
`"Gauntlet Revival Chamber Technology"`.

## 4. THE STARTER SHIP WAS BUILT, NOT REPAIRED

`RuinedShipSpawnerPreprocessor.cs` is the only wreck type in the decompile. On
the client it gets `ScannableGUID` and `BlockItemPlacement`, and it owns a child
object of `QuestGivingTriggers`. Combined with quest 100's
`> Use your grapple to escape from your ship`, **the wreck is the thing you wake
up inside, scan, take quests from, and leave behind.**

There is **no repair-the-wreck code path anywhere in the client** — no condition,
no event, no visualizer. `HavenRuinedShipRespawner` as a C# identifier returns
**zero hits** across `acs/`, `gencode/`, `ecs/`, `sdk-decomp/` and
`component-map.tsv`. The first ship is a full build (quest 111) plus a hull
editor covered only by the overlay layer (`LOAD_SHIP -> EDIT_SHIP ->
POST_EDIT_SHIP -> SAVE_SHIP`, backed by `ShipHullAgentState`, 5 hull slots
hardcoded).

**The upside is large: Act 1 needs no ship system at all.** Grapple, salvage, one
craft, scanning, one knowledge purchase, one teleport. Ship Haven and stop at the
teleport and you have a faithful, cheap first hour.

## 5. SHIP SCHEMATICS DO NOT COME OVER 1097

`ShipSchematicsList` has two categories, `_hullsCategoryTransform` and
`_blueprintsCategoryTransform`. Hulls are populated by
`ShipHullAgentVisualizer.cs:70` -> `UpdateShipSchematics(_state.Schematics)` —
that is component **1207 `ShipHullAgentState { List<ShipHullSchematicData> schematics; EntityId editorId }`**,
each entry `{ byte[] data; string name; float beamsLength; int numberOfDecks; string clientSchematicsId; string uuid }`.
`SchematicData.FromShipHullData` force-sets `SchematicType.Ship`. 1097 carries
`schematicsData` for *items*, not hulls.

So the Dinghy / Tug / Skiff / Spear / Skipper list seen in 2017 footage would be
**1207 entries** — server-held saved hull designs whose `byte[] data` is an opaque
hull-geometry blob. **The format is unknown and undocumented anywhere in the
decompile.** That is a hard blocker on any "just push ship schematics down the
pipe we already have" plan.

## 6. A SHIPYARD IS HEAVY SERVER STATE

`ShipyardVisualizer.cs:11-12` holds a **`ShipyardStateReader`**. The client cannot
claim, deploy or register — all of it is server-authoritative. `ShipyardState`
(1205) is:

```
bool active; EntityId dockedShipId; bool deployed; string ownerCharacterUid;
int inactivityTimer; bool initialised;
Map<string,EntityId> registeredPlayersDeprecated; List<string> registeredCharacterUids;
```

`ownerCharacterUid` + `inactivityTimer` is the mechanism behind the 2017 message
"You have taken ownership of this abandoned shipyard", and the 2019 build kept it.
There is a matching generic `InteractiveExpiringOwnershipState { string ownerId; int minutesToExpire }`
(1284) with zero client references, plus `ShipAbandonedBehaviour`
(`[WorkerType(UnityWorker)]`, `IsAbandoned => CoreDampenTime >= 86400`).

Minimum for a claimable shipyard: entity creation (**stubbed**), a 1205 seed and
handler, 1219 `ShipyardVisitorState{shipyardId, code}` per player, 1114
`DockableState`, and the registration command path (**no command channel exists**).

## 7. SHIP PARTS ARE ENTITIES, AND FLIGHT IS SERVER-AUTHORITATIVE

`ShipPreprocessor.cs:44-86` attaches ~25 visualizers to a ship. Parts are separate
entities with their own components — `ShipPartState` (1120), `ShipPartInfoState`
(1119), `ShipPartExclusionRadiusState` (1234), `ShipPartVariationsSeedState`
(1246), `OverheatingShipPartState` (1251) — parented via `ShipRootState` (8066) and
`RelativeParentBehaviour`. You craft an item, then the Shipbuilding Tool *places*
it as a world entity inside the dome.

`Assets.Visualizers/ShipControlVisualizer.cs:16` is
`[WorkerType(WorkerPlatform.UnityWorker)]`. **Ship physics, wind, lift,
self-righting and collision all run on the FSim worker.** Ship flight is
server-authoritative rigid-body simulation, not client-side. This is the single
most decisive cost fact in the question.

## 8. WHAT OUR SERVER SUPPORTS TODAY

A structural blocker dominates everything: the Reborn server speaks a **5-channel
protocol with no command channel** (`enetLayer.h:16-20`), and
`SendCommandRequest` / `SendCreateEntityRequest` /
`RegisterCommandRequestCallback` are all `// TODO` stubs returning 0
(`Exports.cpp:157-189`, `:87-94`). SpatialOS commands are the exact mechanism
quests, scanning, node spends, harvest and shipyard registration used. Only
**four** component-update handlers exist server-wide.

| Step | Ours | Evidence |
|---|---|---|
| 1 Spawn on Haven | **PART** | `SpawnPolicy.cs:62,92-93,120-121`; 8055 hardcoded `isNewPlayer=false` |
| 2,4 Grapple / climb | **YES** | client physics; server relays 1098 |
| 3 Quest triggers and state | **NO** | 8053/8054 no seed, no handler; zero `Quest` hits server-side |
| 5,6 Salvage / metal deposits | **NO** | no node entities possible |
| 7 Craft the scanner | **NO** | handler is three `Console.WriteLine`s; catalogue is one fake `"glider"` |
| 8 Equip tool | **NO** | `equipTool` logs only; 8051 hardcoded `ToolStateData(30)` |
| 9,15 Scan -> knowledge | **NO** | 1330/1331/1334/1307 have 0 branches |
| 10 Buy a knowledge node | **NO** | as above |
| 11 Barrier drops | **YES (n/a)** | client-only; the server never had it |
| 12,13 Revival Chamber + teleport | **NO** | 8052/8056/8070/6905/1046 all 0 seed branches |
| 14 `isNewPlayer -> false` | **NO** | no handler can flip it |
| 16,18 Place shipyard / station | **NO** | `SendCreateEntityRequest` returns 0 |
| 17,19,20,21 Frame, parts, reviver, flight | **NO** | 1207 empty, 1219 seeded `"abcdefg"` |
| Equip wearable | **YES** | `InventoryModificationState_Handler.cs:26-73`, real logic, not persisted |
| Inventory contents | **PART** | fixed 7-item seed, read-only |
| Respawn / death | **NO** | 1077 seeded 200/200, never updated |

**Live risk worth checking:** `SendOPHelper.cs:85-94` returns false on
unserializable components and all four interest call sites pass
`failOnComponentInitError: true`. If the client ever requests 8053/8054/1334 the
**entire** `AddComponent` batch is dropped, not degraded. Grep a real server log
for `[error] failed to initialize component`.

## 9. COST RANKING

1. **Teleport (190607) — cheapest by a wide margin, and it is the authentic Haven
   exit.** The authored tutorial ends in a teleport, not a ship. No ship entity,
   no physics, no shipyard state. It buys the entire Act 1 arc with zero ship
   work. **Recommended.**
2. **Spawn a pre-built flyable ship — expensive.** Needs entity creation (stubbed),
   the full ~25-component ship entity, and server-side flight physics.
3. **Shipyard plus the client UI — most expensive.** Everything in (2) plus
   claimable/damageable/dockable shipyard state, plus a hull `byte[]` format we
   cannot synthesise. The 2017 footage is authentic but points at the costliest
   path.

## NOT VERIFIED — do not build on these

1. **The localisation values are lost.** `LocalizationSchema.cs` has 60+ keys
   including `TUTORIAL_START_POPUP_SCANNING_TITLE/MESSAGE` and
   `TUTORIAL_START_POPUP_SHIPBUILDING_*` — very likely the first thing a new
   player read. All 126 TextAssets across `resources.assets`, `sharedassets0/1`
   and `globalgamemanagers.assets` were enumerated: **no localisation table
   ships.** It was downloaded. Recoverable only from video, wiki or community
   archive.
2. **Live knowledge costs** — `graphJson` overrode them; section 2 gives prefab
   defaults.
3. **The item crafting catalogue** — `schematicsData` was a server blob. What a
   Shipyard or Assembly Station actually cost in materials is unknown.
4. **Haven geometry** — trigger volumes 1000/1001/1002 and the wreck, databank,
   deposit and chamber placements. The Haven prefabs ship in no bundle; this
   remains the biggest blocker.
5. **Hull `byte[]` format** — unknown, undocumented.
6. **30 of 228 knowledge nodes have duplicate ids** (e.g. `WingsGlyph1` x6).
   Either they span multiple prefabs or branch attribution is off. The spine is
   unaffected; treat leaf attribution in `knowledge-tree.json` as provisional.
7. `ConsumeItemConditionData` did not parse to zero trailing bytes.

## CONTRADICTIONS WITH EXISTING DOCUMENTS

- **`findings-haven.md:142-143`** lists `HavenAncientRespawner`,
  `HavenRuinedShipRespawner`, `Barrier_Wall` and `RevivalChamber` as furniture. As
  **C# identifiers these return zero hits**. The real names are
  `AncientRespawner`, `RuinedShipSpawner(Preprocessor)`, `BarrierWall` and
  `TeleportHelperBehaviour`. They are probably prefab asset names — do not grep
  for them as types.
- **`findings-haven.md:101`** says `HavenIslandManager` lives on the
  `HavenAncientRespawner` prefab. More precisely it lives on the prefab carrying
  **8052 `HavenTeleporterState`** — the teleporter; `AncientRespawnerActivation`
  is a child (`HavenTeleporterVisualizer.cs:20`).
- **8070 `HavenRespawnerIslandReferenceState`** is absent from the findings and
  looks like the Haven↔island binding. Zero client references.
- **`findings-progression.md:3`** cites `ComponentsSerializer.cs:491` for
  `ToolStateData(30)`; it is now `:558`. The reasoning holds, the line drifted.
