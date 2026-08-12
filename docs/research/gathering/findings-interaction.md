# FINDINGS — INTERACTION & PICKUP

## LEAD: the cheapest interactable is a LOOT RUIN PILE — and it beats both pickup and the beam
**`LootRuinPile1…24` / `LootRuinPileKioki01…12` (36 shipped prefabs) need exactly TWO
components — `1210 InteractiveState` + `1081 InventoryState`** — plus the transform trio that
is **already seeded**. Verified against the shipped asset, not inferred (UnityPy dump of
`LootRuinPile11_unityclient`, path_id 47171): every `[Require]` on that root resolves from
just those two ids.

**Total server work:** one new 1210 branch · one array element (`1211`) in
`AuthoritativeComponents` · one `InteractAgentState_Handler` that answers with a 1210 update
carrying `AddInteract`. **The chest's contents come from the SEED — there is no inventory
mutation anywhere on the open path**, so none of the three live 1081 write-path bugs are
touched.

| | ruin pile | pickup | beam |
|---|---|---|---|
| new grants | **1211 (1)** | 1211 (1) | 2105+2106+2002+1231 (4) |
| new serializer branches | **1210 (1)** | 1099 (+more) | 1099, 1016, 12283, 2103, 1255… |
| entity graph | **1 free-standing** | 1 | deposit + core + crust |
| reply | **one event push, no state mutation** | **a mutating 1081 push** | shotPoints + 1016 + isDestroyed + late-join replay |

**Correcting a prior pass:** it was right that pickup needs 1 grant vs the beam's 4, but wrong
about which object is cheapest. **No cheap dedicated pickup prefab ships** — `MetalNugget`'s
verb is `Default(0)` not `PickUp`, and it needs 1099 (no seed branch exists);
`MetalDepositAtlas` needs a live initialised deposit; picture frames are full ship parts.
**And pickup's reply is the mutating 1081 path.** Verdict: **containers first, pickup second,
beam third.**

## ⭐ THE SURPRISE IS NEGATIVE: THE SHIPPED ISLAND HAS ZERO INTERACTABLES
`949069116@island_unityclient` (29.6 MB, the bundle this server actually serves) holds 8,993
objects and **18 MonoBehaviours**, all island infrastructure. All 1,991 GameObject names are
LOD/coordinate strings; keyword search for loot/chest/container/crate/nugget/station returned
**0 hits**. **There is nothing hidden a player could already press E on.** Every interactable
must be spawned as a separate entity — which is why "just add 1211" alone changes nothing
visible except that the outline never appears on anything.

## WHY NOTHING IS INTERACTABLE TODAY — and the test that proves the fix landed
`InteractAgentObserver` requires 1212 (✔ seeded), 1088 (✔), and **1211 as a WRITER** — seeded
but **absent from `AuthoritativeComponents`**, so the writer never resolves and `OnEnable`
never runs. `OnEnable:177` is what sets `_input.Enabled = true`, so `CanInteract()` is false,
so `PlayerLookingAt.FixedUpdate:82` takes the `ResetLook()` branch **every frame**.
**Consequence: no yellow outline on anything, ever.** That is the test — before you even
press E.

## THE CONTRACT
**1210 (on the object):** `{bool available; EntityId inUseBy; List<InteractionEntry>
interactions; bool syncSchematics}` + event `Interact{verb, playerEntityId, characterUid}`.
`InteractionEntry` = `{verb, radius, lockOnUse, activatedByItem, description,
lockedDescription, exclusiveUse, timeToUse}`.
**1211 (on the player, client-authoritative):** events `InteractWithObject{target, verb}`,
`UseItemKeyPressed`, `UseItemKeyReleased`, `ReleaseInteraction{interactEntityId}`, `ChangeMode`.
**1212 (on the player, server-authoritative):** no events.
`InteractVerb`: `Default, Activate, PickUp, Man, Inventory, Craft, Harvest, Forced, Design,
ReclaimShip, ShipBoost`.
**1284 `InteractiveExpiringOwnershipState` has ZERO client consumers** — do not seed it.

### THE RADIUS TRAP, CONFIRMED — and worse than described
`OnEnable:62-68` does `Interactions.FirstOrDefault(i => i.verb == Verb)`. `InteractionEntry`
is a **struct**, so no match yields `default` → `radius = 0f` → `distance + 0.5 < 0` is never
true ⇒ **no outline, no interaction, no error message.** The visualiser's own inspector
default is unconditionally overwritten because `[Require]` guarantees `_interactive != null`.
**And the interactions list is read EXACTLY ONCE, at OnEnable — there is no
`InteractionsUpdated` subscription anywhere.** A later 1210 update changing it is silently
ignored until re-checkout. Same class as the 1081 `width/height` trap.
**`Verb` is a `[SerializeField]` baked into the prefab — the server cannot choose it.**

### THE COMPLETION CONTRACT — the interaction is not done until the server writes back
| verb | what completes it |
|---|---|
| `Inventory` | **1210 update on the OBJECT** with `AddInteract(Interact{Inventory, playerEntityId, uid})` |
| `Craft` | **1005 update on the STATION** with `PlayerStartCrafting{playerId, schematicId}` |
| `PickUp` | nothing required to avoid a hang; the effect is a **1081 push on the PLAYER**; `1210.SetAvailable(false)` is the depletion tell |

**There is no generic "Press E" prompt** — `UpdatePrompt` only raises tutorial steps, and
**`Default(0)` has no case**, so nugget-class objects get no prompt at all.

**Callback order within one 1210 update** is `available → inUseBy → interactions →
syncSchematics → interact`, so a single update carrying both `SetInUseBy` and `AddInteract` is
safe on first open. **Trap: property callbacks fire on PRESENCE in the update, not on value
change — any server 1210 push including `inUseBy` while the local player already holds it
CLOSES THEIR UI.** Send `inUseBy` once on grant, once on release, never idly.

## EXCLUSIVE USE — a confirmed trap
`InteractionEntry.exclusiveUse` and `.lockOnUse` are **read by NO client code** — exclusivity
is 100% server policy. **On the client a stale `inUseBy` does NOT block another player** (the
lock branch never fires for a non-ship object); the only residue is stuck "being looted" VFX.
**On a naive server it bricks the chest for the process lifetime** — the crafting-busy /
ship-blueprint hard-lock shape again.

`ReleaseInteractiveObject` has four callers, all client-UI-driven. **There is no
distance-based release, no timeout, no death hook, and no disconnect hook.** The server must
implement all four release conditions: explicit release · **peer disconnect** · **distance** ·
**a lease timeout** (1284 is the shape Bossa used, but nothing reads it — just write
`inUseBy = InvalidEntityId` when the timer fires).

## ⭐ A LIVE BUG IN OUR OWN SEED — my earlier fix landed on the wrong argument
Commit `ecd3d76` claimed to fix *"a valid-looking EntityId(0) that triggered spurious
interaction-release events"* — but it changed argument **1** (`equipId`), not argument **5**
(`exclusivelyUsingEntityId`). **The spurious-release bug was still live.**
Why it bites: `EntityId.InvalidEntityId` is **−1** while `IsValid()` tests `Id > 0`. So
`EntityId(0)` is **invalid to `IsValid()` but NOT equal to `InvalidEntityId`**, and
`ReleaseInteractiveObject`'s guard passes — the client emits `ReleaseInteraction(0)` every
time any inventory or crafting UI loses focus. **Now fixed** (args 4 and 5 both →
`InvalidEntityId`).

## CONTAINERS — the inventory UI DOES reuse 1081/1082 against a different entity
Confirmed. The container's inventory is **1081 on the container entity**; the UI is told which
entity via `ChangeInWorldStorageEntityID`; movement is **1082 addressed by entity id on both
ends** (`crossInventoryMoveItem{src, dst, …}`, `moveAll{src, dst}`). **1082 is already
authoritative — no new grant needed for looting.** But **both** entities need a 1081 push per
move or both grids grey out permanently.
Same `width/height/hasBelt/beltRow`-read-once trap as the player's.
**60 containers ship** (24 ruin piles + 12 Kioki + chests, crates, barrels, ammo boxes).
`LootChest_001` is equally cheap and gets an **opening animation free**.
**Ship containers cost one more component** (1236, unseeded) — prefer island loot piles.

## CRAFTING STATIONS — gated TWICE, and one gate is client-side
The station UI opens **only** when the server fires `PlayerStartCrafting` on the station's
1005 (no client writer for 1005 exists anywhere). But before that,
`InteractAgentObserver:352-357` refuses outright if `!_isShipBuildingAware` — **no packet
leaves the client at all**. Check that flag before blaming the server.
The station still needs a correct 1210 entry with `verb == Craft` even though the payload
rides on 1005. **Strictly more expensive than a container**; the personal Multitool-craft path
needs no world entity and no interaction at all.

## ⭐ REASSURING CORRECTION: 1211 IS NOT A FLAT 60 Hz STREAM
`FinishAndSend` runs `FinishAndSend_ResolveDiff`, which clears unchanged fields and **returns
false when nothing changed — no packet is sent**. Traffic is proportional to how often
`lookingAt`/`itemSlot`/`selectedHotbar` actually change: meaningful while the camera sweeps,
**near-zero when still.** This materially lowers the desync risk the skeptic flagged.
Still add an id filter: the remote rig carries **no `InteractAgent*` component at all**.

## VERB CENSUS — 191 instances in `resources.assets`
**Inventory 60** (containers) · **Default 50** (`MetalNugget`, `MetalDepositCore/Scrap`,
`HarvestableRock`, `FuelDeposit`, `Egg`, `WoodlandTree*`) · **Activate 38** ·
**PickUp 16** (`Bomb`, `Lifter`, `Lock`, `MetalDepositAtlas`, picture frames) ·
**Man 15** (chairs, helms, cannons) · **Craft 10** · **Design 2**.
Everything the interaction system needs is **already on the Traveller prefab we spawn**
(`#15 PlayerLookingAt`, `#56 InteractAgentObserver`, `#57 TimedInteractionController`, …).

## ORDERED PLAN
**0.** Grant 1211. *Verify: the yellow outline appears.* **0b.** Fix the 1212 seed (done).
**1.** Add the 1210 branch — **entity-aware**, verb matching the prefab's baked `Verb`, radius
> 0, **or nothing is ever interactable, silently.** Without the branch the whole AddComponent
batch aborts (`failOnComponentInitError: true`).
**2.** Make 190602 entity-aware — **or the chest spawns inside the island.**
**3.** Spawn the entity via the island's sync-step pattern; **allocate the id once, shared
across peers.** Context is ignored for non-`Traveller` names.
**4.** Give the container its own 1081 (entity-aware).
**5.** The handler — **ignore `target == InvalidEntityId`** (the "nothing here" ping), validate
ownership and distance, never re-send `inUseBy` unchanged.
**6.** The four release conditions. **7.** An id filter for 1211 in the relay.
**Also:** `SendOPHelper.cs:222` passes `updates.Count` instead of `cupdates.Count` — every
step that pushes 1081+1210 together hits it.

## COULD NOT DETERMINE
**The authentic `interactions` values Bossa shipped** — SpatialOS state, in no bundle. Radius,
timeToUse, descriptions **must be authored**; the suggested 3 m / 0.5 s is invented.
The `Verb` byte-parse is **inference, not typetree** (no typetree exists on any MonoBehaviour
in this build) — self-consistent across all 191, but re-verify first if a chest is unpressable.
Whether `_isShipBuildingAware` is ever true in our session. What `1212.aimType` /
`equipInventoryId` do. **Only 1 of 255 island bundles was scanned** — the zero-interactables
result is extrapolation. Nothing was executed.
