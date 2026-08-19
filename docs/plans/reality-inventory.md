# REALITY INVENTORY — what the shipped client CONTAINS, versus what this server implements

**Written:** 2026-08-20. **Branch:** `docs/reality-inventory`, cut from `main` at `a3a2621`.

**Scope:** an *enumeration of the shipped client's contents*, category by
category, diffed against this server. Deliberately **not** an audit of our own
claims.

---

## 0. WHY THIS DOCUMENT EXISTS, AND WHAT MAKES IT DIFFERENT

Twice this project has designed around the absence of something that was
sitting in the shipped client the whole time.

1. **Fuel was built per-hull** because an agent searched `resources.assets` for
   a "fuel tank" prefab, found none, and concluded ships had no tank. The tank
   exists. Retail calls it the **Power Generator** (`PowerGenerator01`).
2. **Instrument placement was patched to force mounting onto railings**,
   because nobody knew **bar pipes** existed. `BarPipe_unityclient` and
   `BarPipeBent_unityclient` are baked into `resources.assets`, carry the full
   ship-part component stack, and have icons filed under `ship parts/`.

Both misses share one root cause, and it is not laziness:

> **The decompile cannot tell you the name of a thing you have not thought to
> search for.**

That is not a figure of speech. It is a measured property of this codebase.
The prefab-enumeration pass done for this document grepped every one of the
353 entity-prefab names against the entire decompile at
`/home/ttanurhan/Games/WAReborn-decompiled/` and got **zero hits**. Prefab
names live purely in asset data; they never appear in code. A code search for
`"fuel tank"` was therefore guaranteed to fail no matter how thorough it was.

**The method this document uses instead — and the method that should be used
from now on — is to enumerate contents, not to test hypotheses.** You cannot
fail to think of a name that is printed in a list in front of you.

### 0.1 Provenance key

The repo's existing labels, unchanged.

| label | means |
|---|---|
| **PROVED** | read directly off shipped bytes — the decompile, the extracted asset data, or this repo's own code |
| **RECOVERED** | reconstructed from surviving shipped data (a schema, an extracted table, a component contract) |
| **INFERRED** | a reasoned conclusion from PROVED facts; could be wrong |
| **WIKI** | community sources. Weakest. Their job here is to say *what to look for*; the client then confirms it |
| **WAREBORN TUNING** | ours. Not Bossa's. Never to be cited as recovered |

### 0.2 The three enumeration oracles

Everything below is diffed against one of three complete, closed lists. Each
was independently re-derived for this document rather than trusted.

| oracle | count | what it is | provenance |
|---|---|---|---|
| **the entity-prefab table** | **353** | every `entityprefabs/<name>_unityclient` baked into `resources.assets` | **PROVED** — four independent extractions agree exactly (see §0.3) |
| **the icon catalogue** | **1,010** | every icon path in the client's own icon atlas, `docs/research/valid-icons.txt` | **PROVED** — extracted from the shipped atlas; already used as a test oracle by `ReferenceDataCrashSafetyTests` |
| **the component index** | **444** | `component-map.tsv` — every SpatialOS component id the game's ECS defines | **PROVED** — from the decompile |

An icon is weaker evidence than a prefab, but it is *not weak*. An artist
authored, named, sized and shipped a 2×2 sprite called
`crafted items/2x2_timed_explosive`. That is a thing the game had.

### 0.3 How the 353 was established, so a negative result means something

Four methods, agreeing byte-for-byte:

- `strings -n 4` over `resources.assets` (610 MB) → 353 unique `_unityclient`
- `strings` over `globalgamemanagers` → the same 353, stored lowercased as
  asset-DB keys
- UnityPy 1.25.3 `GameObject.m_Name` enumeration of `resources.assets` → 353
- this repo's pre-existing `docs/research/loop/data/prefab-names.tsv` → 353,
  `diff` clean

`sharedassets0.assets`, `sharedassets1.assets`, `globalgamemanagers.assets`,
`level0`, `level1`, `resources.resource` and `sharedassets0.resource` were all
scanned and contain **zero** entity prefabs. So "not in the 353" genuinely
means "not in the shipped client", not "not in the one file I looked at".

Every prefab exists in both `_unityclient` and `_unityworker` flavours (706
GameObjects). None is client-only.

---

## 1. HEADLINE NUMBERS

### 1.1 Entity prefabs

| | count |
|---|---|
| entity prefabs shipped in the client | **353** |
| referenced *anywhere* in our server source (including comments) | **161** |
| **never mentioned once, anywhere** | **188** |

"Referenced" is generous — a name appearing only in a code comment counts.
The real implemented figure is lower. Read 161 as an upper bound.

### 1.2 The icon catalogue, by category

Shipped versus referenced anywhere in server code or data (case-insensitive;
`docs/research/valid-icons.txt` is all-lowercase while `itemData.json` is
mixed-case, and a naive case-sensitive diff understates our coverage badly).

| category | shipped | referenced | **GAP** |
|---|---:|---:|---:|
| `scrap items/` | 250 | 145 | **105** |
| *(root, no folder)* | 187 | 24 | **163** |
| `clothing/torsos/` | 94 | 81 | 13 |
| **`ship parts/`** | **81** | **38** | **43** |
| `character customisation/` | 68 | 0 | **68** |
| **`foods/`** | **68** | **1** | **67** |
| `clothing/heads/` | 59 | 55 | 4 |
| `clothing/legs/` | 46 | 28 | 18 |
| `materials/` | 30 | 1 | **29** |
| `crafted items/` | 27 | 14 | 13 |
| `procedual ship parts menu icons/` | 19 | 2 | **17** |
| `metals/` | 18 | 15 | 3 |
| **`ship parts/furniture/`** | **17** | **0** | **17** |
| `woods/` | 13 | 8 | 5 |
| `lifetime knowledge nodes/ship parts/` | 8 | 0 | 8 |
| **`turrets/`** | **7** | **0** | **7** |
| `alliance/` | 6 | 0 | 6 |
| `hud/` | 3 | 0 | 3 |
| `tutorial/` | 3 | 0 | 3 |
| `crew/` | 2 | 0 | 2 |
| `atlas/` | 1 | 0 | 1 |
| `pets/` | 1 | 0 | 1 |
| `misc craft materials/` | 1 | 1 | 0 |
| `steam inventory bundles/` | 1 | 1 | 0 |
| **TOTAL** | **1,010** | **414** | **596** |

The maintainer's known measurement — *98 ship-part icons, 36 referenced* — is
confirmed and now slightly improved: 81 + 17 furniture = **98 shipped**, **38
referenced** (BarPipe and BarPipeBent were added after that count was taken).

**Four rows of that table are NOT gaps, and saying so is part of the job.**
An honest inventory has to subtract its own false positives or it becomes
another kind of unreliable:

- **`character customisation/` 68 → 0** is expected. Appearance travels as an
  **opaque client-published key/value map** (`AppearanceStore.cs`), so the
  server never names a hair or a face. The icons are pure character-creator
  UI. *Not a gap.* (1074 `CustomisationSenderState` being unserved is a real
  and separate question.)
- **`hud/` 3 → 0** — all three are VOIP indicators, and VOIP is **force-disabled**
  at `WAConfig_Patch.cs:242`. A decision, not an omission.
- **`tutorial/` 3 → 0** and **`crew/` 2 → 0**, **`alliance/` 6 → 0** — client-side
  UI chrome. The *systems* behind alliance and crew exist (§7.4); their icons
  simply have no server-side name.
- **`scrap items/` 250 → 145** overstates the gap: the 105 unreferenced ones
  are variant art for a salvage table that already works by tier.

Subtracting those leaves the real headline gaps: **ship parts, ship-part
furniture, foods, materials, procedural part tiers, turrets, and the 163
unfoldered root icons.**

### 1.3 The ship-part boundary is objectively PROVED, not guessed

This is the strongest single result in this document, and it should end all
future argument about what does and does not count as a ship part.

Classifying all 353 prefabs by *component* rather than by keyword, three
markers select **exactly the same 98 prefabs**:

```
ShipPartVisualizer (98)  ==  ShipPartShipyardInformationVisualizer (98)  ==  PlacementRules (98)
```

**98 prefabs carry the ship-part component stack. The client ships 98
`ship parts/` icons. The two 98s are independent measurements of the same
set.** That is a hard, closed boundary: it is the complete list of things a
player could attach to a hull.

Our `LoosePartCatalogue` implements **38** of them.

Two corrections this classifier produced that keyword search would have got
wrong in both directions:

- **`WallSegment` is NOT a ship part.** 7 components, no `Rigidbody`, no
  `PlacementRules` — static island/ruin geometry. A name-based sweep would
  have filed it as buildable and wasted effort.
- **The `Turret*` family is NOT a ship part.** They carry
  `IslandTurretProjectileShootingVisualizer` — they are **island defences**,
  not ship weapons. This materially changes what implementing turrets means
  (see §3.4).

### 1.4 Item and schematic data

| | shipped/authored | ours |
|---|---:|---:|
| rows in `itemData.json` | 396 | 396 |
| distinct `ship parts/` icons referenced by `itemData.json` | — | 37 |
| schematics in `schematicData.json` | — | **62** |
| schematic **categories** the client ships icons for | **20** | **4** |

The 20 shipped schematic-category icons are a complete, authored taxonomy of
retail's crafting menu, and we implement four of its branches:

`cannon` · `clothing` · `communications` · `cooking` · `crafting` ·
`engine` · `firstaid` · `flightinstruments` · `furnciture` *(sic — Bossa's
typo, shipped)* · `sails` · `shipbuildingtool` · `shipdecoration` ·
`shipshelm` · `skycore` · `storage` · `structural` · `swivelgun` · `utility` ·
`weapons` · `wing`

Our four `schematicData.json` categories are `CraftingStation` (39),
`Personal` (18), `Cooking` (4) and `Shipyard` (1). **PROVED** that retail's
menu had five times the breadth.

The client also ships five **schematic rarity capsules** —
`schematic_capsule_common / uncommon / rare / exotic / legendary` — plus four
`item_schematic_*` rarity badges. We have a `rarity` field on item rows but no
capsule/rarity tier system. **PROVED (icons)**.

---

## 2. CATEGORY: SHIP PARTS

### 2.1 What the client ships — all 98

*(see §1.3 for why this list is exactly 98 and not a judgement call)*

### 2.2 What we implement — the 38 in `LoosePartCatalogue`

`AirspeedIndicator` · `Altimeter` · `ArtificialHorizon` · `BarPipe` ·
`BarPipeBent` · `Barrel01` · `ContainerLarge` · `ContainerMedium` ·
`ContainerMount` · `ContainerSmall` · `CoreAirfilter` · `CoreAtlasEnhancer` ·
`CoreCircuitryNetwork` · `CoreComputer` · `CoreCoolantSystem` ·
`CoreEfficiencyModule` · `CoreGenerator` · `CoreMain` · `CoreStabiliser` ·
`Cupboard` · `Deck01` · `FuelGauge` · `HeadingIndicator` · `Helm01` ·
`Horn01` · `Lamp01` · `ModularEngine` · `ModularWing` · `Panel01` ·
`Panel02` · `Panel03` · `PowerGenerator01` · `RailingCorner` ·
`RailingStraight` · `Respawner01` · `Sail01` · `Stairs1` · `Window01`

### 2.3 THE DIFF — 60 ship parts the client ships and we do not

Grouped by what a player would say about it, not by asset type.

#### A. Every ship part exists in **wood and metal**. We ship one of each pair.

**PROVED (icons).** The client has `barrelmetal`/`barrelwood`,
`deckmetal`/`deckwood`, `cupboardmetal`/`cupboardwood`,
`helmmetal`/`helmwood`, `generatormetal`/`generatorwood`,
`stairsmetal`/`stairswood`, `railcornermetal`/`railcornerwood`,
`railstraightmetal`/`railstraightwood`, `containerlargemetal`/`…wood` (and
medium, small, mount), `panellargemetal`/`…wood` (and medium, small) —
**23 metal/wood pairs.**

The prefab table has only one prefab per pair (`Deck01`, `Helm01`,
`Cupboard`, …), so the material is not a different prefab: it is
`HullMaterials` / `ComponentMaterialColors.SetMaterials` choosing the
dominant wood and the dominant metal. **We already have `HullMaterials`.**
What we do not have is the *icon* switching to match, so a wooden cupboard
shows a metal cupboard's picture in the inventory.

**INFERRED, and the single cheapest item in this document:** this is an icon
selection rule keyed off the part's dominant material, not 23 new parts.
It closes 20-odd rows of the icon gap for one function.

#### B. Ship-part FURNITURE — 17 shipped, 0 implemented

`chair_marauder_01` · `chair_metal_001` · `chair_metal_002` ·
`chair_wood_001` · `lamp_marauder_01` · `marauder_table` ·
`shelf_metal_001` · `shelf_wood_001` · `stool_marauder_01` ·
`stool_metal_001` · `stool_wood_001` · `stool_wood_002` · `stool_wood_003` ·
`table_metal_001` · `table_metal_002` · `table_wood_001` · `table_wood_002`

Backed by 16 prefabs (`ChairMetal01/02`, `ChairWood01`, `ChairMarauder01`,
five `Stool*`, five `Table*`, `ShelfMetal01`, `ShelfWood01`). **An entire
authored sub-category with a folder of its own, and not one row anywhere in
this repo.** The `schematic_icon_furnciture` category icon exists too.

**What it would take:** these are inert props on the common ship-part base,
exactly like `Barrel01` and `Cupboard` — which already work. `LoosePartCatalogue`
rows plus schematics. **No new components.** This is the largest
count-for-effort win on the list.

*Are chairs sittable?* **No — and that makes this cheaper, not more
expensive.** Searched all 443 component ids for `seat`, `sit`, `chair`,
`ride`, `mount`, `climb`, `ladder`, and the decompile's class list for the
same: **there is no seating component in the shipped game.** The only
adjacent id is 1052 `PlatformPositionOffset` (standing on a moving platform).
Retail chairs were decoration too. So B really is "17 inert props on a base
that already works".

#### C. Ship IDENTITY — figureheads, flags, plaques, masks: 19 shipped, 0 implemented

9 `FigureHead*` (Founder01, Kioki01/02, Pekoe01/02, Pioneer01,
Saborian01/02), 6 flags (`BossaFlag`, `GabenFlag`, `ImprobableFlag`,
`NailAndGearFlag`, `PirateFlag`, `FlagFounder01/02`), `PlaqueFounder01`,
`MarauderMask01/02`, plus `SailFounder01`, `HelmFounder01`,
`LanternPioneer01`, and four culture lamps (`LampKioki01`, `LampPekoe01`,
`LampSaborian01`, `LampChristmas01`), `Jack_O_Lantern01`.

**Player impact is higher than "decoration" suggests.** In a game whose
entire social surface is *seeing another crew's ship on the horizon*, the
figurehead and the flag are the only way a ship says who owns it. They are
also inert props — the same zero-component build as a barrel.

#### D. `CrowsNest` and `CrowsNest_wood` — 2 prefabs, 1 icon, 0 implementation

**Triple-confirmed and never mentioned once in this repo.** Searched
`crowsnest`, `CrowsNest`, `crow_nest`, `crow's nest`, `Crowsnest` — zero hits
across all of `WorldsAdriftRebornGameServer*` and `WorldsAdriftReborn`. There
is even a third icon, `ship_crowsnest`, in the root folder.

A crow's nest is a *climbable lookout* — the highest-visibility structural
part on this list after the deck itself.

#### E. `AllianceRadio01` — 1 prefab, 1 icon, 1 component, 0 implementation

Component **1262 `AllianceRadioState`** is unserved. This is ship-to-ship
alliance communication — and given there is no chat at all (§7.3), it is
either the most valuable missing part on the ship or moot until chat exists.

#### F. `ShipFrame` — the icon exists, the *bare* frame does not

`shipframe` is a `ship parts/` icon and `ShipFrame`/`ShipFrame01`/`ShipFrame02`
are prefabs. We reference `ShipFrame` heavily (`BuiltShipSpawner`,
`ShipPublisher`) as the built-ship root, but there is **no craftable frame
part** in `LoosePartCatalogue`. **INFERRED:** retail let you buy/craft hull
frames as parts; we generate them. Worth checking before treating it as a gap.

#### G. Ship weapons — `ModularCannon` and `ModularSwivelGun`

Both are in the 98 (they carry the ship-part stack). We implement neither.
`ModularCannon` appears once in our entire codebase — a doc comment in
`Materials/HullMaterials.cs` describing its four material slots (Casing /
Barrel / AmmoLoader / FiringMechanism). `ModularSwivelGun` appears **nowhere**.

The supporting evidence is overwhelming and entirely unexploited:

- **19 `procedual ship parts menu icons/`**: `cannon-1`…`cannon-5`,
  `engine-1`…`engine-5`, `swivel-1`…`swivel-4`, `wing-1`…`wing-5`. We use
  **two** — `engine-1` and `wing-1`. **PROVED that retail's procedural parts
  had five quality tiers each, and that we ship one.**
- `schematic_icon_cannon` and `schematic_icon_swivelgun` — two of the 20
  crafting categories.
- Items: `cannonball`, `cannonShell`, `swivelGunShell` already exist as
  `Equipment` rows in `itemData.json`. **The ammunition is implemented and
  the guns are not.**
- Prefab `CannonBall01`.
- Components 1173 `CannonHeatEfficiencyState`, 4445 `SwivelGunState`, and the
  whole shooting stack (§7.2). We *do* serve **4444 `MountedGunShotState`** —
  the shot event exists with nothing behind it.

#### H. The `Bag` and `EggHolder` storage parts

Both are in the 98; neither is implemented. `EggHolder` has its own component
(21012 `EggHolderState`, unserved) and ties into the fuel-egg chain (§6.5).

#### I. `Lock` — ship locking

In the 98. Zero references. With `LockEquip` and components 1217/1218/1220/1221.
**In a PvP game with boardable ships, "can I lock my ship" is not a
decoration question.**

---

## 3. CATEGORY: DEPLOYABLES, PROPS AND TOOLS

### 3.1 The `*Equip` family — the client's own marker for "a player deploys this"

The client ships **13** prefabs whose name ends `Equip`. That suffix is
retail's own convention for the *held/placing* form of a deployable, and it
makes this an unusually clean enumeration: a thing with an `Equip` twin is a
thing a player was meant to plant in the world.

| `*Equip` prefab | ours? |
|---|---|
| `CraftingStationEquip` | **yes** (`Deployables.cs`) |
| `ShipyardEquip` | partial — `Shipyard` is implemented, the `Equip` prefab is never named |
| `CampfireEquip` | partial — `Campfire` placed, **but as an inert prop** (1012 unserved) |
| `LifterEquip` | partial — `Lifter`/`atlasLifter` placed, 1021 unserved |
| `TerritoryControlBeaconEquip` | partial — beacon placed, 1272 `RadarState` unserved |
| **`BombEquip`** | **no** — 1014/1015/1350 all unserved |
| **`ChestEquip`** | **no** |
| **`LockEquip`** | **no** — with `Lock`; 1217/1218/1220/1221 unserved |
| **`TurretEquip`** | **no** — see §3.4 |
| **`MarauderCompassEquip`** | **no** — with `MarauderCompass`; 1265 `AtlasCompassChestState` unserved |
| **`RockSpawnerEquip`** | **no** |
| **`TreeSpawnerEquip`** | **no** |
| **`FuelEggSpawnerEquip`** | **no** — see §6.5 |

Eight of thirteen have **zero references anywhere in our source**. Searched
each on the prefab name, the base name without `Equip`, and the lower/upper
and underscore variants.

### 3.2 The deployables we do place, and what they do when placed

`Placement/Deployables.cs` registers: `AssemblyStation`, `atlasLifter`,
`Barrel01`, `Campfire`, `ContainerLarge`, `ContainerMedium`,
`CraftingStation`, `Cupboard`, `KiokiRevivalChamberA`, `Lamp01`, `Lifter`,
`Loom01`, `MakeshiftStorage`, `MountedBox`, `personalReviver`,
`PowerGenerator01`, `Shipyard`, `Stove01`, `TerritoryControlBeacon`, `Trunk`.

**But `Deployables.cs:217-259` registers campfire, stove, loom, lifter, lamp,
power generator, personal reviver, territory beacon and every storage
container as `TransformOnly` / `hasBackedState: false`. They place as inert
props.** That is the honest reading of this category: the placement system is
real, the placed objects mostly are not yet functional.

And one of the stated reasons is out of date — see §7.3 on 1081.

### 3.3 Tools

| prefab | status |
|---|---|
| `placementScannerTool` | **zero references.** Searched `placementScannerTool`, `PlacementScanner`, `scanner tool`, `scannertool`. (2108 `ScannerToolState` also unserved; note we *do* serve 2107 `ScannerToolPlayerState`, so this is a partial.) |
| `salvageRepairTool` | **zero references** as a prefab — but salvage/repair *works* under the multitool id family (1099/2106/1231). A rename, not a gap. See §7.4. |
| `Lock` / `LockEquip` | **zero references.** Searched `Lock`, `LockEquip`, `Lockable`, `Unlocking`. Ship locking is entirely absent. |
| `TeleportHelper` | **zero references** (the `TeleportHelperState`, 1046, is also unserved) |
| `WiringKit` | **zero references.** Searched `wiringkit`, `WiringKit`, `wiring`, `wire`. 1213–1216 all unserved. **The whole ship-wiring system.** |
| `ControlButton`, `ControlLever` | **zero references** each. Searched both spellings plus `control button`, `lever`. These are the *inputs* a wiring kit wires up. |

`WiringKit` + `ControlButton` + `ControlLever` + 1213 `WiringKitState` +
1214/1215/1216 `Wireable`/`ShipWires`/`WireTrigger` is a **complete, coherent
subsystem that nothing in this repo has ever mentioned.** It is the clearest
example in the document of the failure mode this exercise exists to catch:
nobody searched for it because nobody knew the word.

### 3.4 Turrets — and a correction that changes the work

Seven turret prefabs (`TurretLight`, `TurretHeavy`, `TurretHeavyKioki`,
`TurretExplosive`, `TurretExplosiveKioki`, `TurretElectric`, `TurretSnare`),
three projectile prefabs, `TurretEquip`, and **seven authored `turrets/`
icons**. **Zero server implementation** — we filter 1112 `TurretControlInput`
out of the mirror and read a `turrets` boolean in `ReleaseWorldCatalog.cs`,
and that is the entire extent of it.

**The correction:** a keyword sweep would file these as ship weapons. The
component classifier says otherwise — they carry
`IslandTurretProjectileShootingVisualizer`, and their components are 1371–1374
`IslandTurret*`. **They are island defences, not ship guns.** That materially
changes the work: turrets are a *world/PvE* feature attached to islands, and
the ship-mounted weapons are the separate `ModularCannon` / `ModularSwivelGun`
pair (§2.3).

### 3.5 Loot props — 44 shipped, 2 used

`LootRuinPile1`–`24`, `LootRuinPileKioki01`–`12`, `LootChest_001`,
`LootChest_Kioki`, `LootContainer_001/002/003`,
`LootContainer_Kioki_01/02/03`, `DataBank_001/002/003`, `ScannableRuin`.

Referenced: `lootruinpile1`, `lootchest_001`, `lootcontainer_001`,
`databank_001`, `scannableruin`. **The other 39 are named nowhere.** Every
one is client-resolvable today. This is already recorded in
`feature-roadmap.md` §8 as pending work on `feat/loot-containers`; it is
repeated here because a *contents* enumeration reaches the same place a
*claims* audit did, which is a useful check that both are working.

### 3.6 Props and expression — an entire category with no representation

| prefab | evidence | ours |
|---|---|---|
| `PhotoCamera` | prefab + `item_camera` icon + **1024–1028** (`PhotoBook`, `PhotoCamera`, `PictureFrame` components) | **nothing** |
| `PictureFrame01/02/03` | prefabs, ship-part class | **nothing** |
| `Ocarina`, `marauder_drums`, `tom`, `MarauderGuitar` | prefabs + **1023 `MusicalInstrumentState`** | **nothing** (`Guitar` and `Horn` exist as inert item rows; 1023 is not served — the `1023` hits in `ComponentsSerializer.cs` are `Quaternion32` packing constants, not this component) |
| `MarauderMask01/02`, `Jack_O_Lantern01`, `LampChristmas01` | prefabs + `ship parts/` icons | **nothing** — seasonal/event content |
| 9 `FigureHead*`, 6 flags, `PlaqueFounder01` | prefabs + `ship parts/` icons | **nothing** — see §2.3 |

**Photography is a whole shipped feature (five component ids and a prefab)
that appears nowhere in this repo, in any document, under any name.** It was
found by reading the prefab list, not by looking for it.

---

## 4. CATEGORY: ITEMS, MATERIALS, FOOD

### 4.1 Cooking — the largest single content gap in the client

| | count |
|---|---:|
| `foods/` icons shipped | **68** |
| `foods/` icons referenced by us | **1** |
| `Food` rows in `itemData.json` | **4** (Manta Steak, Moonshine, Thuntomite Steak, Thuntomite Stew) |
| `Cooking` schematics | **4** |
| `schematic_icon_cooking` | shipped |
| knowledge-tree cooking branch | **9 nodes** (already recorded in `feature-roadmap.md` §8) |

**PROVED (icons).** 68 authored, named, sized food sprites. They are not
placeholder art — they describe a real, deep cooking system with
ingredients, processing steps and cuisine styles:

- **Raw meat, per creature per biome:** `2x2_beetle_biome2/3/4_meatraw`,
  `2x2_mantaray_biome2/3/4_meatraw`, `2x2_beetle_steak_raw`,
  `2x2_manta_steak_raw`. **The biome suffix is the tell — meat quality varied
  by where you killed the animal.**
- **Staples and processing:** `1x2_flour`, `2x1_yeast`, `2x2_bread`,
  `2x2_plainrice`, `1x1_vinegar`, `1x1_selenesugar`, `1x1_conossalt`,
  `1x2_birikoispices`, `2x3_meatpaste`.
- **Drinks:** `1x2_grog`, `1x2_moonshine`, `2x3_rum`, `1x2_witchsbrew`,
  `1x1_greyshellbrew`, `1x1_cloudwormcordial`, `1x1_hardy_draught`,
  `1x1_climberselixir` — the last two read as *buff consumables*, not food.
- **Foraged ingredients:** `1x1_craggy cap mushroom` *(with a space, shipped)*,
  `1x1_killa_mushrooms`, `1x1_tasty_mushrooms`, `1x1_cloudworm`,
  `1x1_greyshell`, `1x1_slug`, `1x1_worm`, `2x2_pumpkin`.
- **Finished dishes — 30 of them**, including
  `2x2_verdubanstylebatteredthuntomite`,
  `2x2_exquisitethuntomitestuffedshell`, `2x2_gourmetmantacurry`,
  `2x2_rum marinated manta` *(spaces, shipped)*. The adjective ladder
  (`nice_` → `gourmet` → `exquisite`) is a **quality tier system**.

**What it would take:** `itemData.json` rows + `Cooking` schematics + a
`Stove01`/`Campfire` that actually cooks — which needs **1264
`InventoryItemCraftingStationState`** and **1012 `CampfireState`**, both
unserved (§7.2). The 9-node cooking knowledge branch is already loaded and
spendable. **This is the biggest "content is already on disk" item in the
whole client, and it is bigger than the roadmap's §8 estimate because that
section counted the knowledge nodes and not the 68 icons.**

Two prerequisites are worth stating plainly: cooking needs food *sources*,
and food sources are **creatures you can kill** (§5.3) — which needs the
creature health/damage stack. Cooking is not independent of combat.

### 4.2 Materials — 30 shipped, 1 referenced

`2x2_cloth`, `2x2_leather`, `2x2_leathersalvage`, `2x3_chitin`,
`3x1_clothdyed`, `3x1_clothmakeshift`, `3x1_clothmakeshift_brown`,
`3x1_clothsalvage`, `1x1_pigment`, `2x1_ancientglass`, `2x1_glassshards`,
plus the **biome-suffixed creature harvest set**:

- `1x1_biome1..4_neuralcluster` (4)
- `2x1_biome1..4_conductivevessels` (4)
- `2x3_beetle_biome1..4_resource1` (4)
- `3x2_mantaray_biome1..4_resource1..4` (4)

**PROVED (icons).** Sixteen of the thirty are *creature drops keyed by
biome*. Together with §4.1's biome-keyed raw meat, this establishes something
the repo does not currently model: **killing an animal on a tier-3 island
gave materially different loot from killing the same animal on Haven.** That
is a progression axis, not flavour.

`neuralcluster` and `conductivevessels` read as high-tier crafting inputs —
plausibly the ship-core and instrument tier. **Unconfirmed.**

### 4.3 Crafted items — 27 shipped, 14 referenced

The 13 unreferenced ones are unusually load-bearing for a "crafted items"
folder:

| icon | what it evidently is |
|---|---|
| `2x1_makeshiftbandages`, `2x1_nervurebandages` | **healing.** `schematic_icon_firstaid` is one of the 20 categories |
| `2x2_timed_explosive` | with prefabs `Bomb`/`BombEquip` and components 1014/1015/1350 |
| `3x2_flaregun`, `1x1_flaregun_stubby_cartridges` | signalling — plus root icons `item_flare`, `item_flare_gun` |
| `3x2_pioneer_pistol` | with prefab `PistolPioneer01`; a `pistol` item row exists, 1096/1249 unserved |
| `3x4_inertia_pack`, `3x4_stasis_pack` | **movement utilities with real decompiled classes** — see §4.6 |
| `2x2_bioelectrical_generator2` | a second generator type; icon-only |
| `3x3_rhegus_greaves` | **the Atlas Boots** — see §4.6 |
| `1x2_dye`, `2x2_paintcan`, `3x4_paintdrum` | **ship painting.** Three icons. No component found |

**First aid is a shipped crafting category we have nothing for.** In a game
about falling off islands, that is a real omission.

### 4.4 Tools and equipment — the root icon folder

163 of the 187 root icons are unreferenced, but most are UI chrome
(`hotbar_*`, `scanner_*`, `slot_*_ciphers`, `loading_logo_noglow`). After
subtracting those, the substantive ones:

**Implemented despite the icon being unreferenced (checked, not assumed):**

- **Grappling** — `item_grapple`, `item_hook`. **Works.** 1098
  `RopeControlPoints` is served and the client mod draws `RemoteGrappleLine`.
  An icon-only diff would have called this missing.
- **Glider** — `item_glider`. **Partially works.** 1151/1152 `GliderState` are
  unserved, but `UtilitySlotActivatedState_Handler.cs:12` relays the deploy so
  *"they can see each other's glider deploy"*. Whether it produces flight is a
  live-client question (§10), **not** the flat "cannot fly" a component-only
  reading gives.
- **Multitool** — five colour variants unreferenced, but the multitool works
  (2002/2105/2106 served); the colours are its **modes**.
- **Torch, axe, telescope, harpoon, crossbow** — all present as `itemData.json`
  rows. Inert, but present.

**Genuinely absent, evidence class in brackets:**

| thing | evidence | note |
|---|---|---|
| `item_bomb` + `Bomb`/`BombEquip` + 1014/1015/1350 | prefab + component + icon | the strongest-evidenced missing item in the game |
| `item_bow` | **icon only** — no prefab, no component, no item row. Searched all three | possibly cut pre-launch |
| `item_gasmask` | **icon only**. Searched prefabs, decompile, item rows | ties to the Blight? unconfirmed |
| `item_camera` + `PhotoCamera` + 1024–1028 | prefab + 5 components + icon | §3.6 |
| `item_tct` | **icon only** — but `TerritoryControlBeacon` exists. "TCT" = territory control tool | the beacon's placing tool |
| `atlas_compressor`, `atlas_injector`, `beltseparator` | **icon only** for the first two; `beltseparator` appears in `acs/ScannableData.cs` | atlas-processing devices; the strongest lead in this row |
| `item_fuel_extractor`, `item_fuel_crystal`, `item_fuel_tank` | icons + prefabs | §6.5 |

### 4.5 Ciphers — a whole progression system, stubbed

Eleven root icons: `blue/green/orange/purple/red/yellow_ciphers`, their seven
`slot_*` twins, and four **part-typed** ones —
`cannon_ciphers`, `engine_ciphers`, `swivelgun_ciphers`, `wing_ciphers`.

`schematicData.json` carries `cipherSlots` and `cipherSlotParsed` on every
row — **empty on all 62**. `InventoryModificationState_Handler.cs:443-444`
counts `installCipher` and `destroyCipher` and logs *"no cipher model"*.
`ComponentsSerializer.cs:1409` says *"cipherSlotCounts stays empty — cipher
purchases are a later track"*.

**RECOVERED:** ciphers were retail's per-part upgrade/tuning system, colour-
tiered, slotted into procedural cannons, engines, swivel guns and wings.
The wire messages arrive today and are counted and dropped. This is the
mechanical partner to the five procedural quality tiers in §2.3.G — the two
were almost certainly one feature.

### 4.6 The utility-slot family — six classes, and we implement two

**This is the best single demonstration in the document of why "one failed
search" is not evidence.** My first pass filed `3x4_inertia_pack`,
`3x4_stasis_pack` and `3x3_rhegus_greaves` as *icon-only, probably cut*,
because none of the three names appears in the prefab table, the component
map, or `itemData.json`. Searching the decompile on the *concept* instead of
the *icon name* immediately found this:

```
acs/Assets.Scripts.Player.Utilities/
    AtlasBoots.cs   Glider.cs   InertiaPack.cs
    LightSource.cs  StasisPack.cs   Weapon.cs
```

**PROVED.** Six concrete `UtilityItem` subclasses, all
`[WorkerType(WorkerPlatform.UnityClient)]`. This is retail's utility-slot
roster — the thing 6910 `UtilitySlotActivatedState` (which we **do** serve and
relay) activates.

| utility | ours? | evidence |
|---|---|---|
| `Glider` | **partial** | `glider` item row + deploy relay; 1151/1152 unserved |
| `LightSource` | **partial** | `hipLamp` "Hip Lamp" and `headTorch` "Head Torch" item rows exist |
| `Weapon` | **no** | the whole combat pillar, §7.2 |
| **`AtlasBoots`** | **no** | class has a `greavesRenderer` — and the unreferenced icon is `crafted items/3x3_rhegus_greaves`. **INFERRED, strongly: the Rhegus Greaves *are* the Atlas Boots.** Another item-name / class-name collision, exactly like the generator |
| **`InertiaPack`** | **no** | class fields: `trails`, `energyMeter`, `minHeightFromGroundToActivate`, `energyLossPerSecond`/`energyGainPerSecond`. **A fall/momentum utility with a rechargeable energy budget** |
| **`StasisPack`** | **no** | same energy model plus a `ParticleSystem vfx` and `minHeightFromGroundToActivate` |

Both packs gate on `minHeightFromGroundToActivate` and drain an energy bar.
**INFERRED:** these are the air-mobility items — what you use after you step
off an island and before you hit the clouds. In a game whose defining verb is
*falling*, a fall-arrest utility is not a side item.

None of `AtlasBoots`, `InertiaPack`, `StasisPack` or `LightSource` appears
anywhere in this repo. Searched each on the class name, the lowercase and
camelCase item-id forms, the icon name, and the plain-English name.

---

## 5. CATEGORY: CREATURES

### 5.1 What the client ships — 12 fauna prefabs

`Beetle` · `BeetleEgg` · `MantaRay` · `MantaRayEgg` · `MantaRay_Egg` ·
`JellyFish` · `DiscoWhale` · `Flock` · `Egg` · `Patrol` ·
`BasicCreatureSpawner` · `BigCall`

Plus five jelly *flora* pods that the fauna system owns: `DesertPod`,
`DesertPodB`, `FlowerPodJelly`, `FlowerPodFireJelly`, `SeedPodJelly`.

### 5.2 What we implement

**Nearly all of them, as bodies.** `IslandFaunaPolicy.cs` names each retail
species against its prefab; `SkyWhalePolicy.cs` handles `DiscoWhale`;
`IslandFaunaSchool.cs` reimplements `BasicCreatureSpawnerState` server-side.
`FlowerPodFireJelly` is explicitly noted as *"a fifth prefab [that] survives
with NO enum member"* — an honest, already-recorded gap.

Prefab coverage here is **the best of any category in this document**. It is
also the most misleading.

### 5.3 THE DIFF — creatures are furniture

The bodies exist. **61 of the 443 component ids — the single largest gap in
the whole component map — are the creature behaviour stack, and none of them
is served.** We serve the six that make a creature *render* (4322
`BasicCreatureState`, 1166 `AgeState`, 1177 `GenderState`, 1182
`SpeciesState`, 1183 `ReconsumablesState`, 4326 `MantaRayVariantState`) and
none of the ones that make it *behave*.

| missing subsystem | ids | consequence |
|---|---:|---|
| conducts / behaviour tree | 1153–1157, 1162, 1178/1179, 1190–1193, 4343 | ★ nothing hunts, feeds, flees, mates or patrols |
| senses / emotion | 1158/1159, 1188/1189, 1196, 1296–1299, 1302 | ★ **nothing reacts to the player** |
| damage / health / mortality | 1160/1161, 1171, 1194/1195, 4324, 4346, 4348 | ★ **creatures cannot be killed and cannot kill** |
| life cycle | 1167–1170, 1180/1181, 1301, 4325, 5000, 21012 | no breeding, no eggs hatching |
| needs | 1172, 1176, 1184–1186, 4335–4337 | no hunger, no rest |
| population / flocking | 1187, 1197–1200, 1245, 1300, 4321, 4332, 4341, 4344 | *renamed* — done server-side, see §7.4 |

This is the clearest case in the document of a category that **looks** near-
complete on a prefab count and is near-empty on a gameplay count. Any future
inventory should count both.

`pets/3x3_basher` — a single icon in its own `pets/` folder, and the only
member. **PROVED (icon only).** No `Basher` prefab, no `Basher` string
anywhere in the decompile. Searched: prefab table, decompile tree, icon
atlas, item data. This is the strongest evidence in the document for a
**pet system** that was authored and either cut or never shipped past the
icon.

---

## 6. CATEGORY: WORLD RESOURCES

### 6.1 Trees and flora — the best-covered category

72 flora prefabs shipped; **65 referenced**. The seven not referenced:

`treespawnerequip` (a deployable, §3) and the six `woodlandtree*` —
`woodlandtreebarelarge` · `woodlandtreebaresmall` · `woodlandtreebareverylarge` ·
`woodlandtreefoliagelarge` · `woodlandtreefoliagesmall` ·
`woodlandtreefoliageverylarge`.

**INFERRED:** these are one temperate/woodland biome's tree set. We populate
the palm, wonky, desert and straight families and not this one. Low player
impact today because no live island uses that biome; it becomes a visible hole
the moment one does.

### 6.2 Metals — a roster mismatch in BOTH directions

This is a §9-class finding and it was found by enumerating the atlas, not by
auditing our table.

| | count | list |
|---|---:|---|
| **client `metals/` icons** | **18** | aluminium, bronze, copper, epilar, eternium, gold, iron, lead, **magnesium**, nickel, orthite, **palladium**, **platinum**, silver, steel, tin, titanium, tungsten |
| **our `MaterialCatalog`** | **17** | aluminium, **aurium**, bronze, **cobalt**, copper, epilar, eternium, gold, iron, lead, nickel, orthite, silver, steel, tin, titanium, tungsten |

- **Shipped and missing from us (3):** `magnesium`, `palladium`, `platinum`.
  Each has an authored, named, shipped icon. **PROVED (icon).**
- **In our table with no client icon (2):** `aurium`, `cobalt`. Either they
  are retail metals whose icons live elsewhere, or they are **WAREBORN
  TUNING** that has quietly entered a table read as recovered. Worth ten
  minutes to settle — a metal with no icon is a metal the client cannot draw.

### 6.3 Woods

| | count | list |
|---|---:|---|
| **client `woods/` icons** | **13** | ash, birch, cedar, chestnut, **ebony**, elm, hemlock, **ironwood**, **mahogany**, **maple**, oak, palm, **palm2** |
| **our `TreeSpecies` / `MaterialCatalog`** | **8** | ash, birch, cedar, chestnut, elm, hemlock, oak, palm |

**Five wood types shipped and unimplemented:** `ebony`, `ironwood`,
`mahogany`, `maple`, plus the `palm2` variant. **PROVED (icon).** Given
`MaterialCatalog` already carries per-material quality/mass properties, these
are table rows plus a tree-species→wood mapping, not a system.

### 6.4 Metal deposits and harvestables

The client ships a 42-prefab harvestable family: `MetalDeposit{Atlas,Boulder,
Core,Crust,Scrap}`, `metal_deposit_entity`, `metal_harvest_rock_piece1-20`,
`MetalNugget`, 11 `MetalScrap_*`, `metal_scrap_3/4`, `HarvestableRock`.

We implement the anchored deposit chain (crust → health → core) on the
`metal_deposit_entity` family. **Not referenced anywhere:** all 20
`metal_harvest_rock_piece*`, all 11 `MetalScrap_*` + `metal_scrap_3/4`,
`metaldepositboulder`, and `HarvestableRock`.

**INFERRED:** the `metal_harvest_rock_piece*` set is the *debris* a deposit
throws when struck, and the `MetalScrap_*` set is the loose world scrap the
`scrap items/` icon family feeds off. Both are cosmetic-to-mechanical polish
on a loop that already works, not missing systems. `metaldepositboulder` and
`HarvestableRock` are two deposit *shapes* we never spawn — a visual variety
gap, cheap to close.

### 6.5 Fuel — the source chain, not the tank

The generator miss is fixed. **The rest of retail's fuel chain is not.**

| prefab | status | evidence |
|---|---|---|
| `PowerGenerator01` | **implemented** (was the famous miss) | in `Deployables.cs` and `LoosePartCatalogue.cs` |
| `FuelDeposit` | **zero references anywhere** | searched `fueldeposit`, `FuelDeposit`, `fuel_deposit`, `fuel deposit` |
| `FuelExtractor` | **zero references anywhere** | searched `fuelextractor`, `FuelExtractor`, `fuel_extractor`, `extractor` |
| `FuelEggSpawnerEquip` | **zero references anywhere** | searched `FuelEggSpawner`, `fuelegg`, `egg spawner` |
| `item_fuel_crystal`, `item_fuel_extractor`, `item_fuel_tank`, `item_fuel` | icons shipped, none referenced | icon atlas root |

**This is the same shape of miss as the generator, one link further up the
chain.** We implement fuel *consumption* (`ShipFuelLedger`, `FuelCanister`,
`FuelPods`) and the tank. We do not implement where fuel *comes from*.

And unlike most entries in this document, the decompile spells the whole thing
out once you know to look for `FuelDeposit` — which is exactly the point of
enumerating rather than hypothesising. **PROVED, all four files read:**

- `acs/FuelDepositLocation.cs` — a `MonoBehaviour` with `radius`, **`eggRadius`**,
  **`eggCount`** and a `layerMask`.
- `acs/FuelDepositVisualizer.cs` — renders `numberOfCrystals` instances of
  `fuelCrystalVisual` in a seeded scatter within `radius`, between `minScale`
  and `maxScale`. **A cluster of fuel crystals growing out of the ground.**
  This is what `item_fuel_crystal` is a picture of.
- `acs/IslandSurfaceData.cs:163-177` — `GenerateFuelDepositSpawnRequest()` /
  `FindPlaceForFuelSpawn()`: the deposit is placed by **surface-sampling the
  island mesh**, returning a `SurfaceInformationPoint` with a world point and
  a surface normal.
- `acs/IslandProxyVisualizer.cs:160-175` — the caller. On failure it falls back
  to `IslandGenSpawnLocations.GenerateFuelDepositSpawnRequest(_eggSpawnSpec)`,
  then spawns a `FabricTransform` oriented to the surface normal with the
  literal asset name **`"Egg"`**.

So the retail chain was: **island surface-sample → `FuelDeposit` node → a
scatter of fuel crystals + `Egg` entities → `FuelExtractor` (deployable) works
the node → fuel**. That ties together five prefabs we never implemented
(`FuelDeposit`, `FuelExtractor`, `FuelEggSpawnerEquip`, `Egg`, `EggHolder`),
one unserved component (**1022 `ResourceGenerationState`** — the regeneration
timer), and four unreferenced icons.

Today fuel arrives from loot and crafting instead. **That is WAREBORN TUNING
standing in for a recoverable retail system, and it should be labelled as such
wherever it is documented.**

---

## 7. CATEGORY: COMPONENT IDS WITH NO SERVER IMPLEMENTATION

### 7.1 The numbers

`component-map.tsv` is 444 lines: 1 header + **443 unique component ids**.
(One name collides: `HealthState` is both **1077**, the player's, and **1160**,
the creature's.)

| | count | share |
|---|---:|---:|
| component ids defined by the shipped client | **443** | 100% |
| implemented — served, handled or mirrored | **127** | 28.7% |
| **deliberately absent** — decided, documented, tested | **8** | 1.8% |
| **unimplemented** | **308** | 69.5% |
| ids we reference that are NOT in the client's map | **0** | — |

**PROVED.** Method: four dispatch surfaces enumerated separately —
126 literal `componentId == N` branches in `ComponentsSerializer.cs`, 21
`Update/Handlers/*.cs` (registration is by *type hash*, not id, so the id was
resolved through the generic argument), 44 ids in `MirrorSendPolicy.cs`, and
the 8 live entries in `ComponentAbsencePolicy.KnownAbsentComponentIds` — then
a full-tree scan of 724 `.cs` files for every integer literal matching a map
id **and** every component class name as a word token, with all 86 borderline
hits hand-reviewed. Test fixtures, item ids, material Q-values and line
numbers were rejected, not counted.

**We have invented nothing.** Three independent checks (all 124 literal serve
branches, all 56 `*ComponentId = N` constants, and a scan for any id-shaped
literal on a line mentioning "component id") found the server's vocabulary to
be a **strict subset** of the client's. The single non-map hit was `157` — a
source line number in a comment.

### 7.2 The unimplemented 308, by feature area

Sorted by size. ★ marks *player-visible*.

| area | ids | what a player loses |
|---|---:|---|
| **Creature / AI** | **61** | ★ creatures are furniture — see §5 |
| **Items / deployables** | 30 | ★ campfire, bomb, lifter, multitool, photography, wiring kit, scanner tool, pistol |
| **Player** | 26 | ★ death/respawn, clean logout, appearance sender, quests |
| **Ship** | 25 | ★ wiring, locks, engine/wing state, overheating, alliance radio |
| **World / resources / loot** | 20 | mostly renamed — see §7.4 |
| **AI-crew / NPC scripting** | 18 | *nothing* — Bossa's internal test harness |
| **Social / alliance / crew / chat** | 15 | ★ **no in-game chat at all** |
| **Combat / shooting / turrets** | 12 | ★ an entire pillar |
| **Weather / storms / lightning** | 11 | ★ no wind walls, no storms, no lightning |
| **Haven / teleport / respawn infra** | 6 | ★ haven is a permanent prison |
| **Crafting / knowledge** | 5 | mostly renamed — see §7.4 |
| **Spectator** | 5 | dev tooling |
| **Glider / movement** | 4 | ★ the glider item exists and cannot fly |
| **Salvage / repair** | 3 | renamed — see §7.4 |
| **Infra / no-op / dev / deprecated** | ~45 | nothing |

### 7.3 The load-bearing individual gaps

Named, because these are the ones with a documented blocker already sitting in
our own source.

- **1094 `RespawnPointState`** — `Ship/PartInteractionPolicy.cs:61` says the
  ship reviver is *"BLOCKED on serving 1094"*. With 1092/1093 this is the
  whole death→respawn loop. **A genuine gap, not a rename.**
- **9002 `NewChatListener` / 1001 `ChatListener`** — `FallRescueService.cs:120-131`
  records the exact blocker: the client's `ChatVisualizer` requires a
  `NewChatListener.Writer` and neither id is seeded. **There is no in-game
  chat.**
- **8056 `LeaveHavenRequest`** — `SpawnPolicy.cs:144` notes it *"has ZERO
  references in the client"*. Haven `true` is therefore a permanent state with
  no scripted exit.
- **190606 `TeleportAckState`** — we serve **190607 `TeleportRequestState`**
  and not its ack. Nothing in the tree documents skipping it. **A request
  component with no ack half is the shape of a bug, not a decision.**
- **1072 `CharacterControlsData`** — granted authority in
  `MirrorSendPolicy.cs:90`, with *no serializer branch*. Granted-but-never-
  seeded is precisely the `NoSeedForEntity` case `ComponentAbsencePolicy` was
  written to make loud.
- **1081 `InventoryState`** — `Placement/Deployables.cs:217-223` says it *"has
  no ComponentsSerializer branch yet"* and therefore keeps all eight storage
  containers `TransformOnly`. **That comment is stale — 1081 is served at
  `ComponentsSerializer.cs:633`.** Placed chests, barrels and cupboards may be
  one flag flip from being openable.

### 7.4 Implemented under a different id family — do NOT read these as gaps

This is the §9 error class showing up in component space. Each was checked on
the numeric id, the class name and 2+ synonyms before being cleared.

| looks missing | actually implemented as | our own source says |
|---|---|---|
| 1032/1034/12280/12289/2101/2104/1031 — the **metal-rock** family | the **anchored metal-deposit** family: 1255, 2103, 12283, 1305 + `MetalDeposits.cs`, `MetalHarvest.cs`, `MetalNode(s).cs` | `MetalNode.cs:72` |
| 1237/1238/1263/1244 — island loot | server-side ledgers: `Loot/LootTable.cs`, `LootBudget.cs`, `LootScrapTable.cs` | `LootBudget.cs:38` — the 1244 global-data entity *"did not ship"*, so it **cannot** be implemented |
| 1307 `GlobalKnowledgeGraphDataState` | `knowledge-tree.json` + 1332/1334/1079/1080/1260 | `KnowledgeTree.cs:18` — *"We deliberately do NOT serve 1307"* |
| 1199/1200/1245/4321/4332 — flocking & population | `Islands/IslandFauna{Capacity,Rhythm,School,Ecology,Movement}.cs`, simulated server-side | each file names the retail component it replaces |
| 1100/1101/1174 — salvage | 1099 + 2106 + 1231 + `ShipSalvageService.cs`, `ScrapSalvageService.cs` | salvage works end to end |
| 6920–6923 / 1273 — alliance | a **REST alliance service** (`alliance/`) surfaced via 6924/6925 | alliances are real; the client alliance components are not the transport |
| 1104 `FuelConsumerState` / 1106 `FuelTankState` | **1105 `FuelGaugeState`** + `Ship/Fuel/ShipFuelLedger.cs` | `ShipFuelLedger.cs:47` — *"Retail put 1106 FuelTankState on real tank ENTITIES"*; we bind fuel to the **gauge** instead |
| 1330 `ScannableState` | 8073 + 1331 + 2107 + `Databanks.cs` | databank scanning works |
| 1116 `ShipEngineState` | `Ship/Flight/ShipForceModel.cs` reconstructs thrust server-side | `ShipForceModel.cs:150` |
| 6905 `AncientRespawnerState` / 8052 `HavenTeleporterState` | `Wilderness/WildernessShrine.cs` | `WildernessShrine.cs:68,320`; the test at `WildernessShrineTests.cs:30` deliberately **fails if 6905 is added** |

One housekeeping recommendation, not an implementation: **1092/1093/1072 are
decisions** (`TeleportPolicy.cs:105-107` documents keeping `RespawnVisualizer`
disabled deliberately) but they are not in `ComponentAbsencePolicy`. Promoting
them would stop them reading as gaps to the next reader.

---

## 8. THE RANKED DIFF — TOP 20 BY PLAYER IMPACT

Ranked by *what a player notices*, not by effort and not by asset count. The
ranking rule: **a missing mounting bracket that unblocks five instruments
beats a decorative crate.**

Effort is a first guess only. "None" in the *new components* column means the
thing renders on machinery we already have.

| # | missing thing | evidence | new components | first guess at the work |
|---:|---|---|---|---|
| 1 | **In-game chat** | 1001/1002/9002/9003/9004 + `FallRescueService.cs:120-131` names the exact blocker | 9002 minimum | seed `NewChatListener`/`Speaker`, route text server-side. **The most-noticed absence in any multiplayer game** |
| 2 | **Death and respawn** | 1092/1093/1094; `PartInteractionPolicy.cs:61` — the ship reviver is *"BLOCKED on serving 1094"* | 1092/1093/1094 | serve 1094 first; it unblocks `Respawner01`, `PersonalReviver` and the Kioki chambers, all of which we already place |
| 3 | **Creatures that can be hurt or that hurt you** | 1160/1161 `HealthState`, 1194/1195 `DamageDealer`, 1171 `MortalityState` | ~6 | without it there is **no meat, so no cooking, so no food economy**. Gates #4 |
| 4 | **Cooking** | **68 food icons**, 4 item rows, 9 knowledge nodes, `schematic_icon_cooking` | 1264, 1012 | item rows + schematics + a `Stove01`/`Campfire` that cooks. Largest content-on-disk item in the client |
| 5 | **Ship weapons — cannon and swivel gun** | `ModularCannon`/`ModularSwivelGun` in the 98, 9 procedural tier icons, 2 category icons, **ammo item rows already exist**, 1173/4445 | ~8 (shooting stack) | large, but the ammunition, the materials model and 4444 `MountedGunShotState` are already in place |
| 6 | **Instrument mounting — is the bar pipe the shelf?** | BarPipe is now in `LoosePartCatalogue`, but `LoosePartCatalogue.cs:353` admits *"their exact retail server-refdata strings are unavailable"* and mounts all five instruments on `ShipDeck` | none | **verification, not construction.** The single cheapest high-value item here: read `PlacementRules` off the instrument prefabs and off `BarPipe`, and stop guessing |
| 7 | **Ship-part furniture — 17 parts, 0 rows** | 17 icons + 16 prefabs + `schematic_icon_furnciture` | **none** | catalogue rows + schematics. Inert props on a base that already works. **Best count-for-effort on the list** |
| 8 | **Ship locking** | `Lock`/`LockEquip` + 1217/1218/1220/1221 | 4 | in a PvP game with boardable ships this is a safety feature, not a convenience |
| 9 | **Fuel's source chain** | `FuelDeposit` + `FuelExtractor` + `FuelEggSpawnerEquip` + 1022, and the client's own placement algorithm in `IslandSurfaceData.cs` (§6.5) | 1022 | today fuel is loot. **This is the generator miss one link upstream** |
| 10 | **Storage containers that open** | `Deployables.cs:217-223` keeps 8 containers `TransformOnly` because 1081 *"has no branch yet"* — **and 1081 is served** | **none** | possibly a flag flip. Verify, then flip |
| 11 | **Deployables that do anything** | campfire/stove/loom/lifter/lamp/generator/beacon all placed as inert props | 1012, 1021, 1022, 1264, 1272 | five ids, five features, all already placeable |
| 12 | **The crow's nest** | 2 prefabs, 2 icons, **zero mentions in this repo** | none | inert structural part. High visibility per unit of work |
| 13 | **Ship wiring** | `WiringKit` + `ControlButton` + `ControlLever` + 1213–1216 — **a complete subsystem never once named here** | 4 | genuinely new. Ranked here because it is *invisible until you know it existed*, which is this document's whole point |
| 14 | **Ship identity — figureheads, flags, plaques** | 19 prefabs + 19 icons | none | inert props. In a game about seeing other crews' ships, this is how a ship says who owns it |
| 15 | **Wood and metal variants of every ship part** | 23 metal/wood icon pairs; `HullMaterials` already picks a dominant wood and metal | none | an icon-selection rule. Closes ~20 icon-gap rows for one function |
| 16 | **Island turrets** | 7 turret prefabs + 3 projectiles + 7 icons + 1371–1374. **Island defences, not ship guns** | ~5 | the PvE threat layer. Depends on the shooting stack (#5) |
| 17 | **First aid** | `2x1_makeshiftbandages`, `2x1_nervurebandages`, `schematic_icon_firstaid` | 4337 `HealthRegenerationState` | in a game about falling, healing is core, not comfort |
| 18 | **Storm walls and lightning** | 1204, 1222–1229, 1202/1203, 5129 + 44 typed weather-wall segments already extracted | ~8 | the world has no soft boundary. Also what bar pipes were *for* — *"attract lightning in a Stormwall"* |
| 19 | **The utility packs — Inertia and Stasis** | `acs/…/InertiaPack.cs`, `StasisPack.cs` + 2 icons; energy-budget fall utilities | unknown | see §4.6. Ranked below the rest only because their component wiring is unestablished |
| 20 | **Bombs** | `Bomb`/`BombEquip` + 1014/1015/1350 + `item_bomb` + `2x2_timed_explosive` | 3 | the most thoroughly evidenced single missing *item* in the client |

**Just outside:** ciphers and the 5 procedural quality tiers (§4.5, one
feature, gated on #5); the 39 unused loot props (already on
`feat/loot-containers`); photography (5 components, pure expression);
musical instruments (1023); the 5 missing wood and 3 missing metal types
(§6.2–6.3); `AllianceRadio01` (moot until #1); the pet (`pets/3x3_basher`,
one icon and nothing else).

---

## 9. THINGS WE IMPLEMENTED UNDER A NAME THAT IS NOT RETAIL'S

*This is the generator class of error. Each of these is a place where a future
search for retail's word will come back empty and a future agent will conclude
the thing does not exist.*

### 9.1 Retail's ITEM names are not retail's PREFAB names

This is the single biggest structural reason the generator was missed, and it
is not our fault — it is how Bossa shipped the data. **PROVED** from
`itemData.json` icon paths cross-referenced against the prefab table:

| retail item id | retail item name | icon it uses | prefab it is |
|---|---|---|---|
| `powerGenerator` | **Power Generator** | `ship parts/generatormetal` | `PowerGenerator01` |
| `trunk` | Trunk | `ship parts/containermediumwood` | `ContainerMedium` |
| `mountedBox` | Mounted Box | `ship parts/containermountmetal` | `ContainerMount` |
| `storageContainer` | Storage Container | `ship parts/containersmallmetal` | `ContainerSmall` |
| `shippingContainer` | Shipping Container | `ship parts/containerlargemetal` | `ContainerLarge` |
| `assemblyStation` | Assembly Station | `crafted items/4x4_crafting_station` | `CraftingStation` |
| `barrel` | Barrel | `ship parts/barrelwood` | `Barrel01` |

**Three naming systems for one object** — item id, icon name, prefab name —
and none of them agrees with the others. A search for "fuel tank" fails; a
search for "Power Generator" hits the item; a search for `generatormetal` hits
the icon; a search for `PowerGenerator01` hits the prefab. **Any future
enumeration must join across all three, and the join key is the icon path.**

*(`AssemblyStation` is confirmed retail — it appears in
`acs/Bossa.Travellers.CraftingStation/CraftingStationBehaviour.cs`. The other
item ids do not appear in the decompile at all, consistent with §0: item ids
live in data, never in code.)*

### 9.2 A duplicate Power Generator we created ourselves

`itemData.json` now contains **two** Power Generator rows:

| id | size | health | description | origin |
|---|---|---|---|---|
| `powerGenerator` | 2×2 | 40 | *"Generators refine fuel to power engines."* | **RECOVERED** — retail |
| `powerGenerator01` | 3×3 | 100 | *"Generates power for connected devices."* | **WAREBORN** — added by `a3ad3ff` |

The second was added when the generator miss was fixed, named after the
*prefab* rather than the *item*, and given different dimensions and health from
the retail row that was already sitting in the file. Both are live, and
`LoosePartCatalogue.cs` maps both to the same prefab, with the comment *"Two
schematic keys, one prefab"* — so the duplication is known at the catalogue
layer. What is not recorded is that **one of the two keys is ours and the
other is Bossa's**, and only the item rows show it.

**Recommendation (noted, not done):** this is exactly the kind of thing that
becomes load-bearing quietly. `territory_control_beacon` has the same tell —
snake_case in a file that is otherwise camelCase, added in the same commit.
Neither is wrong, but both should be labelled **WAREBORN TUNING** in place so
the next reader does not cite them as recovered.

### 9.3 Metals in our catalogue with no shipped icon

`aurium` and `cobalt` are in `MaterialCatalog` and have no icon in the
client's `metals/` folder, while `magnesium`, `palladium` and `platinum` have
icons and are not in our catalogue. See §6.2. **Unresolved** — stated, not
guessed.

### 9.4 Systems implemented on a different component-id family

Summarised in §7.4 and not repeated here. The eleven entries there are all
legitimate engineering decisions, several forced (the 1244 global-data entity
*did not ship*, so its component **cannot** be implemented). They are listed
because each one is a future false negative: a search for `MetalRockState`,
`GlobalKnowledgeGraphDataState`, `FuelTankState` or `SalvageableState` will
come back empty against a working feature.

### 9.5 Bossa's own typo, shipped

The schematic-category icon is `schematic_icon_furnciture`. If anything ever
keys off that string, it must be misspelled to match.

---

## 10. WHAT I COULD NOT ENUMERATE

*(populated below)*
