# FINDINGS — TOOL SYSTEM

## THE ANSWER: add `1211 InteractAgentState` to `MirrorSendPolicy.AuthoritativeComponents`
**One array element** (`MirrorSendPolicy.cs:125-132`). 1211 is **already seeded**
(`ComponentsSerializer.cs:148`) so no serializer work is needed.

Why that is necessary AND sufficient for one observable signal:
1. `InteractAgentObserver` is on the shipped prefab (verified: root component #57 of
   `Traveller@Player_unityclient`). Its `[Require]` set is 1212 reader (seeded ✔), 1088
   reader (seeded ✔), and **1211 writer — the only unmet one**.
2. On enable it sets `_pendingSlot = 0` (`:172`); `MapSlotToToolType(0) => Salvage`
   (`:331`); `IsToolUnlocked(Salvage)` is already true. So `CurrentItemSlot = 0` and
   `:279` computes `itemSlot = -2`.
3. `-2` reaches `PlayerEquipmentVisualizer.OnHotbarSlotChanged`
   (`acs/PlayerEquipmentVisualizer.cs:52-56`) → `TryEquip(Multitool)`, `Mode = Salvage`.
4. On fire, `InteractAgentObserver.cs:298` sends
   `TriggerUseItemKeyPressed(lookingAtEntityId, lookDirection, CurrentItemSlot, sourcePosition)`
   — **the complete "player used tool T on entity E" tuple, in one event, zero new
   serializer code.**

## CORRECTION 1 — TODAY NO TOOL IS EQUIPPED AT ALL
`PlayerMultitool.Update()` returns immediately unless `Equippable.IsEquipped`
(`acs/PlayerMultitool.cs:192-196`), and the ONLY thing that sets it is
`PlayerEquipmentVisualizer` reacting to `1211.itemSlot ∈ {-2,-5,-3,-6}`. We seed
`itemSlot = 1` (`ComponentsSerializer.cs:148`, 7th ctor arg) → `default: TryEquip(null)`.
With 1211 unwritable, itemSlot stays 1 forever.
**So "the beam already fires locally" was HALF WRONG — nothing is equipped and the beam
never charges.** (A one-line diagnostic alternative: seed `itemSlot = -2`. Equips the
gauntlet without any grant, but produces no server-observable signal.)

## CORRECTION 2 — IT IS 1077, NOT 1160
`PlayerMultitoolVisualizer.cs:24-25` resolves `HealthStateReader` through
`using Bossa.Travellers.Player;` and subscribes `_healthReader.DamageEvent` (`:45,:56`).
Only **1077** `Bossa.Travellers.Player.HealthState` has that event;
**1160** `Bossa.Travellers.Creatures.HealthState` has `Option<float>` fields and **no
events at all**. 1077 is **already seeded** (`ComponentsSerializer.cs:136`).
**So the multitool is missing THREE writers, not four-plus-a-reader. 1160 is a creature
component — do not seed it on players.**

## THE MISSING SET — three writers plus one seed value
| id | component | namespace | need | seed |
|---|---|---|---|---|
| **2105** | MultiToolPlayerState | `Items` | **WRITER** | `(isVisible:false, mode:Salvage/*1*/, salvagerBlastDamage:200)` |
| **2106** | MultitoolSalvagerState | **`Salvaging`** | **WRITER** | `(isOn:false, isJammed:false, isEngaged:false)` |
| **2002** | MultitoolRepairerState | **`Salvaging`** | **WRITER** | `(isOn:false, isEngaged:false)` |
| **1231** | SalvagerAimerState | `Items` | **WRITER** + server-seeded field | `(InvalidEntityId, (0,0,0), (0,0,0), maxBoltDistance: **10.0f**)` |
| 1077 | HealthState (Player) | `Player` | reader — **DONE** | — |

**`maxBoltDistance` is the single most important seed value in this spec.** At the C#
default of 0, `AreWithinDistance(p1,p2,0)` is false for every point, so `lookingAt` is
permanently Invalid and **nothing is ever a target**. 10.0f matches the beam's own
`_maxAimDistance` recovered byte-exactly from the shipped prefab.

**2002 is required even for a pure-harvest MVP** — it is a `[Require]` on the *same*
MonoBehaviour that publishes the salvage shot. You cannot skip repair.

No SpatialOS commands anywhere in the tool system: `ICommandReceiver`/`Commands` are empty
`{}` on every one. Everything is `COMPONENT_UPDATE_OP`, the op we already handle.

## WHAT THE SERVER HEARS — today: LITERALLY NOTHING
Two compounding reasons: no tool is equipped (above), and even if it were, the four
writers are unresolved so nothing publishes. Grep confirms **zero non-doc hits** for
2105/2106/2002/1231/1099/1016/1174 anywhere in the repo.

### After the grants — the exact ordered trace
**Per frame while engaged (~60 Hz):** 1211 (lookingAt, itemSlot=-2, selectedHotbar) ·
1231 (lookingAt; lookHitPoint if moved >0.05 m; lookDirectionEuler if turned >~11°) ·
2105 (isVisible) · 2106 (isEngaged/isOn/isJammed) · 2002 (isOn/isEngaged).
**On button down (once):** 1211 `UseItemKeyPressed{target, lookDirection, itemSlot, sourcePosition}`.
**On release:** 1211 `UseItemKeyReleased{timeButtonHeld}`.
**2.75 s after engage, then every 0.75 s** (warm-up 2 s + charge 0.75 s, +0.2 s beam
pulse) — three ops in this order:
- **A** `2105.ShotEntityEvent{EntityId, Vector3f offset}` — offset **relative to the target**
- **B** `2106.ShotEvent{EntityId, Coordinates shotCoordinate, Vector3d shotDirection}` — absolute
- **C** `2106.DeployedEvent{}` — empty

**A and B are the harvest tick.** They fire ONLY if the ray hit a `Salvageable` whose
`IsSalvageable()` is true AND `hitEntity != null` (`PlayerMultitool.cs:317`).
> **`DeployedEvent` with no preceding `ShotEvent` = "fired at nothing harvestable".**
> That negative result is itself proof the whole chain is live.

**Damage magnitude is NOT on the wire.** `SalvageShootDamage = 200` is a client-local
static; `2105.salvagerBlastDamage` has no client reader. **The server must invent the
yield.**

Repair produces the same A+B (same `ShotEntity` event, `:244`) but **never C**, at a fixed
1 Hz, and only when `IsDamaged()`.

## TIMINGS (prefab-serialized, cross-checked against anti-cheat ValueAssertions)
`MinDeployInterval 0.75 s` (charge/shot) · `WarmUpDurationSec 2 s` (before the FIRST shot)
· `DischargePulseDuration 0.2 s` (events fire at the END) · `MaxJamDurationSec 3 s` (jam is
set by `TakeDamage()` from 1077's DamageEvent) · `RepairToolBeatInterval 1 s`.

## THREE INDEPENDENT RAYCASTS PER FRAME — and they disagree deliberately
| consumer | distance | mask | pattern |
|---|---|---|---|
| `PlayerMultitool` (damage) | **10 m** | 569601 | single ray, nearest hit |
| `SalvagerAimerObserver` (reticle) | **40 m** hardcoded, clamped by `maxBoltDistance` | 37121 | **16 rays**, 2 rings × 8 angles |
| `InteractAgentObserver` | **2000 m** | 37121 | single ray |
Seed `maxBoltDistance = 10` so the reticle agrees with the beam.

## TOOL TAXONOMY — three disjoint categories
- **Gauntlet modes** (Salvage/Repair/Build/Scan) — **innate**, selected by `1211.itemSlot`.
  The `gauntlet_*` inventory rows are UI/hotbar shells, not real items.
- **Hand items** (Pistol, instrument, Food, Photo camera) — real inventory items.
- **Wearable utilities** (Glider, AtlasBoots, InertiaPack, StasisPack, LightSource) —
  `CharacterSlotType.UtilityHead/Utility/UtilityFeet`, all riding the generic 6910 channel.
- **Innate abilities** (Grapple) — no slot. **Already working**: `GrapplingHookNew` has NO
  `[Require]` at all; only `RopeObserver` needs 1098, which is granted.

`PlayerEquipment` enum is the complete hand-item list: `{None, Multitool, ScannerTool,
Pistol, Food, MusicalInstrument}`. **There is nothing else.**

Slot encoding (`InteractAgentObserver.cs:32-38, :279`): hotbar 0→-2 Salvage, 1→-5 Repair,
2→-3 Build/Lifter, 3→-6 Scanner; hotbar 4-7 map to real inventory item ids.

## DEAD — DO NOT BUILD
`GliderState` 1151 / `GliderEquippedState` 1152 (zero references; superseded by 6910) ·
`BombState` 1014 · `DeleteToolState` 1133 · `WiringKitState` · `MultitoolType` enum ·
`GrapplingHook.cs` (legacy) · `SimpleGrapplingHookVisualizer` `[Obsolete]` ·
`MultitoolSalvagerVisualizer` `[Obsolete]`, not on any shipped prefab ·
`RawMaterialSourceState` 1030 · `HarvesterState` 1031 · `SalvagerRepairerState` 1100 ·
`Weapon : UtilityItem` (24-line stub — **there is no melee tool**).
**No cutter item, torch, lantern, shield, pick, wingsuit, bow, rifle, harpoon or spear
exists.**

## 8051 ToolState = 30 DECODED — the value is CORRECT, leave it
A **bitmask** over `ToolType` (`acs/ToolType.cs:4-12`):
`Multitool=1, Salvage=2, Scan=4, Repair=8, Build=0x10, Grapple=0x20`.
`30 = 0b011110 = Salvage|Scan|Repair|Build` — **all four unlockable gauntlet modes.**
Multitool and Grapple are excluded because `IsToolUnlocked` short-circuits them to true
regardless (`ToolBehaviour.cs:102-105`). **30 is the correct complete value.**

The four unlock toasts fire because the diff is `(_lastToolState ^ new) & new` and
`_lastToolState` starts at 0 — they fire **once**, unavoidably, unless we seed 0 and never
grant.

**Highest-severity `[Require]` in the tool system:** `ToolBehaviour` requires the **writer**
for 8050. If that grant were removed, `_behaviour` is null and `IsToolUnlocked` returns
false for **everything including Grapple and Multitool** — the null guard (`:97-101`) runs
*before* the hardcoded shortcut (`:102-105`). Every tool slot would clear. **Currently
satisfied by accident.**

8050 `ToolRequestState` has **zero data fields** — a pure request bus like 1082. We grant
and seed it but have **no handler**, so unlock requests fall through. A handler that ORs
`ToolRequest.tools` into 8051 and pushes back is ~20 self-contained lines.

**There is no tool tier or tool quality anywhere.** Grep for `ToolTier|ItemQuality` yields
only the graphics `QualityManager`. The gauntlet has **no durability model** — its only
failure mode is the 3-second jam.

## 1280 / 6910 CARRY WEARABLE STATE, NOT TOOL STATE
**1280** `WearableUtilsState` — three index-aligned parallel arrays `itemIds/healths/active`.
`GearWearablesVisualizer` matches ids against equipped `UtilityItem.ItemTypeId`, pulls
`totalHealth` from the item's meta map, and **drains health per frame while active**.
**6910** — `bool head, body, feet` + six `Option<float>`. **Slot-generic**: the server is
told "the body utility is active", never "the glider is deployed".

## TWO HAZARDS THE GRANTS INTRODUCE
1. **Relay mis-addressing.** `RemotePlayerMirror.OnComponentUpdate:53-67` ignores the
   inbound entity id and re-addresses to the sender's own player entity. The remote rig
   `Traveller_unityclient` carries **no `InteractAgent*` component at all** (verified: 28
   components), so relaying 1211/1231 pushes updates for components that do not exist.
   **Add a relay allowlist** next to `RelayReliabilityFor` — do NOT just enlarge the remote
   seed (rule 7: bigger seeds NRE visualizer OnEnable chains against default data).
2. **60 Hz flood.** Five of the six streams call `FinishAndSend()` unconditionally every
   `Update()` — ~600 ops/sec inbound at two players, each also relayed. Given the logging
   precedent, instrument before shipping.

## SHIPPED-PREFAB VERIFICATION (UnityPy)
`Traveller@Player_unityclient` root = 107 components. Confirmed: `#21 TreeCuttingBehaviour`,
`#23 PlayerMultitoolVisualizer`, `#24 PlayerEquipmentVisualizer`, `#26/27 Scanner*`,
`#28 PlayerPistolBehaviour`, `#31 PlayerPlacementToolBehaviour`, `#44 UtilitySlotActivatedBehaviour`,
`#57 InteractAgentObserver`, `#59 SalvagerAimerObserver`, `#65 RopeObserver`,
`#91 MultitoolCraftingBehaviour`, `#94 ToolBehaviour`. Child `GrappleHook` carries the
grapple trio.
Recovered serialized values: `_maxAimDistance = 10.0`, `_targetMask = 569601`, `_mode = 1`,
`RepairToolBeatInterval = 1.0`, `SalvagerAimerObserver.LayerMask = 37121`.

## REPAIR — separable in gameplay, NOT in publication
Node contract: **1099** with `isRepairable = true` + **1016** with `0 < health < maxHealth`,
on a prefab whose `SalvageableItemVisualiser.AllowRepair` is true.
**Harvest nodes are explicitly not repairable** (`MaterialSourceVisualizer.IsRepairable() => false`).
Once harvest works, repair is: read `2105.mode`, increment `1016.health` by
`1099.repairAmountPerPeriod`, push. **No new components, no new grants.** But repair
targets are ship parts — a much larger entity graph. **Harvest a rock first.**

## FREE UI POLISH, NO AUTHORITY NEEDED
**8060 `FeedbackListener` is already seeded** (`ComponentsSerializer.cs:356`). Triggering
its `ReceiveSalvageFeedback{itemTypeId, quantity}` event makes `FeedbackVisualizer`
(`acs/FeedbackVisualizer.cs:41-43`, on the player prefab) pop the salvage toast.
Server-authored event on a server-owned component — nothing to grant.

## ORDERED PLAN
**P0 — make tool use observable. ~1 line + 1 test.** Add 1211 to `AuthoritativeComponents`;
assert it in `MirrorSendPolicyTests`. **Exit criterion: a `UseItemKeyPressed` event with a
non-invalid `target`.**
**P1 — relay hygiene, same change.** `ShouldRelay(componentId)` returning false for
1211/1231/1037; rate counter on `RelayToOtherPlayers`.
**P2 — enable the beam's publication.** Four `ComponentsSerializer` branches (model on the
6910 branch at `:196-208`) + four grants. Expect **C only** with no node spawned — the
proof the chain is live.
**P3 — give it something to shoot.** Make seeding **per-entity** first (the `entityId`
param is already in the signature and 1088 already uses it via `AppearanceStore:109`).
Then spawn one node with **1099 + 190602 only** — `MaterialSourceVisualizer` needs nothing
else. Remember Q52.12.
**P4 — close the loop.** A 2106 `ShotEvent` handler that decrements node health and pushes
a full replacement 1081 `inventoryList`. Pickup SFX is free from the 1081 diff. Node-scoped
relay is required (see findings-node-relay.md).
**P5 — follow-ons in cost order:** remote beams (add 2105/2106/2002 to the remote seed,
watch rule 7) · repair · the 8050 unlock handler (~20 lines) · tree cutting (needs the
FSim sim reimplemented — defer).

Handler registration is by **FNV-1 hash of the component factory type name**, not numeric
id (`Update/ComponentUpdateManager.cs:36-53`).

## COULD NOT DETERMINE
The damage→yield formula (Scala GSim; must be invented). Whether `WarmUpDurationSec` is
really 2 s on the shipped prefab (the controller lives on a separate prefab not decoded;
`MinDeployInterval` and `RepairToolBeatInterval` are certain from the anti-cheat asserts).
Semantics of `ShotWorldEvent`/`ToggleModeEvent` (no client raiser found — probably
vestigial). Whether granting 1231 outside the original ACL errors client-side. The real
1099 field values a Bossa metal node carried. `1212.multitoolMode` — the client handler
`OnMultiToolModeUpdated` is an **empty method body**.
