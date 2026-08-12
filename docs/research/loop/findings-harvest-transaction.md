# FINDINGS — THE HARVEST TRANSACTION

*How a player with nothing gets their first resource into their inventory.*

Every line number in this document was read in this pass, against the tree as it stands on
`wip/loop-tools`. **Several line numbers in `docs/research/gathering/` have drifted** (e.g.
`findings-tools.md` cites the 1211 seed at `ComponentsSerializer.cs:148`; it is now `:184-194`).
Where I cite a prior document I quote it verbatim.

Legend: **[V]** I read the code. **[I]** inferred from code I read. **[A]** assumed / taken on
trust from another document. Everything not marked is **[V]**.

---

## 0. THE ONE-PARAGRAPH ANSWER

The player **already owns the tool** — the gauntlet's four modes are innate and are already
seeded into their inventory and already unlocked. There is no craft-your-first-tool bootstrap
problem. What is missing is a **channel**: component `1211 InteractAgentState` is seeded but is
**not in `MirrorSendPolicy.AuthoritativeComponents`**, so the client never resolves its writer,
`InteractAgentObserver.OnEnable` never runs, no tool is ever equipped, and **the server hears
nothing at all when the player presses anything.** The yield is unambiguously the server's to
invent — the shipped client contains no yield table and cannot: it receives its entire item
table *from us* over 1097, and that record has no rewards field. Our server has **no harvest
path whatsoever** (`grep -i harvest` over `WorldsAdriftRebornGameServer/` returns nothing), and
of the 15 `1082` events exactly **one** (`equipWearable`) mutates 1081; six log and drop, eight
are not mentioned. Nothing is persisted, and a *second interest request in the same session*
resets the inventory to defaults.

---

## 1. WHAT TOOL DOES THE PLAYER START WITH?

### They start with the whole gauntlet, and it needs no acquisition at all.

`ItemHelper.GetDefaultItems()` (`WorldsAdriftRebornGameServer/Game/Items/ItemHelper.cs:137-150`)
is what every player's 1081 is seeded with, unconditionally
(`Game/Components/ComponentsSerializer.cs:118-130`):

```csharp
MakeItem(1, "gauntlet_salvage", -1, -1, hotBarSlot: 0),
MakeItem(2, "gauntlet_repair",  -1, -1, hotBarSlot: 1),
MakeItem(3, "gauntlet_build",   -1, -1, hotBarSlot: 2),
MakeItem(4, "gauntlet_scanner", -1, -1, hotBarSlot: 3),
//MakeItem(1100, "gold", 2, 3, 40, 9),          <- commented out
MakeItem(1101, "glider"),
MakeItem(1102, "torso_poncho", 0, 4),
MakeItem(1103, "head_devhat", 3, 0)
```

Three facts make this decisive:

1. **The four gauntlet rows are decorative.** `InteractAgentObserver.Update:282` hardcodes the
   published `itemSlot` for hotbar 0-3 and never consults the inventory:
   ```csharp
   num4 = ((CurrentItemSlot == 0) ? (-2) : ((CurrentItemSlot == 1) ? (-5) :
          ((CurrentItemSlot == 2) ? (-3) : ((CurrentItemSlot != 3)
          ? _inventorySystem.Value.PlayerInventory.GetItemIdAtHotSlot(CurrentItemSlot) : (-6)))));
   ```
   Only hotbar **4-7** reads real inventory. Delete `gauntlet_salvage` from the seed and salvage
   mode still works. They are 0x0 items at `(-1,-1)` — grid-free UI shells.
2. **The gauntlet is already unlocked.** `8051 ToolState` is seeded `30`
   (`ComponentsSerializer.cs:556-561`) = `Salvage|Scan|Repair|Build`, and
   `ToolBehaviour.IsToolUnlocked` (`acs/ToolBehaviour.cs:95-108`) short-circuits `Multitool` and
   `Grapple` to `true` regardless. Slot selection is gated on this at
   `InteractAgentObserver.cs:272-279` and passes.
3. **There is no other harvesting tool in the game.** The `PlayerEquipment` hand-item enum is
   `{None, Multitool, ScannerTool, Pistol, Food, MusicalInstrument}`; the gauntlet has no
   durability, no tier and no quality (`findings-tools.md` established this; I did not re-verify
   the exhaustive grep — **[A]**).

### So the loop's blocker is not acquisition. It is that nothing is equipped and nothing is heard.

**Verified, and this confirms `findings-tools` CORRECTION 1:** the 1211
seed sets `itemSlot = 1` (7th ctor arg — field order confirmed at
`gencode/Bossa.Travellers.Interact/InteractAgentStateData.cs:9-25`), which
`PlayerEquipmentVisualizer.OnHotbarSlotChanged` (`acs/PlayerEquipmentVisualizer.cs:48-75`) sends
to `default: _equipper.TryEquip(null)`. `PlayerMultitool.Update` then early-returns forever
(`acs/PlayerMultitool.cs:190-196`). **No tool is equipped today, and the beam never charges.**

And 1211 is absent from the grant list — read it yourself,
`WorldsAdriftRebornGameServer.Multiplayer/MirrorSendPolicy.cs:125-132`:

```csharp
public static readonly IReadOnlyList<uint> AuthoritativeComponents = new uint[]
{
    8050, 8051, 6908, 1260, 1097, 1003, 1241, 1082,
    TransformStateComponentId,
    ClientAuthoritativePlayerStateComponentId,
    UtilitySlotActivatedStateComponentId,
    RopeControlPointsComponentId,
};
```

That same list is used twice at the interest-request site — once as injected interest
(`WorldsAdriftRebornGameServer.cs:734`) and once as the authority grant (`:742`) — so a single
array element does both jobs.

---

## 2. NEW, AND IT BREAKS THE CORPUS'S P0: THE INPUT PRIORITY STEAL

**This is the most important new finding in this document, and it invalidates the exit criterion
of `findings-tools`'s P0 and the SKEPTIC's "cheapest meaningful experiment in the corpus".**

`findings-tools.md:194-197` says:

> **P0 — make tool use observable. ~1 line + 1 test.** Add 1211 to `AuthoritativeComponents`;
> assert it in `MirrorSendPolicyTests`. **Exit criterion: a `UseItemKeyPressed` event with a
> non-invalid `target`.**

and `findings-SKEPTIC.md:69-75`:

> **THE 1211 GRANT IS OBSERVABLE WITH NO NODE AT ALL** ... **Aim at the other player, left-click,
> read the log. The cheapest meaningful experiment in the corpus.**

**That left-click will not produce a `UseItemKeyPressed`, because granting 1211 is exactly what
takes the left mouse button away from the component that sends it.** The chain, every link read:

| # | fact | evidence |
|---|---|---|
| a | `InputDispatcher.PrepareSinksForInput` sorts sinks **descending by priority** every frame | `acs/InputDispatcher.cs:149` |
| b | `DispatchButtons` gives a button to the **first** sink that `CanReceive` it and sets `receiving:false` on every later one | `acs/InputDispatcher.cs:63-101` (the `flag4` latch at `:81,:90`) |
| c | `InputPriorityClass`: `InteractAgent = 2`, **`PlayerItem = 3`**, `ObjectPlacement = 4` | `acs/InputPriorityClass.cs` |
| d | `InteractAgentObserver`'s sink is `InteractAgent` and captures `UseLeftHand` | `acs/Bossa.Prototype.Character.Observer/InteractAgentObserver.cs:151-164` |
| e | `PlayerMultitool`'s sink is `PlayerItem` and captures `UseLeftHand` | `acs/PlayerMultitool.cs:147` |
| f | that sink is registered **exactly while the tool is equipped** | `acs/PlayerMultitool.cs:192` (`_input.Enabled = Equippable.IsEquipped`), setter at `acs/InputSink.cs:40-61` |
| g | granting 1211 makes `itemSlot = -2` -> `TryEquip(Equipment.Multitool)` | `InteractAgentObserver.cs:282` -> `PlayerEquipmentVisualizer.cs:52-56` |

Full extraction of every `InputSink` in the client, sorted by priority, is committed at
`docs/research/loop/data/input_sink_priority.tsv`. **Four** `PlayerItem` sinks and one
`ObjectPlacement` sink outrank `InteractAgentObserver` on `UseLeftHand`
(`PlayerMultitool`, `PistolInput`, `FoodInput`, `PlayerScannerInput`, `ItemPlacingBehaviour`).

**Consequences, and they are actionable:**

- `UseItemKeyPressed` fires **only when nothing is equipped** — i.e. hotbar slots 4-7 holding
  nothing, or `itemSlot` outside `{-2,-5,-3,-6}`. It is the "use a *server-side* item" verb,
  not the salvage verb. The salvage verb is `2105`/`2106`, which is why those components exist.
- **`InputButtons.Interact` (E) is uncontested.** The extraction shows `InteractAgentObserver`
  is the *only* sink that captures it; the two `<ALL>` sinks above it
  (`TimedInteractionController` at 11, `ShipHullAgentVisualizer` at 7) are situational, and
  `ShipControlsBehaviour` explicitly excludes it (`acs/ShipControlsBehaviour.cs:137`).
  **The E-key / `InteractWithObject` route is therefore the robust one, and the left-click /
  `UseItemKeyPressed` route is the fragile one.** This *reverses* the corpus's implicit ranking.
- The P0 probe is still worth running, but its **correct** exit criterion is
  *"press key `5` (empty hotbar slot 4) and left-click -> a `UseItemKeyPressed` arrives"*, with
  *"press `1` and left-click -> nothing arrives"* as the confirming negative. **[I]** on the
  exact behaviour of an empty hotbar slot 4 (`GetItemIdAtHotSlot` return value on an empty slot
  was not read).
- One unverified link: whether `Equipment.Multitool` resolves and `ItemEquipper.EquipRoutine`
  (`acs/ItemEquipper.cs:114-152`, a coroutine) actually reaches `IsEquipped = true` on *our*
  spawned rig. If it does not, the steal does not happen and `UseItemKeyPressed` fires. **Either
  outcome is diagnostic**, which is the nice property of this probe.

---

## 3. ONE COMPLETE HARVEST ON THE WIRE

Committed as a machine-readable table at `docs/research/loop/data/harvest_wire_trace.tsv`.
Prose version, with authority called out at each step:

### Phase A — checkout (server-authoritative, works today)
1. `SEND_COMPONENT_INTEREST` from the client -> `WorldsAdriftRebornGameServer.cs:674-758`.
   `ComponentsSerializer.InitAndSerialize` fabricates each component and registers the ref in
   `GameState.ComponentMap[peer][entityId][componentId]` (`ComponentsSerializer.cs:610-651`).
2. **Server-authoritative:** `1081` (inventory), `8051 = 30` (tools unlocked),
   `1212` (interact server state), `8060` (feedback listener), `1077` (health).
3. **Granted to the client:** the 12 ids in `AuthoritativeComponents`, via `SendAuthorityChangeOp`
   at `:742`. **1211 is not among them — this is the single blocking line.**

### Phase B — aim (client-authoritative, dead today)
4. `PlayerLookingAt.FixedUpdate` raycasts **40 m** on `Layers.Interactables`
   (`acs/Assets.Scripts.Player/PlayerLookingAt.cs:80`) and sets `LookingAtEntity` /
   `LookingAtInteractive`. It **bails to `ResetLook()` every frame** unless
   `_interactAgentObserver.CanInteract()` — which is `_input.IsReceiving(InputButtons.Interact)`
   (`InteractAgentObserver.cs:504-507`) — which is false while the sink is unregistered.
   > **Correction to `findings-tools.md:99`**, whose raycast table lists
   > `InteractAgentObserver | 2000 m | 37121 | single ray`. The 2000 m ray at
   > `InteractAgentObserver.cs:290` produces `LookHitPoint` only (the aim point sent in the
   > update). `LookingAtEntityId` comes from `PlayerLookingAt` at **40 m**. Two different rays.
5. `InteractAgentObserver.Update:318-323` publishes `lookingAt`, `lookingAtInteractive`,
   `debugLookingAt`, `itemSlot`, `selectedHotbar` on 1211 — **client-authoritative**, and
   `FinishAndSend` suppresses no-change updates, so this is not a flat 60 Hz stream
   (`findings-interaction.md:127-131` is correct on this; I did not re-read `FinishAndSend`
   — **[A]**).

### Phase C — the input (client-authoritative)
Two disjoint verbs. **They are not interchangeable and only one is contested.**

- **E key -> `InteractWithObject{target: EntityId, verb: InteractVerb}`**
  (`InteractAgentObserver.cs:341` gate, `:400-408` timed hold, `:451-454` send).
  Requires an `InteractiveObjectVisualizer` on the target, which `[Require]`s
  `InteractiveStateReader` (`acs/Assets.Visualizers/InteractiveObjectVisualizer.cs:19-20`) —
  i.e. **1210 on the node**. Verb comes from the prefab's `[SerializeField] private InteractVerb Verb`
  (`:25-26`); the server cannot choose it. The node's 1210 `interactions` list must contain an
  entry whose `verb` matches, or `OnEnable:67`'s `FirstOrDefault` yields `default(InteractionEntry)`
  -> `radius = 0` -> silently uninteractable.
- **LMB -> `UseItemKeyPressed{target, lookDirection: Quaternion, itemSlot, sourcePosition: Coordinates}`**
  (`InteractAgentObserver.cs:291-300`; signature confirmed at
  `gencode/Bossa.Travellers.Interact/UseItemKeyPressed.cs:18`).
  > **Correction to `findings-tools.md:17-18`**, which writes the payload as
  > `TriggerUseItemKeyPressed(lookingAtEntityId, lookDirection, CurrentItemSlot, sourcePosition)`.
  > The second argument is a **`Quaternion`** — `Quaternion.LookRotation(normalized).ToNativeQuaternion()`
  > — not a direction vector. Wire fields are `Field1Target, Field2LookDirection, Field3ItemSlot,
  > Field5SourcePosition` (field 4 is a removed slot),
  > `gencode/.../UseItemKeyPressed_Internal.cs:13-16`.
  > It also fires **unconditionally on every left click**, with `target` possibly
  > `InvalidEntityId`. And per section 2, it is silenced while the gauntlet is equipped.

### Phase D — the beam (client-authoritative, four writers missing)
6. `PlayerMultitool.TryDeploySalvager` (`acs/PlayerMultitool.cs:288-325`) raycasts 10 m
   (`_maxAimDistance = 10f`, `:57`), resolves a `Salvageable` on the hit entity (`:297-300`),
   and on a charged deploy raises `ShotEntity(hitEntity, coords, direction, 1f)` (`:317-320`).
7. `PlayerMultitoolVisualizer` turns that into the wire. Its `[Require]` set is exactly four:
   `HealthStateReader` (1077, seeded, present) and **writers** for `MultiToolPlayerState` (2105),
   `MultitoolSalvagerState` (2106), `MultitoolRepairerState` (2002)
   (`acs/PlayerMultitoolVisualizer.cs:24-33`). It publishes, in order
   (`:87-93`, and `SalvagerAimerObserver` separately needs 1231):
   - `2105.ShotEntityEvent{EntityId, Vector3f relativeOffset}`
   - `2106.ShotEvent{EntityId, Coordinates, Vector3d}`
   - `2106.DeployedEvent{}`
   **No damage, no item, no amount is on the wire.** `PlayerMultitool.SalvageShootDamage = 200`
   (`:42`) has **exactly one occurrence in the entire decompile — its own declaration.** It is
   dead client-side. `2105.salvagerBlastDamage` has no client reader.

### Phase E — the credit (server-authoritative, does not exist)
8. Server decides item + amount + quality. **Nothing here exists.**
9. Server pushes a **full replacement** `1081.inventoryList` (there is no add-delta:
   `gencode/Bossa.Travellers.Inventory/InventoryStateData.cs:9-25`).
   `InventoryVisualiser` subscribes `_inventoryState.ComponentUpdated` **unconditionally**
   (`acs/Bossa.Travellers.Visualisers.Profile/InventoryVisualiser.cs:98`) ->
   `InventoryStateOnPropertyUpdated` (`:106-109`) -> `LoadInventory()` (`:121-130`), which is the
   only place `SetServerAsWaiting(false)` is called for the player inventory (`:127`).
   **Any 1081 push both renders the item and unsticks the panel.**
   Trap confirmed: `width/height/hasBelt/beltRow` are read once, at `OnEnable:86`.
10. Optional and free: `8060 FeedbackListener` ->
    `TriggerReceiveSalvageFeedback(string itemTypeId, int quantity)`
    (`gencode/Bossa.Travellers.World/FeedbackListener.cs:50,206-208`) ->
    `FeedbackVisualizer` (`acs/FeedbackVisualizer.cs:24,41-43`) -> `FeedbackScreen` renders
    `$"Salvaged {inventoryItemData.name} x{Quantity}"`
    (`acs/Travellers.UI.Feedback/FeedbackScreen.cs:139-141`, fresh-toast path `:331-335`).
    8060 is **already seeded** (`ComponentsSerializer.cs:413-418`) and is server-owned — no grant.
    **Hazard confirmed:** `InventoryItemManager.LookupItem` returns `null` on a miss
    (`acs/InventoryItemManager.cs:99-110`) and `FeedbackScreen:141/:332/:335` dereference it
    unguarded -> NRE. Never send an `itemTypeId` absent from our own `itemData.json`.
11. Depletion: a `12283 MetalRockCrustState` update (`shotPoints` is a **growing list**,
    `exploded` is the terminal flag). **[A]** — taken from `findings-node-relay.md:51-73`; I did
    not open `MetalRockCrustStateData.cs` in this pass.

**Authority summary:** phases A and E are ours; phases B, C and D are the client's and are all
gated behind the same single missing grant. **No SpatialOS commands are involved anywhere on
this path** — everything is `COMPONENT_UPDATE_OP`, the op we already handle.

---

## 4. WHO COMPUTES THE YIELD? — THE SERVER, WITH NO AMBIGUITY

**Verified negative, from five independent directions:**

1. **The client does not even own the item table.** `ReferenceDataVisualiser` *requests* it on
   enable (`acs/Bossa.Travellers.Visualisers/ReferenceDataVisualiser.cs:60`,
   `TriggerRequestReferenceData`) and populates from the server's reply
   (`:96-102`, gzip -> `InventoryItemManager.Instance.DeserialiseJson`). Those two lines are the
   **only** callers of `DeserialiseJson` in the whole decompile. Component `1097 ReferenceDataState`
   (`WAReborn-decompiled/component-map.tsv:100`) is in our grant list and our seed
   (`ComponentsSerializer.cs:575-590`) — **we are the item database.**
2. **The record the client parses has nine fields and no yield.**
   `acs/InventoryItemData.cs`: `itemTypeId, name, category, iconName, stackingMax,
   numOfSlotsWidth, numOfSlotsHeight, equippable, wearable`. There is nowhere to put a drop table.
3. **The salvage parameters replicate but are never read.** `SalvageAndRepairStateData`
   (`gencode/Bossa.Travellers.Salvaging/SalvageAndRepairStateData.cs:9-31`) carries
   `salvageDamagePerPeriod`, `salvageRatio`, `repairToSalvageRatio`, `period`,
   `originalMaterials`, `casingQuality`. Grep for `salvageRatio` / `salvageDamagePerPeriod` over
   the decompile hits **only `gencode/`** — zero consumers in `acs/` or `ecs/`. They are GSim
   inputs riding on a component the client happens to receive.
4. **The only client code that touches `Salvageable` uses it for audio.**
   `acs/Salvageable.cs` is 19 lines: one `[Require]` reader, one `OriginalMaterials` passthrough,
   three abstract predicates. Its only non-visual consumer is
   `PlayerMultitool.ImpactSalvage:347-352`, which uses `OriginalMaterials` to pick a **sound
   switch**. `MaterialSourceVisualizer` uses it to tint a material.
5. **A client cannot ask for an item.** The 15 `1082` events are
   `removeItem, craftItem, moveItem, crossInventoryMoveItem, splitItemStack, assignToHotBar,
   removeFromHotBar, equipTool, equipWearable, unequipWearable, moveAll, tryToConsume,
   tryToLearn, installCipher, destroyCipher` — no add/grant/reward. The three
   `InventoryContents.AddToInventory` call sites are local optimistic UI in crafting-slot
   drag-out, immediately followed by `SetServerAsWaiting(true)` and an authoritative RPC, and
   the local model is wholesale cleared and rebuilt from the server list
   (`acs/Travellers.UI.PlayerInventory/InventoryContents.cs:157,163`).

**The one genuine yield table we hold is our own.** `findings-items-materials.md` reports a
tier-keyed `rewards` block on all 134 `Salvage` items in `itemData.json`, extracted to
`docs/research/gathering/data/salvage_yields.tsv` (239 rows), silently dropped by
`ItemHelper.ValidItem` because it has no `rewards` property — **[A]**, I did not re-run that
extraction, but I did verify `ValidItem`'s property list
(`ItemHelper.cs:16-45`: `itemTypeID, name, height, width, stacksize, iconName, equippable,
characterSlot, category, description, rarity, metadata`) and it indeed has no `rewards`.
That table covers **scrap salvage**, not raw metal/wood nodes; the damage->yield and
quality->stat formulas for nodes are still unrecoverable and must be invented.

**Practical consequence: the server's job is *larger*, not smaller.** There is no client table
to defer to. But the flip side is that the server can be arbitrarily crude at first — the client
will render whatever number it is handed.

---

## 5. WHAT IS ALREADY IMPLEMENTED VS MISSING

### The brief's premise about `InventoryModificationState_Handler` is *half* wrong — verified

The brief says it *"reportedly only LOGS 1082 events instead of mutating the server-owned 1081"*.
Read `Game/Components/Update/Handlers/InventoryModificationState_Handler.cs`:

- **`equipWearable` (`:26-74`) genuinely mutates and pushes.** It dereferences the stored 1081
  (`:36`), rewrites `slotType` on the matching item (`:43-46`), and sends
  `{1280, 1081, 1088}` to the owner (`:51`) plus a 1081 fan-out to every other peer (`:64-73`).
  That is a real, working, server-authored inventory mutation. **It is the template to copy.**
- **Six events log and drop:** `equipTool` (`:75-79`), `craftItem` (`:80-86`),
  `crossInventoryMoveItem` (`:87-97`), `moveItem` (`:98-107`), `removeFromHotBar` (`:108-113`),
  `assignToHotBar` (`:114-120`).
- **Eight are not mentioned at all:** `removeItem, splitItemStack, unequipWearable, moveAll,
  tryToConsume, tryToLearn, installCipher, destroyCipher`.

Full table committed at `docs/research/loop/data/inventory_1082_event_coverage.tsv`.

The trailing `SendComponentUpdateOp(player, entityId, {1082}, {serverComponentUpdate})` at `:122`
echoes the 1082 back. **That does not unstick the panel**: `IsWaitingForServer` is cleared only
inside `LoadInventory()`, reached only from a 1081 update
(`InventoryVisualiser.cs:98,106-109,127`). So today, **the first time a tester drags an item
their inventory panel greys out permanently.** That is true right now, with no new code.

### The in-code comment at `:60-63` is wrong, and so is its correction's confidence

The handler comments *"the slotType change lives only on this copy, it is never written back"*.
`findings-inventory.md:130-133` calls that *"factually wrong"* because
`Improbable.Collections.List<T>` is a class and `ToUpdate()` shares the reference. **I did not
re-verify `ToUpdate()`'s implementation in this pass** — flagging it as **[A]**, still open, and
noting that it does not matter for a fresh design: the plan below routes every mutation through
one seam that writes back explicitly.

### The rest of the ledger

| thing | status | evidence |
|---|---|---|
| 1211 grant | **MISSING** — one array element | `MirrorSendPolicy.cs:125-132` |
| 1211 seed | present, `itemSlot = 1` (irrelevant once granted) | `ComponentsSerializer.cs:184-194` |
| 1212 seed | present, `InvalidEntityId` args fixed | `ComponentsSerializer.cs:196-211` |
| 8051 = 30 | correct, leave it | `ComponentsSerializer.cs:556-561` |
| 8050 handler | **MISSING** — seeded and granted, requests fall through | `ComponentsSerializer.cs:562-567` |
| 8060 seed | present, **never triggered** | `ComponentsSerializer.cs:413-418` |
| 1210 seed branch | **MISSING** | no `1210` in `ComponentsSerializer.cs` |
| 2105 / 2106 / 2002 / 1231 seed branches + grants | **MISSING** | grep: zero non-doc hits |
| 12283 / 1099 / 1016 / 1032 seed branches | **MISSING** | ditto |
| any harvest/yield code | **MISSING** | `grep -i harvest` over the game server -> nothing |
| per-entity seeding | **MISSING** — 1088 is the only branch that reads a side table | `ComponentsSerializer.cs:145` vs `:118-130` |
| inventory persistence | **MISSING at every layer** | see below |
| ownership gate on inbound updates | **MISSING** | `WorldsAdriftRebornGameServer.cs:776` calls `HandleComponentUpdate` with no `Players.Owns` check; `:781` relays likewise |
| relay can address a non-player entity | **NO** — `entityId` is available at `:766` and not passed to `RelayToOtherPlayers` at `:781` | verified; `findings-node-relay`'s core claim holds, line numbers drifted from 697/707/712 |

### Persistence: nothing, at three layers

- `WorldsAdriftReborn.Storage` has exactly three tables — `accounts`, `sessions`, `characters` —
  and says so on purpose: `Schema/SchemaScripts.cs:62-65`, *"Inventory and progression are
  deliberately absent: they belong to the game server."*
- `WorldsAdriftRebornGameServer.csproj` **does not reference `WorldsAdriftReborn.Storage` at
  all**, so the game server cannot reach the database today.
- `grep -E 'File.Write|WAREBORN_DATA_DIR|JsonConvert|Npgsql'` over
  `WorldsAdriftRebornGameServer/` and `.Multiplayer/` -> **zero hits.** Nothing is written anywhere.
- Worse than "lost on relog": **lost on re-checkout.** `ComponentsSerializer.cs:636-641`
  overwrites the stored refId with a freshly-seeded default whenever the same
  `(peer, entity, component)` is served again, and the "already set up" branch at
  `WorldsAdriftRebornGameServer.cs:750-757` re-serves whatever the client asks for, forever.
  **A second interest request for 1081 resets the inventory to the seven default items.**
- `AppearanceStore` is a pure in-memory `Dictionary<long, ...>` keyed by **entityId**
  (`Multiplayer/AppearanceStore.cs:14`), and `EntityIdAllocator` never reuses ids
  (`Multiplayer/EntityIdAllocator.cs:26-29`) — so it is a within-session mirror cache, **not** a
  persistence precedent. Copying its *shape* is right; copying its *key* is fatal.
- `ForgetPeer` (`WorldsAdriftRebornGameServer.cs:67-108`) cleans six things and **never touches
  `GameState.ComponentMap`**, contradicting its own docblock at `:55-63`.

### Exactly what it takes to make ONE harvest persist

1. **One array element**: add `1211` to `AuthoritativeComponents` (+ a `MirrorSendPolicyTests`
   assertion). Without it nothing above the transport layer runs.
2. **An `InteractAgentState_Handler`** (~40 lines, modelled on the equipWearable branch): read
   `interactWithObject` / `useItemKeyPressed`, ignore `target == InvalidEntityId`, validate
   `Players.Owns(sender, entityId)` and distance.
3. **An `InventoryStore` keyed by character uid**, in `.Multiplayer/` (pure, unit-testable),
   plus an `InventoryPolicy` enforcing every wire invariant (`slotType` exactly `"None"`;
   `meta` non-null; unique `itemId`; the whole w x h rectangle in bounds and free).
4. **A single `InventoryPush` seam** that mutates the store, writes back to `ComponentMap` for
   **every** peer holding the entity, and sends `1280 -> 1081 -> 1088` in that order. Nothing else
   may push 1081.
5. **Make the 1081 seed read the store**, using the shape already proven for 1088 at
   `ComponentsSerializer.cs:144-164`.
6. **An item-id allocator.** There is none (`findings-items-materials.md` reports
   `grep nextItemId` -> nothing; **[A]**). Duplicate ids silently corrupt lookups.
7. **Get the character uid to the game server.** `PlayerPropertiesState_Handler.cs:54-70`
   contains an explicit unverified probe for exactly this, whose own comment says the assumption
   *"has only ever been verified by reading the decompiled client, never observed running."*
   **This is the load-bearing unknown for persistence** — without the uid there is no durable key.
8. **Persist it**, either as a `V2` appended to `SchemaScripts.All` (`Schema/SchemaScripts.cs:57`,
   append-only) plus a new `ProjectReference`, or as a game-server-local JSON file in the shape
   of `WorldsAdriftServer/Persistence/JsonFileStore.cs:13`.
9. **Clean `ComponentMap` in `ForgetPeer`**, and stop `:636-641` clobbering live inventory state.

Steps 1-2 are hours. Steps 3-6 are the real work. Step 7 is a *measurement*, and it should be
made first because it can invalidate the design of step 3.

---

## 6. THE SMALLEST END-TO-END SLICE

**Design rule, inherited from the SKEPTIC and worth keeping: no step may depend on an earlier
step passing.** Steps 1-3 below are independent and each is separately observable.

### Slice A — prove the channel, zero world entities (half a day)

1. **Add `1211` to `AuthoritativeComponents`** (`MirrorSendPolicy.cs:125-132`) + one test.
   *Observable immediately, before any key is pressed:* the yellow interaction outline starts
   appearing, because `PlayerLookingAt.FixedUpdate:80` stops taking the `ResetLook()` branch.
2. **Register an `InteractAgentState_Handler` that only logs**, keyed by FNV-1 hash of the
   component factory type name (`ComponentUpdateManager.cs:36-53,147-162`).
   *Probe 2a:* press `1` (salvage), left-click -> expect **no** `UseItemKeyPressed` (section 2).
   *Probe 2b:* press `5` (empty hotbar slot 4), left-click -> expect a `UseItemKeyPressed`.
   Together these settle the input-steal question in ten seconds and cost nothing.
3. **Grant on a timer, not on input** — a server-side 10-second tick that pushes
   `1081` + `8060` with `("iron", 12)`:
   ```csharp
   ItemHelper.MakeItem(itemId: 1200, itemTypeId: "iron", x: 0, y: 8, amount: 12, quality: 5)
   // ScalaSlottedInventoryItem{itemId, itemTypeId, amount, slotType:"None", utilitySlotNum:-1,
   //   xPosition, yPosition, rotated:false, hotBarSlotNum:-1, timeToBuild:0, quality,
   //   lockBoxItem:false, meta:Map (non-null, {} is fine), rarity:Option<int>}
   ```
   `iron` is present in `itemData.json` (3x2, category `Metal`, `metadata: {}`), so no NRE in
   either the grid or the 8060 toast. **This decouples "can we make an item appear" from "can we
   hear a key", which are separate risks.** Expect the count label **not** to render — no item
   in `itemData.json` has a `stacksize` key, so `ValidItem.stacksize` stays `-1`
   (`ItemHelper.cs:22`) — **[A]** on the client's `stackingMax <= 1` hiding rule
   (`findings-inventory.md:22-26`, not re-verified). Fix by adding `"stacksize": 99` to the row.

### Slice B — one real, shared, depleting node (the actual loop)

4. **Make seeding per-entity.** `InitAndSerialize` already receives `entityId` and 1088 already
   branches on it (`:145`). Add an early `if (Nodes.IsNode(entityId)) { ... }` before the id chain.
   Without this, spawning any node hands it the player's inventory and the player's transform.
5. **Spawn one node** via the existing `SendOPHelper.SendAddEntityOP` path — the island already
   does exactly this (`WorldsAdriftRebornGameServer.cs:491`) — with the id allocated **once** from
   `EntityIdAllocator` and replayed byte-identically to every joiner, and
   `failOnComponentInitError: false` so a missing component names itself instead of aborting the
   whole `AddComponentOp`.
6. **Seed `1210` on it** (`InteractiveStateData{available, inUseBy, interactions, syncSchematics}`)
   with an `interactions` list containing an entry whose `verb` **equals the prefab's baked
   `[SerializeField] Verb`** and whose `radius > 0`. Get either wrong and the object is silently
   uninteractable with no error — `InteractiveObjectVisualizer.cs:26-28,62-68`.
   Per the verb census in `findings-interaction.md:135-141` (**[A]**), loot piles are
   `Inventory(60)` and nugget-class objects are `Default(50)`.
7. **Handle `1211.interactWithObject`** — E, the uncontested key (section 2) — by pushing
   1081 + 8060 to the harvester and a depletion state update to **every peer holding the node,
   harvester included** (`PeersExcept` is the wrong fan-out set for a node).
8. **Persist**: uid probe -> `InventoryStore` -> seed reads the store -> relog shows the iron.

**Recommended target for step 5-6:** a loot ruin pile (`1210 + 1081`, no beam, no health, no
crust graph, one grant, one new seed branch), per `findings-interaction.md:5-8` — **[A]**, the
UnityPy prefab dump behind that claim was not re-verified here and is the most expensive
unre-checked result in the corpus. Metal nugget is the better *game* answer and the more
expensive *engineering* answer; the SKEPTIC picked nugget, `findings-interaction` picked
container. **I do not adjudicate that here** — Slice A is neutral between them and should be
built first regardless.

---

## 7. CONTRADICTIONS WITH EXISTING DOCUMENTS (all named and quoted)

1. **`findings-tools.md:194-197` P0 exit criterion** — *"Exit criterion: a `UseItemKeyPressed`
   event with a non-invalid `target`."* **Will not be met by left-clicking with the gauntlet
   selected.** Section 2. Same for `findings-SKEPTIC.md:69-75`'s *"Aim at the other player,
   left-click, read the log. The cheapest meaningful experiment in the corpus."*
2. **`findings-tools.md:99`** raycast table, *`InteractAgentObserver | 2000 m | 37121 | single ray`*
   — conflates the 2000 m `LookHitPoint` ray (`InteractAgentObserver.cs:290`) with the 40 m
   `Layers.Interactables` ray that actually produces `LookingAtEntityId`
   (`PlayerLookingAt.cs:80`).
3. **`findings-tools.md:17-18`** payload — the second argument of `TriggerUseItemKeyPressed` is a
   `Quaternion`, not a look-direction vector.
4. **The task brief's premise** — `InventoryModificationState_Handler` does **not** only log:
   `equipWearable` (`:26-74`) is a complete working mutation and is the correct template.
5. **Line-number drift throughout `docs/research/gathering/`.** `findings-tools.md` cites the
   1211 seed at `ComponentsSerializer.cs:148` (now `:184`);
   `findings-interaction.md:155` cites the `updates.Count` bug at `SendOPHelper.cs:222` — it is
   now **`:217`** (`PB_EXP_ComponentUpdateOp_Serialize(entityId, u, (uint)updates.Count, &len)`
   where `cupdates` may be shorter). `findings-node-relay.md:12-13` cites `:697/:707/:712` — now
   `:766/:776/:781`. **Re-grep before editing anything based on those documents.**

---

## 8. NOT VERIFIED

Things I did **not** read in this pass and am therefore not vouching for, even where I relied on
them above:

- **`InventoryState.Data.ToUpdate()`'s reference semantics.** The whole `findings-inventory`
  "the comment is wrong / bugs 1-3" analysis rests on it. Unresolved.
- **`ItemEquipper.EquipRoutine` reaching `IsEquipped = true` on our spawned rig.** The one
  unverified link in section 2's chain. Also whether `Equipment.Multitool` is non-null.
- **`GetItemIdAtHotSlot` on an empty slot 4** — probe 2b's expected value.
- **`FinishAndSend`'s no-change suppression** (`findings-interaction.md:127-131`).
- **`stackingMax <= 1` hiding the count label**, and the wood-delta-<=5 SFX mute.
- **Any UnityPy prefab result**: the 107 components of `Traveller@Player_unityclient`, the
  `LootRuinPile11` `[Require]` closure, `_maxAimDistance = 10.0` as a *serialized* value (the C#
  field initializer is 10f, `PlayerMultitool.cs:57`, which is a different claim), the
  "island has zero interactables" scan of 1 of 255 bundles.
- **The 191-instance verb census** and the `Verb` byte-parse behind it.
- **`MetalRockCrustStateData` / `MetalRockStateData` / `12283` field lists** — taken from
  `findings-node-relay` and `findings-items-materials`.
- **The `rewards` extraction** (239 rows) — I verified only that `ValidItem` has no `rewards`
  property, which is what makes it get dropped.
- **`grep nextItemId` -> nothing** (no item-id allocator).
- **The island world origin / coordinate chain** — still the corpus's largest unknown, untouched.
- **Whether the character uid actually rides along in the 1088 update** — the probe at
  `PlayerPropertiesState_Handler.cs:54-70` has never fired in a live session. This is the single
  measurement that most changes the persistence design.
- **Nothing was executed.** No client was launched, no packet was observed. Every claim here is
  static analysis, which is the failure mode this project has been burned by. Treat section 2 in
  particular as a *falsifiable prediction with a ten-second test*, not as a fact.
