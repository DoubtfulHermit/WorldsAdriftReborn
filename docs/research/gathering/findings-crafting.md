# FINDINGS — CRAFTING

**LEAD: recipes are NOT shipped in the client. They are server-supplied at runtime
as one gzipped JSON `Dictionary<string, SchematicData>` over the `SendSchematicData`
event on component 1097 `ReferenceDataState` — and OUR SERVER ALREADY SENDS ONE.**

`ReferenceDataRequestState_Handler.cs:56-59` pushes a hardcoded `glider` schematic and
it works. The premise "there is no crafting at all" is half wrong: **the recipe pipe is
live; the book and the transaction are not.**

## THE SMALLEST CHANGE THAT MAKES ONE RECIPE CRAFTABLE = 2 LINES
Both in `WorldsAdriftRebornGameServer/Game/Components/ComponentsSerializer.cs`:
1. `:291` — `defaultSchematics = new List<string> { "glider" }` (was `{ }`)
2. `:335` — `new CraftingStationClientStateData("", ...)` (was the literal `"schematicId"`)

The shipped glider has `"craftingRequirements":[]`, so `AllSlotsHaveRequiredMaterials`
is **vacuously true** (`CraftingMaterialSlotsExtension.cs:5-15`) and **the Craft button
enables with zero materials.** Making the click produce an item needs one handler.

## THE LOAD-BEARING CLAIM IS **FALSE** (as stated)
> "A static non-empty `defaultSchematics` makes crafting usable with zero persistence."

`CharacterLearnedSchematicLibrary.cs:227-237`:
```csharp
if (!_schematicsReferenceStore.GsimReferenceDataLoaded) {
    WALogger.Warn(...); return false;   // does NOTHING
}
```
`GsimReferenceDataLoaded` is set **only** in `SchematicsReferenceStore.DeserialiseJson`
(`:46`), reachable **only** from the 1097 handler. And
`SchematicSystem.RebuildCraftingDataSchematicHierarchy` (`:110-122`) refuses to build the
UI unless **all five** refdata types have landed (`ReferenceDataVisualiser.cs:146-151`).

**Correct restatement:** *a non-empty `defaultSchematics` PLUS a 1097 catalogue
containing that key (or a procedural-JSON entry) makes crafting usable with zero
persistence.* It only looked true because the repo already satisfies the hidden
precondition **two** independent ways — the server handler AND a client-side
`ReferenceDataPatch.Prefix` (`InventoryPatches.cs:41-64`). **Those two catalogues
currently race; keep them in sync or delete one.**

**Runtime proof of the current failure**, from our own log
(`BepInEx/LogOutput.log:2528,2566,15620,15658`):
```
[ERROR] [UI] [CraftingStationData] Schematic is null
   at CraftingStationData.<GetSchematicFromID>m__1(SchematicData schematicToLoad)
```
That is 1005's literal `clientSchematicId = "schematicId"` resolving to null. And
`grep -c "SchematicManager"` on the same log = **0**, confirming the catalogue *was*
loaded and only the key was wrong.

## WHERE RECIPES ARE NOT — exhaustively searched
Per-file UnityPy enumeration, each capped with `systemd-run MemoryMax=4G`:

| Container | Size | TextAssets | Recipes |
|---|---|---|---|
| `resources.assets` | 610 MB | 100 | none |
| `sharedassets0.assets` | 11.4 MB | 1 (`UnityClient.config`, ECS wiring) | none |
| `sharedassets1.assets` | 59.7 MB | 13 (island terrain) | none |
| `globalgamemanagers`, `level0/1` | 24 MB | 0 | none |
| `StreamingAssets/lpbundle` | 1.67 MB | 0 | none |
| `GameDB/clientGameDB.bytes` | **2,064 B** | AES | **6 tables, none item/recipe** |

Raw `grep -a` for `craftingRequirements|timeToCraft|amountToCraft|schematicId` across all
ten containers: **0 hits**. No `itemData.json` exists in the client install — the one in
the repo is a **server-side** asset.

## THE TRANSPORT
1097 carries five events, each `{ <plainField>, byte[] compressedData }`:
`SendInventoryData`, `SendScrapItemsDescriptions`, `SendSteamInventoryBundlesDescriptions`,
`SendResourceDescriptions`, **`SendSchematicData`**.
6908 `ReferenceDataRequestState` carries `RequestReferenceData{bool compress}` — **the
client tells us which encoding it wants**, so the prefab's `_compressData` never has to be
guessed. Our handler already reads it (`:39`).

**Second, undocumented door** (`SchematicsReferenceStore.cs:49-81`): if a key is not in
the catalogue and the string *looks like* JSON, it is parsed **inline** as a
`SchematicData` and tagged `Procedural`. **A whole recipe can be shipped inside a 1079
list entry** — no catalogue key needed.

## THE RECIPE SCHEMA (`acs/SchematicData.cs:99-141`, settable members only)
```
SchematicType (0 Fixed / 1 Procedural / 2 Ship), uUID, schematicId, referenceData,
category ("Shipyard"|"Personal"|"CraftingStation"|"Cooking"|"Clothing"), title, iconId,
description, timeToCraft (int seconds), amountToCraft (int), itemType (grouping key),
craftingRequirements (CraftingItemData[]), baseHp, baseStats (Dictionary<string,float>),
rarity (int, -1 clamped to 0), cipherSlots, unlearnable (bool), modules,
hullData (Base64, ships only)
```
`CraftingItemData` (`:5-18`): `id, name, iconId, component, description, amountRequired,
customizationCategory`.
Slot `component` constants (`SchematicData.cs:59-85`): Casing, Barrel, Ammo Loader,
Firing Mechanism, Aileron, Mechanical Internals, Combustion Internals, Propeller, Panel,
Deck, Mast, Figurehead, Plaque, Cage.
Stat keys (`:95`): hpStat, power, range, frag, choke, overheatLimit, rateOfFire, airBrake,
boost, fuelEfficiency.

### A REAL Bossa recipe (found commented out in our own repo, `InventoryPatches.cs:61`)
```json
{"category":"CraftingStation","title":"Procedural Wing","iconId":"ship_wing",
 "timeToCraft":7,"amountToCraft":1,"itemType":"fixed","rarity":0,
 "craftingRequirements":[
   {"id":0,"name":"Metal","component":"Casing","amountRequired":56},
   {"id":1,"name":"Metal","component":"Aileron","amountRequired":30},
   {"id":2,"name":"Metal","component":"Mechanical Internals","amountRequired":33}],
 "baseHp":0.13,"baseStats":{"power":0.1508,"pivotSpeed":0.1636}}
```
`name:"Metal"` is an item **category**, not an itemTypeId — any metal satisfies it
(`InventoryItemManager.IsSameMaterialType:117-120` matches category OR itemTypeId, plus
the special case `"Wood/Metal"`).

## STATIONS — and the free path that needs none
`CraftingStationBehaviour` requires **two** readers: 1005 `CraftingStationClientState`
and 1004 `CraftingStationGSimState` (`:24-28`). Both must be present or the visualiser
never enables. 1004's only consumer `OnGSimSchematicUpdated` is an **empty method**
(`:135-137`) — it exists purely to satisfy the `[Require]`.
A station's *category* is a `[SerializeField]` **baked into the prefab, not replicated**
(`:37`) — you cannot change what a station crafts from the server; you pick the prefab.

**`MultitoolCraftingBehaviour` requires ONLY 1005's reader (`:16-17`) and sits on the
player entity.** The MultitoolCraft tab opens entirely client-side. **This is the path to
build first — it needs no world entity at all.**

## 1005 FIELD CONTRACT (`gencode/.../CraftingStationClientStateData.cs:8-20`)
| field | drives | trap |
|---|---|---|
| `clientSchematicId` | catalogue lookup → builds slot list | **the `"schematicId"` literal is the live bug**; `""` is safe |
| `schematicOwner` | display only | — |
| `slottedMaterials` | `SyncCraftingItems`, **and clears the busy flag** | **indexes `[i]` for `i < CraftingSlotData.Count`** (`CraftingStationData.cs:283-285`) — must be **≥ craftingRequirements.Length** or IndexOutOfRange |
| `ciphers` | cipher UI | — |
| `itemReadyInSeconds` | countdown; `<0` closes the aperture | seeded `12` is a phantom |
| `currentWeight` | the `"{x}kg"` label | seeded `30f` is a phantom |
| `predictedStats` | stat preview | `None` handled |

## THE PROTOCOL — client→server is ALL on 1003 (already client-authoritative)
| action | wire | file:line |
|---|---|---|
| open a surface | field `craftingStationEntityId` | `PlayerCraftingInteractionBehaviour.cs:99-102` |
| select recipe | `SetSchematic{Option<string>}` | `:104-108` |
| insert material | `AddItemFromInventory{invEntityId, itemId, slotIndex, slotType}` | `:121-125` |
| pull material back | `ReturnItemToInventory{...}` (`slotIndex == -1` = ALL) | `:132-142` |
| Craft (station) | `StartCrafting{EntityId}` | `:110-114` |
| Craft (personal) | `StartPlayerCrafting{}` — zero fields | `:144-148` |

`itemId` is `InventorySlotData.serverItemId` — the id WE assigned in 1081, so server-side
lookup is unambiguous.

## THREE HARD-LOCK FLAGS — the ship-blueprint trap, three times over
1. **`CraftingStationData.IsWaitingForServer`** (`:87-97`) — set on every material drop,
   cleared **only** by the server touching 1005 (`SlottedMaterialsUpdated`,
   `AddItemFromInventoryFailed`, `ReturnItemToInventoryFailed`). While set the Craft
   button is permanently Disabled (`CraftingUI.cs:422`, `:284-287`).
2. **`InventoryContents.IsWaitingForServer`** (`:65,85`) — cleared **only** by a 1081
   update (`InventoryVisualiser.cs:127`). Greys out the whole grid.
3. **`CraftingInProgress`** (`:79`) — set by `CraftingStarted`, cleared **only** by
   `CraftingCompleted`. If you fire the first without the second the station is bricked
   **and** `LoadSchematic` early-returns, so the player cannot even switch recipe.

**NRE guard:** `StartCrafting()`/`FinishCrafting()` dereference `LoadedSchematic.timeToCraft`
(`:233-237`) — firing either event with no schematic loaded throws.
**Escape hatch:** `CraftingValidationFailed` / `AddItemFromInventoryFailed` /
`ReturnItemToInventoryFailed` clear the flags. **Emit one on every unhandled branch.**

## CORRECTION TO A PRIOR FINDING: the 1082 `craftItem` path is DEAD
`InventoryModificationBehaviour.OnItemCraftingStarted` (the sole caller of
`TriggerCraftItem`) fires only when `AddToInventory(..., timeToBuild > 0)`
(`InventoryContents.cs:104-107`) — and **every call site in the shipped client passes
`timeToBuild = 0`**. The `Console.WriteLine` loop at
`InventoryModificationState_Handler.cs:80-86` will never fire. **Material transfer flows
through 1003, not 1082.**

## EVENT ORDER (both messages are required — they clear different flags)
**Insert:** C→S `1003.AddItemFromInventory` → S updates stored 1081 and
`slottedMaterials` → S→C **1081** (clears flag 2) **and** S→C **1005** (clears flag 1).
Order between the two is free; 1081 is idempotent full-state.
**Craft:** C→S `1003.StartPlayerCrafting` → S→C 1005 `itemReadyInSeconds` +
`CraftingStarted` → …t… → S→C **1081** with the new item + 1005 `slottedMaterials=[]`
(correct length) + `itemReadyInSeconds=-1` + `CraftingCompleted`.
**Learn:** C→S `1082.TryToLearn{invEntityId, itemId}` → S consumes item, appends to 1079
`learnedSchematics`, pushes **1081 + 1079**.

## TWO ORDERING TRAPS ON 1079
- `AllReferenceAndPlayerDataLoaded` **clears the buffer unconditionally**
  (`SchematicSystem.cs:40-41`) — if `TryAddRawNonShipSchematics` returned false the
  schematics are **silently lost**. Seed 1079 at AddComponent time.
- `LearnedSchematicsUpdated` is a field callback on **`learnedSchematics` only**
  (`SchematicsLearnerClientState.cs:44,171,314`). **A later push touching only
  `defaultSchematics` is invisible.** Always touch `learnedSchematics` too.

## ORDERED PLAN (all paths under ~/Games/WAReborn-src)
0. **One recipe in the book** — the 2-line change above. Verify: Multitool Craft tab
   shows "cool glider". **No persistence.**
1. **Real catalogue out of the handler** — move the inline JSON to
   `Game/Items/Config/schematicData.json` (same `CopyToOutputDirectory` pattern as
   `itemData.json`), loaded by a `SchematicHelper` mirroring `ItemHelper.cs:47-68`.
   Derive `defaultSchematics` from its keys. **Reconcile the client-side duplicate.**
2. **Pure `CraftingPolicy` + unit tests, no wire** — given schematic + slottedMaterials +
   inventoryList → `{newSlotted, newInventory, ok/reason}`. Encode the length invariant
   and the category-or-itemTypeId match rule. Test before any handler exists.
3. **Make ONE glider craftable** — replace the echo in
   `PlayerCraftingInteractionState_Handler.cs:21-30`. Emit `CraftingValidationFailed` on
   every unhandled branch so the UI can never brick.
4. **Materials actually consumed** — handle add/return via `CraftingPolicy`. **Mutate the
   STORED component, not a `.ToUpdate()` copy** — the existing 1082 `equipWearable`
   handler mutates a discarded copy (`:60-63`); do not copy that pattern.
5. **A recipe with real ingredients** (the procedural wing). Pair with resources, or seed
   materials in `ItemHelper.GetDefaultItems()` for testing.
6. **Learning** — 1082 `tryToLearn` + 1260 unlearn. **Needs persistence.**
7. **Persist learned schematics** into `<data>/players/<characterUid>.json`. Restore by
   seeding 1079 at AddComponent time, NOT via a later update (trap above).
8. **Stations as world entities** — needs the per-entity `ComponentsSerializer` first.
   Seed 1004+1005+190602; **add a 1004 branch or the AddComponent batch aborts**
   (`SendOPHelper.cs:84-94`).
9. Ship parts (1013/1281), blueprints (1271), ciphers — separate milestone.

## NO AUTHORITY CHANGES NEEDED for steps 0-7
1003, 1082, 1097, 6908, 1260 are already in `MirrorSendPolicy.cs:125-132`. 1005, 1079,
1081 are correctly **server-owned** — do not grant them. 1080 is already force-injected
(`WorldsAdriftRebornGameServer.cs:662-664`) because `InventoryVisualiser` requires it.

## LIVE BUGS FOUND EN ROUTE
- **`SendOPHelper.cs:222` passes `updates.Count` instead of `cupdates.Count`** to
  `PB_EXP_ComponentUpdateOp_Serialize` — **reads past the array if any component fails to
  serialize.** Fix before pushing multi-component batches (1081+1005) per craft.
- The 1097 handler mutates `newRefData` from `.ToUpdate()` and **never writes it back**
  (`ReferenceDataRequestState_Handler.cs:42-61`). Harmless today; fatal as a pattern.
- `ItemHelper.ValidItem.stacksize` is **never populated** — the JSON has no `stacksize`,
  so `GetReferenceItems()` emits `stackingMax = -1` for every item while the mod's
  hardcoded catalogue uses 1/99.

## COULD NOT DETERMINE
The authentic Bossa recipe catalogue — it lived on the Scala GSIM and is not in this
install; recipe values must be authored (community wikis or a 2018 capture).
Whether 1005's `12`/`30f` are seconds/kilograms (they were copy-pasted from a
serialization scratchpad, `ChangeLogLoader_Patch.cs:159-165`).
`CraftingStationGSimState.lastCreatedEntityId` semantics — no client reader consumes it.
Nothing was executed; static analysis plus mining of the 2026-08-08 07:36 session log.
