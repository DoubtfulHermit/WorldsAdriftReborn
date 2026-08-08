# FINDINGS — THE FIRST SHIP

Islands sit ~3 km apart in a 36 km world and everything within 4 km of Haven is
*above* it, so gliding off is geometrically impossible. Ships are the only
transport. How did a first ship work, and what is the minimum to reproduce it?

## VERDICT IN SIX LINES

1. A ship is **N+1 SpatialOS entities**: one procedural hull root (prefab
   `ShipFrame`) plus one entity per bolted-on part (`Helm01`, `CoreMain`,
   `ModularEngine`, …), linked by **8066 `ShipRootState`**.
2. It was **BUILT, not salvaged.** The client's own tutorial enum settles it.
3. **We can skip construction entirely.** A visible, walk-on, collidable ship is
   `AddEntity("ShipFrame")` plus **four** seeded components, one of which is a
   39-byte hull blob this document shows how to synthesise. **Zero client patches.**
4. Position authority in the original was **always the FSim worker, never the
   client** — the shipped client hull prefab has no transform-writing path at all.
   But the *receive* half is complete on that prefab, so **our server can drive a
   ship itself by publishing 1130 control points**, also with zero client patches.
   Player-piloted flight is the expensive variant; flight itself is not.
5. The smallest thing that gets a player off Haven is a **server-driven ferry** —
   roughly a day of server work, no client patch, no physics engine.
6. That is more than `190607` teleport (hours) but it is the only one of the two
   that is *the game*. And teleport is cheaper than previously assumed: the
   parentless path needs **no new authority grant at all**.

## CONFIDENCE KEY

Nothing here was executed against a running client or server.
**VERIFIED (source)** — read in the decompile, cited `file:line`.
**VERIFIED (assets)** — *measured* out of the shipped Unity containers with
UnityPy; reproducible from the scripts in `data/`.
**INFERRED** — follows from verified facts but has an unexamined step.

One strong anchor: the shipped `Traveller@Player_unityclient` root component list
extracted from `resources.assets` is **byte-for-byte identical, in the same
order**, to the list our own mod logged in a real session. The asset census is
therefore not a guess about what the client does — it is what the client
demonstrably did.

## THE STARTER SHIP WAS BUILT, NOT REPAIRED — HIGH CONFIDENCE

### 1. The tutorial script is a construction script
`acs/Travellers.UI.Tutorial/TutorialStep.cs`, in authoring order:

```
LEARN_SHIPBUILDING, FIRST_REVIVAL_CHAMBER, GRAPLING_HOOK, CLIMBABLE_SURFACE,
CLIMBING, LEAVE_REVIVAL_CHAMBER, LOAD_SHIP, EDIT_SHIP, POST_EDIT_SHIP,
ENTER_SHIP_BUBBLE, EQUIP_SHIP_BUILDER, PLACE_SHIP_PART, EQUIP_SCANNER, FLY_SHIP,
... SAVE_SHIP ...
```

Every step has a live firing site in our build — `LEARN_SHIPBUILDING` at
`KnowledgeManagerScreen.cs:754,758`; `LOAD_SHIP`/`SAVE_SHIP` at
`ShipCraftingUIHelper.cs:269,321-333`; `POST_EDIT_SHIP` at
`MeshEditorMoveGizmo.cs:35`; `ENTER_SHIP_BUBBLE` at `ShipyardDomeTrigger.cs:444,464`;
`EQUIP_SHIP_BUILDER` at `HotBarScreen.cs:62,265`; `PLACE_SHIP_PART` at
`PlayerScannerTool.cs:589,699`; `FLY_SHIP` in `PilotVisualizer`.

**There is no `REPAIR_SHIP`, no `RECLAIM_WRECK`, no `SALVAGE_HULL` step anywhere.**

### 2. "Reclaim" in our build means SCRAP, not repair
`ReclaimableState` (1259) has one field, `int timeTillReclaim`, and its only
consumer *destroys* the ship — `acs/ShipReclaimVisualizer.cs` dissolves the beams
with a materialiser override, calls `HackDespawn` on each child part entity and
disables every collider. That matches the RECLAIM button in the 2017 shipyard
panel: reclaim your docked ship = dissolve it back into materials. It does not
support "reclaim a wreck into a working ship". **Where the 2017 footage and our
client disagree, ours wins, and here they disagree.**

### 3. `HavenRuinedShipRespawner` is scenery
**VERIFIED (assets).** Its full root component list:

```
Transform, TransformNature, TransformOffsetsRegistry,
TransformParentHierarchyBehaviour, TransformChildHierarchyBehaviour,
StaticGlobalTransformBehaviour, StaticLocalTransformBehaviour,
ScannableGUID, BlockItemPlacement
```

No `Ship`, no `MeshGenerator`, no `ShipVisualizer`, no `SSPDeadReckoningVisualizer`.
It is a **static, scannable prop you cannot place items on** — the hulk you wake up
inside. `RuinedShipSpawnerPreprocessor.cs` corroborates: on the client it adds only
`ScannableGUID` and `BlockItemPlacement`, and its serialized fields are `Triggers`,
`QuestGivingTriggers`, `FeedbackTriggers` — quest-giving set dressing.

### What the 2017 footage *does* corroborate
- **The shipyard is a claimable world object** — matches `ShipyardState`'s
  `ownerCharacterUid` + `registeredCharacterUids` and the `Unlock` flow
  (`LockAgentVisualizer.cs:56-59`); the on-screen "SHIPYARD CODE 1781" is
  `ShipyardVisitorState.code` (1219). **A shipyard could be found rather than
  built — genuinely cheaper as a starting point.**
- **The dome** — `Shipyard.ImpactRadius = 35f`, `ShipyardDomeTrigger`.
- **Weight / Complexity %** maps to 1257 `ParentingMassAdderState` vs 1258
  `ShipLiftState`; the overload rule is `ShipLiftVisualizer.cs:18`.

## WHAT A SHIP IS

### Many entities, not one
**8066 `ShipRootState`** = `{Option<EntityId> shipRoot; bool isRoot;}`, read at
`ShipPartVisualizer.cs:96-103`. No `Trigger*` methods — server-written only.

### The hull root
Prefab **`ShipFrame`**, carrying `MeshGenerator`, `Ship`, `ShipVisualizer`,
`CustomShipFrameVisualizer`, `SSPDeadReckoningVisualizer` and a kinematic root
`Rigidbody`. The geometry is **not a prefab** — it is generated at runtime from an
opaque `byte[]`:

```csharp
// acs/CustomShipFrameVisualizer.cs:50-52
ShipPlan plan = new ShipPlan();
plan.Load(array);
_meshGenerator.GenerateShipMesh(plan, 2f, _salvageAndRepairState.OriginalMaterials);
```

`CustomShipHullState` (**1209**) is one field, `byte[] hullData`. Colliders are
real and generated with the mesh (`MeshGenerator.cs:400-401`, `convex = true`).

### Flyable vs pilotable — two things, two entities
**Flyable** (the hull moves and everyone sees it) is **1130 `SSPPredictedMotionState`
+ 190602**. `SSPDeadReckoningVisualizer` requires exactly those two and is the only
thing on the shipped client hull prefab that can move it.

**Pilotable** is a `Helm01` *part* entity carrying **1210 `InteractiveState`** and
**1111 `ShipControlInput`**. The verb is baked in at `HelmPreprocessor.cs:18`:
`AddComponent<InteractiveObjectVisualizer>().SetVerb(InteractVerb.Man)`.

### The player↔ship relationship is NOT parenting
1. **1109 `PilotState` and 1207 `ShipHullAgentState` live on the PLAYER entity**,
   not the ship — our server already injects both onto the player for that reason.
   `PilotStateData` is a pointer *from* the player *to* the vehicle.
2. **A player standing on a deck is attached by 1073, not `TransformState.Parent`.**
   `ClientAuthoritativePlayerMovement.cs:297-328,336-353` sets `relativeTo` /
   `positionRelative` / `relativeBias = 1` from whatever the player stands on, and
   `relativeBias > 0.5` **wins outright** over the Parent branch on restore.
3. `PlayerShipParentingVisualizer` — **the name is a trap** — does no parenting. It
   is `[WorkerType(UnityWorker)]`, absent from the shipped client player prefab,
   and exists only to answer "is anyone aboard?" for the abandonment timer.

## HOW A SHIP WAS BUILT

A shipyard was mandatory, by three independent locks: the hull editor exists only
on the shipyard entity (`ShipHullEditorVisualizer` requires 1206 *and* 1205 on the
same GameObject); part placement refuses outside the dome
(`PlayerScannerTool.cs:816-824`, *"This item can only be placed inside a ship yard
dome."*); and `TryPlace` unconditionally attaches to `Shipyard.DockedShip`.

The wire protocol is **all component-update events — not one SpatialOS command**:

| step | component | event |
|---|---|---|
| unlock the yard | 1221 | `Unlock{EntityId lockId, string code}` |
| open the editor | 1208 | `StartEditingSchematic{editorId}` |
| load / save / rename | 1208 | `LoadSchematic`, `SaveSchematic`, … |
| edit | 1208 | `UpdateShip{beamsLength, numberOfDecks, byte[] data, editorId, id}` |
| feed materials | 1270 | `AddItem`, `AutofillBlueprint`, `ReturnItem` |
| craft | 1270 / 1003 | `StartCrafting{targetEntity}` |
| bolt on a part | 1070 | `PlacePart{parentId, shipId, …}` |

Two properties matter for us:
- **Hull edits are the whole blob on a 3-second heartbeat, not deltas** — all
  gizmos mutate a local `ShipPlan` and the only send site is
  `ShipHullAgentVisualizer.SendUpdate` gated on `_lastSend > 3f`. A design is a
  *value* we can store and replay verbatim.
- **Every 1208 event carries a monotonic `int id` and the server MUST answer.**
  `SendRequest` parks an `Action<bool>` in `_pendingReplies[id]` and nothing else
  clears it. Same latch shape on 1274 `GsimShipBlueprintInteractionState.busy` —
  **fail to clear it and the blueprint UI hard-locks after the first click.**

### "Zero shipframe hits in any manifest" is good news, not a blocker
Ship prefabs are not streamed per island; they are baked into `resources.assets`
(always resident) and pre-split per worker at build time. Re-measured here:
`data/prefab-names.tsv` lists all four `ShipFrame*` prefabs plus `Shipyard`,
`Helm01`, `CoreMain`, `ModularEngine`, `Sail01`, `Deck01`, each in **both**
`_unityclient` and `_unityworker` variants. `AddEntityOp(id, "ShipFrame", …)`
resolves — context is ignored for every prefab name not starting with `Traveller`,
`ModalErrorPopup` or `Spectator` (`DispatchEventHandler.cs:342-344`).

## CAN WE SKIP CONSTRUCTION AND JUST SPAWN A SHIP? **YES**

### The client hull prefab demands four components

| component | id | unlocks |
|---|---|---|
| `TransformState` | 190602 | position; `SSPDeadReckoningVisualizer` |
| `SSPPredictedMotionState` | 1130 | `SSPDeadReckoningVisualizer` → `PathFollower` |
| `CustomShipHullState` | 1209 | `CustomShipFrameVisualizer` → mesh + convex colliders |
| `SalvageAndRepairState` | 1099 | also required by `CustomShipFrameVisualizer` |
| `TransformHierarchyState` | 190601 | only once you bolt parts on |

Everything else on the prefab stays disabled, which is the shipped default — every
`*Visualizer` ships at `m_Enabled = 0`.

### The hull blob, synthesised
**VERIFIED (source); the transcription itself is NOT EXECUTED.**

```
int16  cellCount
per cell: int16 cellNumber, int16 deckNumber, ShipSection Front,
          bool hasBack, [ShipSection Back]
ShipSection = Top[0],Top[1],Bottom[0],Bottom[1] : ShipVertexVec + CurvePoints[2,2] : sbyte
ShipVertexVec = sbyte x(range 16), y(range 1.7), z(range 2)
SerializeFloat(v, range) = (sbyte)round(clamp(v/range,-1,1) * 127)
```

Section = 16 bytes, cell = 21 or 37. `ShipPlan.MakeDefault()` is `AddCell(0,0)`, so
**the smallest legal ship is 39 bytes**:

```
hex    010000000000e80000180000e800001800000000000001e80000180000e8000018000000000000
base64 AQAAAAAA6AAAGAAA6AAAGAAAAAAAAAHoAAAYAADoAAAYAAAAAAAA
```

`data/make_hulldata.py` generates this plus a 3×1 (81 bytes) and 3×2 two-deck (160
bytes) variant. **`ShipPlan.Load` throws on a null or zero-length array, so a
malformed blob is an exception, not a silent no-op — test it early.**

`SchematicData` has a `hullData` field carried as **Base64** and a
`SchematicType.Ship = 2`, so the same 39 bytes can ship either as a 1209 seed
(server-spawned) or as a schematic over 1097 (player-crafted).

## FLIGHT AUTHORITY

### In the original: the FSim, always
The publisher is gated at prefab-export time, not by attribute —
`ShipPreprocessor.cs:108-112` adds `SSPDeadReckoningBehaviour` only on the
UnityWorker branch. The split is visible in the shipped assets:
`ShipFrame_unityworker` has `SSPDeadReckoningBehaviour`, `ShipControlVisualizer`,
`WindPhysicsVisualizer`, `SelfRighteningVisualizer` and the rest;
`ShipFrame_unityclient` has **none of them**.

The chain: player writes `TriggerInteractWithObject(helmId, Man)` on its own 1211
→ a server worker writes **1109 PilotState** on the player (there is **zero**
`PilotStateWriter` in the client) → the piloting client writes **1111
`ShipControlInput`** at 20 Hz, `{Vector3f shipAxes, float vertical, float throttle}`
— **four numbers, the entire client influence over a ship** → the server resolves
1111 into 1113 → the FSim turns 1113 + 1258 + 1257 into forces → publishes 190602 +
1130 → everyone replays through `PathFollower`.

**1113, 1115, 1116, 1257, 1258 have no client-side writer at all.** The client is a
joystick, not a physics engine.

### For us: the cheap option is server-authoritative
`SSPDeadReckoningVisualizer.cs:73` does
`PathFollower = PathFollower ?? gameObject.GetOrAddComponent<PathFollower>()` —
**`PathFollower` is added at runtime**, so the census line "PathFollower 0
instances" is accurate and *not* a blocker.

**Option A — server drives the ship (recommended).** The server publishes 1130
control points; every client's visualizer feeds them to `PathFollower`, which does
`MovePosition`/`MoveRotation`/`velocity` on the kinematic hull rigidbody.
**No client patch, no physics simulation** — a control point is just
`{timestamp, position, rotation, velocity}`. Constraints, all verified:
- **Cadence.** `ControlPoint.ValidateControlPoints` drops any point closer than
  `SendInterval * 0.95 = 0.228 s` to its predecessor. Emit at ~0.24 s, never
  faster, and send reliably.
- **`fsimIdHash` must be constant and non-colliding.** A change triggers
  `IgnoreControlPointsUntil(t + 0.5)`; a collision with a client's own hash makes
  it drop points silently. `WorkerId` is a fresh GUID per process, so any fixed
  value is safe.
- **Timestamps are NTP wall-clock with a knowable epoch** —
  `SynchronisedTime.EpochTime = 2018-03-01T00:00:00Z`,
  `ToMillisecondsSinceEpoch(t) = round(t * 1000)`. No clock negotiation needed.

**Option B — pilot-client-authoritative.** Grant 1130 + 190602 on the ship to the
piloting client and inject the FSim behaviour stack into the client hull prefab at
runtime (~17 components Tier 1, 5 Tier 2). Also requires synthesising
1113/1116/1258/1257. Real, but a week with a long tail.

### The one thing I could not settle: does the player get carried?
**NOT VERIFIED, and the highest risk here.** There is no explicit carry code.
`PlayerMove.cs:2345-2346` records
`movingObjectVelocity = raycastHit.rigidbody.GetPointVelocity(point)` and uses it
**only** for animation and impact damage; the sole physical effect is a yaw match.
Carrying therefore relies on PhysX friction between the player's dynamic rigidbody
and the hull's kinematic `MeshCollider`s. **Test it before building on it**: spawn
a static ship, stand on it, send one 1130 update moving it 5 m, watch.

## BUILD ORDER

1. **Generalise entity spawning** — extend the spawn state machine and
   `ComponentsSerializer`/`SpawnPolicy` from `{Island, Player}` to a third kind.
   Shared with crafting-station work; do it once.
2. **Spawn a static ship** — `AssetLoad("ShipFrame")` → `AddEntity` →
   `AddComponent{190602, 1209, 1099, 1130}`. **Gate:** a ship is visible and you can
   walk on it. First real test of the blob transcription.
3. **Prove the carry** — one 1130 update translating the ship a few metres.
   **Gate:** the standing player moves with it. If this fails, stop.
4. **Make it a ferry** — a path publisher emitting a control point every 0.24 s,
   constant `fsimIdHash`, NTP-epoch timestamps. **Gate:** the ship flies Haven → a
   second island with a player aboard. *This is the milestone.* Zero client patches.
5. **Stop and start** — board/leave detection via 1073, plus a `Helm01` part with
   1210 to interact with.
6. **Only then, player control.** Either Option B, or — strictly cheaper — keep the
   server authoritative and integrate the pilot's 1111 `{shipAxes, vertical,
   throttle}` into the path it publishes. **(b) needs no client patch**: the 1111
   writer is already on the shipped player prefab and only needs authority granted.
7. **Player-built ships** — shipyard entity + 1205/1206/1005/1004/1271, the 1208
   request/reply loop, 1270 blueprint flow, blob persistence. Weeks. Note that
   spawning a `Shipyard` entity is trivial (static-transform prefab), which strips
   the whole crafting prerequisite chain off this step.

## TELEPORT IS CHEAPER THAN ASSUMED

The often-repeated account — "the client applies and acks on 190606" — is one of
*two* paths, and it is the expensive one. **VERIFIED (assets):** the shipped
player prefab carries **both** consumers.

| | `TeleportTransformVisualizer` | `LocalTransformTeleportBehaviour` |
|---|---|---|
| requires | 190602 Reader, **1073 Writer**, 190607 Reader | 190602 Writer, **190606 Writer**, 190607 Reader |
| acks on | **1073** `lastExecutedRequest` | **190606** `TeleportAckState` |
| handles `Parent`? | **no — the else branch computes a name string and discards it** | yes |
| authority we must add | **none — we already grant 1073** | 190602 (have) **and 190606** (do not) |

So the cheap teleport is: **seed 190607 on the player, then send one update with
`localPosition` set, `parent` absent, `request` bumped.**
`TeleportTransformVisualizer` sets `transform.position`, calls
`playerMove.Respawn(...)` and acks on a component we already own. `RespawnVisualizer`
will not accidentally enable — it also needs 1092, 1093, 1072 and 1160.

| | teleport (parentless) | first ship (steps 1-4) |
|---|---|---|
| client patches | **none** | **none** |
| new authority grants | **none** | none |
| rough cost | **hours** | **~1 day** + the carry risk |
| what it buys | a player can be *somewhere else* | a player can *go* somewhere |

**Build both, teleport first, and do not let teleport become the answer.** It
unblocks every other workstream — multi-island testing, respawn, get-me-unstuck,
debug commands — and should exist regardless. But Worlds Adrift with a teleporter
and no ships is a museum.

## CORRECTIONS TO EXISTING DOCUMENTS

1. **`../findings-ships.md`'s headline** — "ship motion is gated by AUTHORITY, not
   worker type, because `SSPDeadReckoningBehaviour` carries no `[WorkerType]`". The
   attribute observation is right, the conclusion misleading: the publisher is
   gated by `ShipPreprocessor`'s `if (platform == UnityWorker)`, which **already ran
   at build time**. No instance exists on the shipped client prefab. The fix is
   injection, not an authority grant.
2. **`../findings-ships.md` Q5 stage A** says ~8 components. Measured: **four**.
3. **`../findings-ships.md` risk 2** ("1130 must be seeded with a valid control
   point or the publisher errors on enable") was already retracted — the code logs
   and falls through.
4. **`../verify/ship-prefabs.md`'s `PathFollower 0 instances`** — true and
   harmless; it is added at runtime. Read alone it looks like a blocker.
5. **The 2017 "reclaim the shipwreck"** does not describe a mechanic in our build.
   Reclaim = dissolve a ship you own. Ours wins.

## NOT VERIFIED — ordered by damage if wrong

1. **Whether a player on a `PathFollower`-driven hull is carried.** Everything from
   step 4 depends on it. Test in step 3.
2. **Whether the 39-byte hull blob is accepted.** Transcribed from the writer,
   never fed to `ShipPlan.Load`. A wrong byte is an exception.
3. **Whether `SynchronisedTime` ever syncs on our stack.** `SmoothFixedNow` — which
   is `PathFollower`'s entire sampling clock — only advances when `_synced` is true.
   **If NTP never succeeds, no ship ever moves on any client**, and it would look
   exactly like "our control points are wrong".
   *Coordinator's note, checked after this report: the failure string
   `"NtpTimeKeeper failed to sync"` (an `ErrorOnce`) has **zero** occurrences in a
   real session log, and the machine reaches `pool.ntp.org` fine. Not proof of
   success, but the catastrophic mode has no evidence behind it.*
4. **Whether `HandleAuthorityChanged(false)` fires at all on our stack** —
   `PathFollower.enabled = true` is set only there.
5. **Which server worker consumed `InteractWithObject{verb=Man}` and wrote 1109.**
   Absent from every decompiled tree; we must invent it.
6. **Who wrote 1113, 1115, 1116, 1257, 1258.** Same. Only relevant for Option B.
7. **How a ship became `docked`.** No client trigger exists.
8. **`ShipHullRequestResponse.ownerId`** is referenced at
   `ShipHullEditorVisualizer.cs:115` but absent from the gencode type. Resolve
   before implementing the 1206 reply.
9. **Whether `ShipFrame01`/`ShipFrame02` are a usable shortcut.** They have baked
   geometry, but **`ShipFrame01_unityclient` has no root `Rigidbody`** and
   `PathFollower.Awake` does `GetComponent<Rigidbody>()`. Likely
   visible-but-immovable. Prefer `ShipFrame` for anything that must fly.
10. **The 2017 footage generally** — Closed Beta 0.1.0.3, two years older than our
    build. Used only to generate hypotheses.
11. Nothing here was executed.

## DATA (`data/`)

| file | what |
|---|---|
| `prefab-component-census.tsv` | every root MonoBehaviour on `Traveller*`, `ShipFrame*`, `Shipyard*`, `Helm01`, `CoreMain`, `ModularEngine`, `Sail01`, `Deck01`, `HavenRuinedShipRespawner` — client and worker variants, with `m_Enabled` |
| `prefab_census.py` | the script; resolves `MonoScript` PPtrs with a raw-header fallback |
| `prefab-names.tsv` | all 354 entity-prefab roots in `resources.assets` |
| `req_shipframe.tsv`, `req_shipframe01.tsv`, `req_shipyard.tsv`, `req_player.tsv`, `req_parts.tsv` | per-prefab `[Require]` → component-id maps |
| `make_hulldata.py`, `hulldata-samples.txt` | hull-blob synthesiser and three worked examples |

Reproduce with `uv run --with UnityPy python <script>` under
`systemd-run --user --scope -p MemoryMax=4G`.
