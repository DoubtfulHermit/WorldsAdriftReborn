# PLAN — LOOT CONTAINERS

*Branch `feat/loot-containers`, cut from `main` @ `2fc5846`.*

Retail Worlds Adrift scattered lootable ruin piles, containers and chests across
every island. This server spawns none. This plan makes them real, in phases, and
records what is recovered evidence and what this project invented.

## PROVENANCE KEY

| Label | Meaning |
|---|---|
| **PROVED** | Read directly out of the decompiled client or shipped game data. |
| **RECOVERED** | Reconstructed from a retail artefact (our recovered `itemData.json`, the shipped icon atlas, the prefab key census). |
| **INFERRED** | Reasoned from evidence, not directly observed. |
| **WAREBORN TUNING** | This project's number. No retail source. Balance only. |

---

## 0. WHAT THE RESEARCH SETTLED

### 0.1 The client contract for a container — PROVED, and it is NOT "just a 1081"

The prior audit's load-bearing claim was that *"the client contract for a
container is just a plain `1081` on the container entity"* and that serving it is
the single blocker. **That is wrong in three separate ways.** The real contract,
read off `acs/Bossa.Travellers.Visualisers/InWorldInventoryVisualiser.cs`:

```
[Require] private InteractiveState.Reader _interactState;    // 1210
[Require] private InventoryState.Reader   _inventoryState;   // 1081
```

Two `[Require]`d readers, not one. A Unity SpatialOS visualiser does not enable
until **every** `[Require]` resolves, so a container served 1081 and no 1210 is
exactly as dead as one served neither — the same shape as the loom's missing
`1264`. And the UI does not open on either component's *data*; it opens on an
**event**:

```
_interactState.InteractTriggered.Add(OnObjectInteractionTriggered);   // OnEnable
... if (verb == InteractVerb.Inventory) { SyncInventory(); DisplayInventoryUI(); }
```

So the complete list is:

1. **190602 TransformState** — already generic in this server.
2. **1210 InteractiveState** carrying an `InteractionEntry{verb=Inventory, radius>0}`.
   Serving no 1210 means no E prompt *and* no visualiser.
3. **1081 InventoryState** — `{width, height, hasBelt, beltRow, allowedItems, inventoryList}`.
   Read exactly once at `OnEnable`; a later resize is a lie.
4. **An `Interact{verb=Inventory, playerEntityId}` event echoed on the container's
   1210** when the player completes the interaction. Without it the panel never
   opens, whatever else is served.
5. **`crossInventoryMoveItem` on the player's 1082**, or the player can look at
   the loot and not take it.

Points 2, 4 and 5 are absent from the audit's account. Point 5 is the one that
would have bitten hardest: `InventoryModificationState_Handler.cs:212` refuses
`crossInventoryMoveItem` outright with *"no second inventory exists yet"*, and
`:215` refuses `moveAll` the same way. **A container is that second inventory.**

The audit is right that the same fix unblocks craftable storage containers —
`Ship/PartInteractionPolicy.cs:53-58` blocks trunk/mountedBox/storageContainer/
shippingContainer on exactly this. That file's own comment is more accurate than
the audit's summary: it already names *"1081 InventoryState (+ inUseBy handshake
+ event_interact echo)"*.

**One further trap the audit did not see.** The 1081 serve branch
(`ComponentsSerializer.cs:618`) is already entity-generic — it calls
`InventoryService.ForEntity(entityId)` with no player gate. But `ForEntity` falls
back to `InventoryWire.DefaultModel()`, which is *the player starter kit*: a
10×18 belt grid pre-filled with gauntlets and stash items. Serving 1081 on a
container today would therefore not be empty — it would be **a chest containing a
set of gauntlets**. The container model has to be bound before the first serve,
not after.

### 0.2 Did containers drop SCHEMATICS? — the direct answer is NO

**A schematic was a real inventory item** — PROVED. `itemTypeId == "schematics"`
is handled literally in four places: a LEARN action and a SALVAGE action on the
tooltip (`acs/Travellers.UI.PlayerInventory/InventoryTooltipPopup.cs:182-188`,
`:113-119`), an icon composed from the meta key `overrideIconName`
(`acs/Travellers.UI.Framework/UIInventoryItemImage.cs:96-102`), and a
`schematicJSON` meta key resolved through `SchematicsReferenceStore`
(`acs/ScannableData.cs:372-382`). Retail's own term for the un-learned form is
`maxPhysicalSchematics` (`gencode/Bossa.Travellers.Player/SchematicsLearnerClientStateData.cs:8-18`).

**But the source of that item was the knowledge tree, not loot** — PROVED.
`KnowledgeUseResponseType` (`gencode/Bossa.Travellers.Scanning/KnowledgeUseResponseType.cs`)
lists `FullInventory` as a failure mode of *spending knowledge at a node*. A node
cannot fail on a full inventory unless the node puts an item in it. The knowledge
screen agrees: it shows *"Inventory full / Make some room in your inventory!"* on
purchase (`acs/Travellers.UI.Knowledge/KnowledgeManagerScreen.cs:736-737`) and
its animation enum contains `SPAWN_SCHEMATIC` (`:22`).

So the retail progression route was **scan → knowledge → spend at a node → a
physical schematic item → LEARN it**. Not chests.

**Could a container have held one anyway?** Structurally yes — `IslandLootStateData`
carries a plain `List<ScalaSlottedInventoryItem>`, which can hold any item. But
there is **no loot table anywhere in the shipped client**: `lootTable|dropTable|
dropChance|dropWeight|generateLoot|lootItem|lootReward` return zero hits across
`acs/`, `gencode/` and `ecs/`. Loot composition ran on the GSim worker and did
not ship.

> **Verdict: schematics from loot containers is NOT SUPPORTED by any surviving
> artefact.** Every schematic acquisition path visible in the shipped client runs
> through the knowledge tree. This plan does not put schematics in chests, and
> anyone who later wants to must label it WAREBORN TUNING, not retail.

### 0.3 What containers DID hold — scrap, and we have the real list

The only item family with an independent structural link to loot is **scrap**:
`RuinLootPreprocessor.cs:33` sets the ruin pile's open sound to `"Play_Scrap_Open"`.
PROVED.

Our recovered `itemData.json` carries **137 `scrapItem-*` rows**, 134 of them
category `Salvage` with a real `rewards` block. These are real retail ids: every
row's `iconName` matches an entry in the shipped icon atlas (250 `scrap items/*`
icons), and the decompile handles the `scrapItem-` prefix at
`InventoryTooltipPopup.cs:113` and `ScannableData.cs:368`, reading
`Meta["title"]`/`Meta["description"]` — exactly the `metadata` block shape the
data file has. Two-way match. RECOVERED.

**Correction to the prior audit:** scrap did **not** salvage into cloth, leather,
glass or pigment. Across all 134 `rewards` blocks the yields are exclusively
metals, woods and fuel — iron 30, tin 20, nickel 19, steel 18, lead 17, silver
17, bronze 14, titanium 13, copper 12, aluminium 9, elm 9, chestnut 9, gold 9,
fuel 8, birch 8, cedar 6, oak 6, hemlock 5, tungsten 3, ash 3, palm 3. PROVED
from the shipped data. The cloth/leather/glass/pigment story belongs to
post-Update-27 creature drops, not to scrap.

**`rewards` is keyed by TIER.** `{"3": {"a": 80, "q": 6, "item": "titanium"}}`;
a `.1`/`.2` suffix is a second or third yield at the same tier. Distinct tiers
present per item give a natural island-tier keying:

| Island tier | Scrap items whose rewards carry that tier |
|---|---|
| 1 | 41 |
| 2 | 50 |
| 3 | 32 |
| 4 | 86 |

**This is the loot table.** Which items may appear on a tier-N island is
RECOVERED from the data's own tier keying. Only *how many* and *how likely* are
WAREBORN TUNING.

### 0.4 How many containers an island gets — the FORMULA is recovered

`acs/LootablePerAreaDataVisualizer.cs:50-62`, PROVED verbatim:

```
DoMath(area, min, areaForMin, max, areaForMax, expLerp):
    if area < areaForMin: return min
    if area > areaForMax: return max
    f = (area - areaForMin) / (areaForMax - areaForMin)
    return min + pow(f, expLerp) * (max - min)

containers = (int)(DoMath(...containers...) * extraLootContainersMultiplier)
chests     = (int)(DoMath(...chests...)     * extraLootChestsMultiplier)
databanks  = (int) DoMath(...databanks...)          # no multiplier
```

`area` is **mostly-flat surface area**, not island area. Ruins are absent from
the formula — ruin piles were prop-placed, not area-scaled.

The 19 tuning fields on `1244 LootablePerAreaDataState` did not ship, so the
**shape is PROVED and the constants are WAREBORN TUNING**. One calibration anchor
exists and it is weak: the survey's real databank counts across 254 islands are
247×5, min 3, max 5, correlation with flat area 0.09. That pins
`maxDataBanks = 5` (RECOVERED) and shows the formula saturated for essentially
every island — i.e. retail's own budgets were near-flat in practice. Container
counts are therefore chosen for feel, not fitted.

### 0.5 How a container SITS ON THE GROUND — PROVED, and it is the fix for the log bug

`acs/IslandDataBankAndLootableSpawnerVisualizer.cs` is retail's own placement
pass and it answers the grounding question outright:

```
:64   min separation: (a-b).sqrMagnitude < 400f              → 20 m
:64   clearance:      Physics.CheckBox(p + up*1.75, half 1.6) → 3.2 m box, 1.75 m up
:51   flatness:       LootablePerAreaDataVisualizer.FlatnessThreshold
:100  position = surfacePoint - normal * Random(0.15 .. 0.30)
:101  rotation = Euler(Random(-5,5), Random(0,360), Random(-5,5))
                 * PointTo(Y -> normal, Z -> up)
```

**The prop is SUNK 15–30 cm INTO the surface along the normal, and its up-axis is
aligned to the surface normal, then jittered ±5°.** That is precisely the
discipline the falling-log bug lacks: `TreeFall.cs:441-442` keeps the log's
position *constant* and rotates about the entity origin, so a trunk's centreline
ends at ground level and half the cylinder is under the terrain, or hanging in
air on a slope (`TreeFall.cs:62-63` admits it).

Containers will use retail's rule: a strict flatness gate, a **0.22 m sink along
the surface normal**, 20 m spacing, and a deterministic yaw. Nothing floats,
nothing half-buries, and none of it needs `FallingLogService`.

### 0.6 Refill was real — PROVED from the schema

`IslandLootSpawnerStateData` keeps three parallel arrays per category:
`*BaseBudget` (`List<float>`), `*SpawningTime` (`List<long>`), `*Opened`
(`List<bool>`), plus `didInitialSpawn`. The per-instance replica
`IslandLootReplicatedDataStateData` is exactly `{long spawningTime; bool opened;}`.
`baseBudget` being a **float per container** is the strongest structural
statement available about contents: retail rolled a *value budget*, not a fixed
item list. INFERRED mechanism; PROVED schema.

(`1053 RefillInventoryItemWhenEmpty` is **not** this. It lives in
`Bossa.Travellers.Player.Ai` and is a single-item NPC top-up. Do not cite it.)

### 0.7 Prefabs — all present and client-resolvable today

`WorldsAdriftRebornGameServer.Multiplayer/Ship/client-entity-prefabs.txt` already
contains `lootchest_001`, `lootchest_kioki`, `lootcontainer_001/002/003`,
`lootcontainer_kioki_01..03` and `lootruinpile1..24` — so
`ClientEntityPrefabs.CanResolve("LootChest_001")` returns true right now. Send
the **bare** name; the client appends `_unityclient` itself.

The `_kioki` suffix is an art-set variant. INFERRED that it was biome-driven; the
mapping is not in any artefact. **`IslandLootSpawnerCategory.Marauder` has no
dedicated prefab** — every `marauder*` key in the census is furniture, an
instrument or an equippable. Marauder loot reused the container prefabs at camp
anchor points. INFERRED.

---

## 1. THE PHASES

Ordering rule: **a container a player cannot loot is a dead prop**, the same
failure the loom is in. So Phase 1 is the whole vertical slice — placed, spawned,
streamed, opened, and emptied into the player's bag. Nothing ships half-open.

### Phase 1 — A chest you can find, open and empty

**Delivers**

| Layer | Work |
|---|---|
| Pure loot model | `Loot/LootContainerKind.cs`, `Loot/LootBudget.cs` (retail `DoMath`), `Loot/LootTable.cs` (tier → scrap ids, deterministic roll), `Loot/LootContainerInventory.cs` (grid layout of rolled items) |
| Placement | `LootContainers.cs` (AssetName/KeyPrefix/Placement/PositionAt/CountFrom), `Resources/HavenSurface.cs` `LootConfig()`/`LootLocals()`, `Islands/release-loot-placements.json` + `ReleaseLootCatalog.cs` generated by `tools/world-import/generate-release-loot-placements.py` |
| Streaming | `ResourceInterestPolicy.IsStreamedResourceKey` gains `loot-` |
| Spawn | `WorldEntities` registration + `LootContainerEntity(i)`, env gate `WAREBORN_SPAWN_LOOT`, `WorldResourceActivation` ledger branch |
| Serve | `ComponentsSerializer` 1210 branch (verb `Inventory`) and 1081 branch bound to the container model, not `DefaultModel` |
| Open | `InteractAgentState_Handler` routes `verb == Inventory` on a loot key to a new `LootService.OpenContainer`, which echoes `Interact` on the container's 1210 |
| Take | `InventoryModificationState_Handler` implements `crossInventoryMoveItem` and `moveAll` through a new pure `CrossInventoryPolicy` |
| Admin map | `IslandResourceInventory.LootContainers` stops being `0`; a new **admin-only** asset `admin-map-loot.js` draws the island-card block |

**A player can newly DO:** walk up to a chest on any seeded island, get the `E`
Inventory prompt, open it, see real retail scrap inside, and drag it into their
bag.

**Dependencies:** none outside this branch. The scrap items already exist in
`itemData.json`.

**Schema migration:** **NO.** Container contents live in the existing in-memory
`InventoryStore` under a session key, exactly as an unbound player inventory
does. Nothing new is written to Postgres.

**Networked state:** **YES.** New entities per island, plus two 1081 pushes per
loot move. **Soak gate required** before any deploy.

**What could go wrong**
- *A chest full of gauntlets.* `InventoryService.ForEntity` defaults to the
  player starter kit. The container model must be bound before the first 1081
  serve or the first thing a player sees is four gauntlets in a ruin pile.
- *Silent non-enable.* Serving 1081 without 1210 leaves `InWorldInventoryVisualiser`
  disabled with no error. The 1210 branch must land in the same change.
- *Prefab case.* `client-entity-prefabs.txt` matching is lower-cased, but the
  AddEntity op carries what we send. `LootChest_001` is the census spelling.
- *Streaming budget.* Each container is another entity on an island's checkout
  set, at ~0.24 s per entity per peer. The per-island cap must stay small beside
  the tree ceiling.
- *Item footprints.* Scrap runs up to 5×3. A grid too small silently drops rolled
  items; the layout pass must report what it could not place.

### Phase 2 — Looted state that survives

**Delivers:** retail's `opened` + `spawningTime` per container, so an emptied
chest stays empty and refills on a timer rather than on every relog.

**A player can newly DO:** nothing new — but the world stops resetting itself,
which is the difference between loot and a vending machine.

**Dependencies:** Phase 1.

**Schema migration:** ⚠️ **YES, IF made durable across restarts.** A
`loot_container_state` table keyed by container key. **This is the phase that
needs game server and login server to deploy TOGETHER.** A split deploy has
already destroyed a player's progression once on this project. Keep it isolated:
Phase 2a is the in-memory ledger (no migration, survives a session), Phase 2b is
the Postgres write-through (migration, deploy in lockstep). Ship 2a first.

**Networked state:** one extra 1081 push on refill. Soak gate.

**What could go wrong:** a refill timer that fires while a player has the panel
open would rewrite the grid under their cursor.

### Phase 3 — Variety: chests, ruin piles, and the Kioki art set

**Delivers:** the three retail categories as distinct kinds
(`Ruin`/`Container`/`Chest`) with their own budgets, prefab families and loot
richness, plus the `_kioki` variant selected per island culture.

**A player can newly DO:** tell a rich chest from a scrap pile at a glance, and
read an island's culture off its props.

**Dependencies:** Phase 1. Independent of Phase 2.

**Schema migration:** NO.

**Networked state:** more entities per island. Soak gate.

**What could go wrong:** the Kioki↔culture mapping is INFERRED. Getting it wrong
is cosmetic, but it must be labelled, not asserted.

### Phase 4 — Scrap becomes worth taking (SEAM: `feat/resource-economy`)

**Delivers:** salvaging a `scrapItem-*` out of the inventory into its recovered
`rewards` yields.

**This phase is NOT MINE.** The `feat/resource-economy` branch owns what scrap
turns into. The seam runs both ways and should be said plainly in both plans:

> **Their salvage recipes are useless until Phase 1 lands** — nothing in this
> world currently produces a single `scrapItem-*`, so every salvage rule they
> write is unreachable.
> **My containers are pointless until their side lands** — a bag of Tonking Pucks
> that cannot be turned into metal is a bag of souvenirs.

Phase 1 is the dependency. Nothing in Phases 1–3 touches their files.

**One correction to hand them:** the recovered scrap `rewards` are metals, woods
and fuel. If their design assumes scrap → cloth/leather/glass/pigment, that
assumption came from the audit and the shipped data contradicts it.

### Phase 5 — Marauder camps and compass chests

**Delivers:** `IslandLootSpawnerCategory.Marauder` anchors, and `1265
AtlasCompassChestState` — the carried compass that breaks open into a chest at a
seeded target.

**Dependencies:** Phases 1–3. Lowest value, highest invention: no marauder
container prefab shipped, so camp anchor positions are entirely WAREBORN TUNING.

**Schema migration:** NO.

---

## 2. WHAT ONLY A LIVE CLIENT CAN SETTLE

- That `InteractiveObjectVisualizer` on the shipped loot prefabs accepts a
  server-authored `InteractionEntry` rather than a baked one. The helm taught us
  this cache is set once at `OnEnable` and a mismatched verb yields no prompt at
  all.
- That the `Interact` echo on the container's 1210 reaches
  `OnObjectInteractionTriggered` — the client also requires
  `_interactiveVis.IsWithinInteractRadius()` at `DisplayInventoryUI`.
- That the container's chosen grid width/height renders, since the client reads
  those exactly once.
- Whether `LootChest_001`'s `Animator`-gated open VFX plays without the server
  driving anything.
