# FINDINGS — INVENTORY WRITE PATH

## LEAD: the minimal correct way to hand a player 5 wood
```csharp
var data = (InventoryState.Data)ClientObjects.Instance.Dereference(
               GameState.Instance.ComponentMap[peer][entityId][1081]);
var list = new Improbable.Collections.List<ScalaSlottedInventoryItem>(data.Value.inventoryList);
list.Add(ItemHelper.MakeItem(itemId: 1200, itemTypeId: "oak", x: 0, y: 8, amount: 5, quality: 0));
data.Value.inventoryList = list;                    // WRITE BACK — the step the equip path skips for 1280
var upd = new InventoryState.Update(); upd.SetInventoryList(list);
SendOPHelper.SendComponentUpdateOp(peer, entityId, new List<uint>{1081}, new List<object>{upd});
```
**1081 is full-state — there is no "add" delta.**

### Hard constraints
`slotType` **exactly** `"None"` (case-sensitive `Enum.Parse`, unguarded) · `itemId` unique
and >100 · `itemTypeId` must exist in the client's item DB (unknown ⇒ NRE) · `x,y` in bounds
with the whole `w×h` rectangle free (**unbounded array write ⇒ IndexOutOfRange**) ·
`timeToBuild = 0` (>0 greys the item out) · `meta` **non-null** (`TryGetValue` called
unguarded on every icon update) · `hotBarSlotNum`/`utilitySlotNum` = -1.

### TWO GOTCHAS THAT MAKE A CORRECT GRANT LOOK BROKEN
1. **The "5" will not render.** `ValidItem.stacksize` defaults to `-1`, **no entry in
   `itemData.json` has a `stacksize` key** (verified: 335 items, zero occurrences), and the
   client hides the count label when `stackingMax <= 1`. **Fix: add `"stacksize": 99` to the
   wood/metal/fuel rows, or default `ValidItem.stacksize` to 99.**
2. **The collect SFX will be silent.** Wood deltas ≤5 are deliberately muted
   (`InventoryContents.cs:552-555`). **Grant 6+, or grant metal.**

Also: `"oak"` is in `itemData.json` but **not** in the mod's hardcoded reference JSON
(which has `birch`, not `oak`/`palm`). The server's 1097 payload replaces that list
wholesale — so `oak` resolves **only if the 1097 reply landed**. `birch` is safe either way.

## 1081 SCHEMA — 9 fields
`updateSequence` and `jsonData` have **ZERO consumers** — never compared, never parsed.
`inventoryList`, `lockBoxItems`, `allowedItems` must all be **non-null** (unguarded
`foreach`). `allowedItems` is functionally dead — both branches of its only caller return true.

**CRITICAL TIMING:** `width`, `height`, `hasBelt`, `beltRow` are read **exactly once**, at
`InventoryVisualiser.OnEnable:86`. `LoadInventory()` never calls `Setup`. **Changing grid
dimensions in a later 1081 update is silently ignored until entity re-checkout.**
`beltRow >= height` with `hasBelt` throws in the constructor.

### The 14 item fields
All 14 are copied; none dropped. `meta` is **the only place colours and item health live** —
keys the client reads: `PrimaryColor/SecondaryColor/TertiaryColor`, **`materials`** (the only
JSON parsed out of 1081 — and it is `meta`, not `jsonData`), `totalHealth`, `health`,
`overrideIconName`, `cipherStats/cipherRarity/cipherShipPartType`, plus tooltip overrides.

**There is no per-item health FIELD.** Max health = `meta["totalHealth"]` (1081); current
health = `WearableUtilsState.healths` (**1280**); neither renders in the panel — condition
shows on the 3D model via `_DamageLevel`.

### `slotType` — one bad value blanks the ENTIRE inventory
`(CharacterSlotType)Enum.Parse(...)` — case-sensitive, no TryParse, no try/catch. The throw
propagates up through `LoadInventory`, and `AllSlotDataLookup.Clear()` **has already run**.
Legal: `None, Head, Body, Feet, UtilityHead, Utility, UtilityFeet, Face, FacialHair, Tool,
UtilityHand, Pet`. A second string-literal comparison depends on the same spelling
(`GearWearablesVisualizer.cs:165`).

## THE GRID — four different mechanisms
Origin **top-left, y grows downward**. Stock player grid 10×18.

**`beltRow` IS A ROW INDEX, NOT A COUNT** (corrected 2026-08-20; this page previously
said "belt at row 3" and the server shipped 3). `InventorySpaceChecker` uses it directly as
`LocationArray[x, beltRow]`, counted down from the top, and blocks that one full-width row.
Retail put the belt on the **bottom three rows with the divider immediately above them** —
Bossa's own forum answer: *"the lower three (four if you count the spacer) grid rows were
converted into the new belt area"*, and *"the entire screen is all the same container"*.
So the correct index is **`height - 4`**, i.e. **14** for an 18-tall grid, and 3 blocked a
row four cells down inside the backpack.

**The blocked row is a WALL for the drag ghost, not just a refused cell.**
`InventorySlotRectController.CheckNewGridCoordsAreValid` returns `_lastNonBlockedGridCoords`
whenever the dragged rectangle touches it, and the drop commits `ServerAdjustedCoords` — the
ghost's stuck position, not the mouse's. Everything on the far side of the wall is
unreachable by dragging, and releasing there sends a coordinate the player never chose.
- **Worn** = `slotType != None` → **excluded from the grid entirely**; x/y ignored.
- **Hotbar** = `hotBarSlotNum > -1`, **orthogonal to `beltRow`** — the item still occupies
  real grid space. 8 slots; **0–3 are the fixed gauntlets, 4–7 user-assignable**, displayed
  as keys 5–8. `>= 8` logs an error and drops it.
- **`beltRow`** is purely spatial — an un-droppable row, never selects items.
- **Stash** is **not a grid** — a category list of fixed 2×2 tiles, so x/y/rotated on stash
  items are parsed but never used.

### There is ZERO validation of server-supplied coordinates
| bad placement | client behaviour |
|---|---|
| out of bounds / negative | **IndexOutOfRangeException**, aborting the refresh mid-way |
| overlapping | **renders overlapping** — distinct keys, the cell is just overwritten. No error |
| on the belt row | renders; **that column's belt stops blocking** |

`(-1,-1)` is safe **only for 0×0 items** — which is exactly why the four gauntlets are 0×0
at (-1,-1). A non-zero item at (-1,-1) throws.
Interactive drag validates bounds and blockers but **not occupied cells**.
**Resolved 2026-08-20:** the default glider at (0,0), 3×4, spanned y 0–3 and overwrote the
belt blockers at (0,3),(1,3),(2,3) — because the divider was at row 3 in the first place.
With the divider at `height - 4` the seed clears it, and `InventoryPolicy` now refuses the
row on every path (grant, move, unequip, cross-inventory, move-all) so nothing can punch a
hole in it again. `ValidateForWire` reports any item that already sits on it.

### The divider has no picture, and that is deliberate
Retail drew it with a real inventory item — `itemTypeId == "beltSeparator"`, special-cased
for tooltips in `ScannableData.cs:474` ("Items placed on the belt are not dropped when you
die"), icon `beltseparator` in the atlas. **We do not send it**, so our divider is a blank
unusable row. Sending one is NOT a free win: `InventorySpaceChecker.AddItem` writes an
item's slot data over every cell it covers, blockers included, so a full-width separator
item deletes the very blockers it is meant to mark — which is exactly the *"exploit that
allowed players to put items onto the belt separator squares"* that Bossa fixed in 0.1.6.1.
Any attempt must use a footprint whose height is 0 (so `AddItem`'s inner loop never runs),
and must be verified in a live client before it ships.

## STACKING AND IDENTITY
**The client never merges stacks** — one `InventorySlotData` per wire item, no grouping.
Merging is entirely server business. `amount > stackingMax` is **not clamped**; `amount <= 0`
is **not rejected** (keeps its space, renders `0`/`-3`).
Key is `(EntityId, ItemId, ItemType, IsSplitItem)`, inserted with the **indexer**:
- same id + same type → **silent overwrite**, one item vanishes
- same id, different type → both survive, but `GetItemById` is non-deterministic and
  `RemoveByItemId` deletes **both**

**The "ids under 100 are reserved" rule is OUR convention** — a server-side comment only.
Exhaustive grep of the client found no such reservation anywhere.

## 1082 — 15 EVENTS, ZERO DATA FIELDS
`moveItem` · `crossInventoryMoveItem` · `splitItemStack` · `removeItem` · `assignToHotBar` ·
`removeFromHotBar` · `equipWearable` · `unequipWearable` · `equipTool` · `craftItem` ·
`moveAll` · `tryToConsume` · `tryToLearn` · `installCipher` · `destroyCipher`.

### THE SINGLE MOST IMPORTANT BEHAVIOURAL FACT
**There is no "add item" event — a client cannot ask for an item to be created.** Creation
is unconditionally server-authored via a 1081 push.

**And every 1082 request MUST be answered with a 1081 push or the panel stays locked.** The
client sets `IsWaitingForServer` before sending and it is cleared in **exactly one place** —
inside `LoadInventory()`, which only runs off a 1081 update. **No timeout, no rollback, no
spinner expiry.** A dropped request greys the panel permanently.

**Harvesting needs NONE of these events** — the whole path is server-side.
**Priority:** P0 `moveItem`/`assignToHotBar`/`removeFromHotBar` (dragging is the first thing
a player does, and today all three lock the panel forever) · P1 fix `equipWearable`, add
`unequipWearable` (**gear is currently a one-way door**) · P2 split/remove · P3 `craftItem`.

## THE WRITE PATH — and the real problem
`GameState.ComponentMap[peer][entityId][1081]` is **per-peer**, and `InitAndSerialize`
creates a **fresh Data on every seed**. Three consequences:
1. **N copies of one player's inventory**, one per peer, drifting immediately.
2. **Any re-seed silently resets the inventory to defaults** — unlike 1088, which consults
   `Appearances.Get(entityId)`, 1081 has no side-table. **A second interest request and the
   granted wood is gone.**
3. This is exactly rule 15 — seed by entity, not by component id alone.

**Race behaviour:** the server is single-threaded, so no data race. The failure is logical:
`Data.ToUpdate()` shares the *same* `List` reference, so two in-place mutations compose —
but **any code calling `SetInventoryList(new List(...))` breaks the chain and the earlier
mutation is lost.** Two full-state pushes in one tick: last wins, and if it was built from a
stale copy the first grant is erased. **Never derive a mutation from a `ToUpdate()`.**

## THE KNOWN BUG — premise corrected, three real bugs found
**The `slotType` mutation IS written back.** `ToUpdate()` shares list references and
`Improbable.Collections.List<T>` is a **class**, so the write lands in the stored Data.
**The in-code comment at `InventoryModificationState_Handler.cs:60-63` is factually wrong,
and so is `findings-persistence.md:57`.**

**Bug 1 — 1280 is a copy that is never written back.** The handler calls
`SetItemIds(new List<int>{...})`, which **replaces** the Option's inner value, so the stored
Data stays at the empty seed forever. **Symptom: equipping a second wearable REPLACES the
first**, and any 1280 re-serve serves an empty list.
**Bug 2 —** other peers' server-side copies are never updated; the relay sends correct bytes
but leaves every other peer's Data stale.
**Bug 3 —** any 1081 re-seed wipes everything (see above).

## 1280 — must move in step, and every rule is enforced by unguarded code
Three **parallel arrays** `{itemIds, healths, active}`, indexed positionally.
`GearWearablesVisualizer` requires **both** 1081 and 1280 readers.
1. Worn set comes from 1081; durability from 1280.
2. **`meta["totalHealth"]` is mandatory** — missing, unparseable or `< 0.01` and the item is
   never registered.
3. **An unregistered id in `itemIds` throws EVERY FRAME** (`KeyNotFoundException`), and
   `active` shorter than `itemIds` throws `IndexOutOfRange`.
**Invariants:** all three counts equal; every id present in 1081 with `slotType != "None"`
and a parseable `totalHealth`. Unequip must remove from **all three**.

## PLAN — pure policy, then thin glue
The policy project has **no references**, so it **must not name `ScalaSlottedInventoryItem`**
— define a mirror record and convert at the glue boundary, as `MirrorSendPolicy` already does.
0. `InventoryGeometry` (port of `InventorySpaceChecker`) + tests — including that the seeded
   glider currently straddles the belt row.
1. `InventoryModel` + `InventoryPolicy` with `ValidateForWire` asserting every invariant here.
2. `WearableInvariants` — derive the three arrays **from** the model so they cannot desync.
3. `InventoryStore` keyed by entityId, mirroring `AppearanceStore`.
4. Glue: make the 1081 seed read the store (the 1088 precedent).
5. Glue: `InventoryPush` — the **single** seam that mutates, writes back to every affected
   peer, and sends 1280 → 1081 → 1088 in that order. Nothing else may push 1081.
6. Fix the equip handler: add the `Players.Owns` gate, fix the 1280 write-back, correct the
   wrong comment, add `unequipWearable`.
7. `Grant(entityId, itemTypeId, amount)` on a debug trigger — **"5 wood appears" observable
   with zero harvesting work.** Fix `stacksize` first.
8. The rest of the bus, one event per commit. **Every handled event must end in a 1081 push,
   including on rejection.**

## COULD NOT DETERMINE
Whether the mod's hardcoded reference JSON or the server's 1097 payload wins at runtime
(statically the server's should; **not observed**) — this decides whether `oak` is grantable.
Whether the seeded 1040 really yields `_serverAuthInv == false` (statically yes, the
forgiving path; if true, initial checkout data alone never populates the UI).
The exact visual result of an out-of-bounds placement. Nothing executed.
