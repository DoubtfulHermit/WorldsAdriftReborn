# PLAN — THE COMPLETE FEATURE ROADMAP

**Written:** 2026-08-19. **Branch:** `docs/feature-roadmap`, cut from `main`.
**Scope:** every item on the Worlds Adrift wiki's contents index, plus the
systems that index omits, audited against this repo and the retail decompile,
then ordered into phases.

This is a **planning document**. It contains no game code and proposes no
change that has been made.

---

## 0. HOW TO READ THIS

### 0.1 Provenance key

The repo's existing labels, used here unchanged:

| label | means |
|---|---|
| **PROVED** | read directly off shipped bytes — the decompile, the extracted asset data, or this repo's own code |
| **RECOVERED** | reconstructed from surviving shipped data (a schema, an extracted table, a component contract) |
| **INFERRED** | a reasoned conclusion from PROVED facts; could be wrong |
| **WIKI** | the community wiki. Weakest source. Frequently wrong about post-launch changes |
| **WAREBORN TUNING** | ours. Not Bossa's. Never to be cited as recovered |

**Rule applied throughout:** inventing balance is fine and expected; inventing
provenance is not. Where a number is ours, it says so.

### 0.2 Status vocabulary

- **LIVE** — implemented and reachable by a player on production today.
- **PARTIAL** — exists but cannot be fully used, or is stubbed.
- **MISSING** — not implemented.
- **UNKNOWN** — could not be established from the repo. Stated, not guessed.

### 0.3 What I could not do

- **The wiki itself was unreachable** (HTTP 402 on `worldsadrift.fandom.com`
  for both the Weapons page and the sitemap). Every "what retail had" claim
  below is therefore sourced from the **decompile and the extracted asset
  data in this repo**, which is stronger evidence anyway. Nothing here is
  quoted from the wiki. Where I could not corroborate a checklist item from
  local evidence I say UNKNOWN rather than describing what the wiki says.
- **No live client was run.** Every "the player sees X" claim is static
  analysis unless the handover records a live acceptance.

### 0.4 Documents this roadmap CITES rather than repeats

Do not re-derive any of these. They are the substrate.

| document | branch | what it settles |
|---|---|---|
| `docs/plans/resource-economy.md` | `feat/resource-economy` | 7-phase resource economy. **Phases 1–3 have landed on that branch.** |
| `docs/plans/loot-containers.md` | `feat/loot-containers` | 5-phase loot containers. **Phase 1 has landed on that branch.** |
| `docs/research/findings-resource-catalogue.md` | `research/resource-audit` | the full resource/harvest audit — **corrected in three places by `resource-economy` §0** |
| `docs/research/findings-weather.md`, `docs/research/diag/findings-weather-storm.md` | main | why weather was removed, measured |
| `docs/research/plan-fauna-liveness.md`, `findings-island-fauna.md`, `design-fauna-ecology-wiring.md` | main | what fauna serve today and what retail had |
| `docs/research/gathering/findings-progression.md` | main | knowledge, schematics, tools, lore, quests |
| `docs/research/gathering/findings-crafting.md` | main | recipes are **server-supplied**, not in client bundles |
| `docs/research/gathering/findings-interaction.md` | main | ruin piles are the cheapest interactable |
| `docs/research/findings-wilderness-shrine.md` | main | graduating out of Haven |
| `docs/research/loop/findings-first-hour.md` | main | the authored first hour |
| `docs/component-ids.md` | main | the 443-component id map. Referenced constantly below |
| `docs/HANDOVER.md` | main | the deploy log. **The authority on what is actually running** |

---

## 1. PRODUCTION REALITY CHECK

Read from the live box on 2026-08-19 (`systemctl show wareborn-game -p Environment`,
read-only). This matters because several features are implemented but gated,
and at least one prior document guessed wrong about which.

**Confirmed ON in production right now:**

| flag | value | effect |
|---|---|---|
| `WAREBORN_RELEASE_WORLD_DISTRICTS` | `tier1` | 46 Wilderness islands; 328 deposits, 215 databanks, 328 atlas shards. The shrine moves people. |
| `WAREBORN_ISLAND_FAUNA` | `1` | jellyfish + manta rays spawn |
| `WAREBORN_ISLAND_FAUNA_ECOLOGY` | `1` | schooling/bloom rhythm on |
| `WAREBORN_ISLAND_FAUNA_JUVENILES` | `1` | `1166 AgeState` served; calves at quarter size |
| `WAREBORN_ISLAND_FAUNA_MAX` | `4000` | world creature budget |
| `WAREBORN_SKY_WHALE` | `1` | the single world whale is flying |
| `WAREBORN_HELM_FLIGHT` | `1` | ship flight is armed |
| `WAREBORN_SPAWN_DATABANK` | `1` | the near-spawn *test* bank (real ones are unconditional) |
| `WAREBORN_TERRAIN_INTEREST_ENABLED` | `1` | terrain streaming, 4000 m load / 4800 m unload |
| `WAREBORN_INTEREST_RADIUS_M` | `120` | resource interest bubble |
| `WAREBORN_SPAWN_DEPOSIT` | `1` | deposits on |

**ON by default and not set** (verified default in code, so they are running):
`WAREBORN_SPAWN_TREE` (`WorldsAdriftRebornGameServer.cs:3075`),
`WAREBORN_SPAWN_FUELPODS` (`:3151`),
`WAREBORN_SPAWN_ATLAS` (`:3140`),
`WAREBORN_RELAY_V2` (`Networking/RelayEmitter.cs:57`),
`WAREBORN_TREE_FALL` (`Multiplayer/TreeFall.cs:241` — ON unless exactly `"0"`, with a written justification for departing from the off-by-default convention),
`WAREBORN_WILDERNESS_SHRINE` (`Multiplayer/Wilderness/WildernessShrine.cs:350` — blank ⇒ true).

**Confirmed OFF:** `WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED=0`,
`WAREBORN_SPAWN_METAL=0`, `WAREBORN_SHIP_FERRY=0`, `WAREBORN_METAL_HANDSHAKE=0`.

**This supersedes** the note in `plan-fauna-liveness.md` and any agent
conclusion that fauna and the sky whale are "off by default, production
UNKNOWN". They are on.

**Not present at all:** there is no weather, storm, wind, loot, quest, damage
or cooking environment variable anywhere in the 120-flag census
(`grep -rho 'WAREBORN_[A-Z0-9_]*' --include='*.cs'`). Those systems are not
gated off; they do not exist.

**Security note (unchanged, restated):** the production database credential is
still in the systemd environment. Do not print the full `Environment=`
property. Rotating it into a root-only `EnvironmentFile` remains open work and
is listed in `HANDOVER.md` §10.

---

## 2. THE STATUS TABLE — EVERY CHECKLIST ITEM

Every verdict carries a file:line, a flag, or a stated reason. Paths are
relative to the repo root unless absolute.

### 2.1 Resources

| item | status | evidence |
|---|---|---|
| **Metal** | **LIVE** | Crust→health→core mining loop. `Multiplayer/MetalDeposits.cs:107-137`; salvage shots via `Game/Components/Update/Handlers/MultitoolSalvagerState_Handler.cs`; depletion on `1016 ItemHealthState` (`WorldsAdriftRebornGameServer.cs:2197-2198`). 18 metal item rows in `Game/Items/Config/itemData.json`. Production: 328 tier-1 deposits + Haven's 40. |
| — per-island metal table | **LIVE — but read the nuance** | The table is **not** unused. `Multiplayer/Islands/release-runtime-catalog.json` holds **254 islands, 1,930 deposits, 15 distinct metals**, qualities 1–10, with a `metalSource` provenance ladder (38 `survey-pve`, 23 `survey-pvp`, 193 `inferred-tier`), stamped onto each deposit at `ReleaseWorldCatalog.cs:150-157`. **The trap:** at *default* config the release world is off and every reachable deposit is hardcoded iron (`Multiplayer/MetalDeposits.cs:223` `=> "iron"`, quality 6; `Game/Gathering/DepositHandshakeSpawner.cs:42,45`). **Production is not at default** — it runs `tier1`, so 328 catalogued deposits with real per-island metals ARE reachable, while Haven's own 40 remain iron. Corrected by `resource-economy` §0.1; commit `058877d` fixes the Haven/handshake paths. |
| — island survey metal *lists* | **MISSING** | `Survey.Metals`/`PveMetals`/`PvpMetals` have **zero non-test readers**; only the boolean `MetalsAreInferred` is consumed. Deposit-level data is wired; island-level menus are not. |
| **Wood** | **LIVE** | 13,266 tree seats over 252 islands (`Multiplayer/Islands/ReleaseTreeBudget.cs:40`, `ReleaseTreeCatalog.cs:76`). Felling shipped in game-server `5a69250` (2026-08-19). Log grounding merged to main as `2cc9f02`. 8 wood item rows. |
| **Fuel** | **LIVE as a material; MISSING as a fuel** | Canisters are a **salvage target, not a pickup** — `Multiplayer/FuelPods.cs:10-17,60,87`; recovered per-shot yield 8/8/9 = 25 (`Multiplayer/FuelCanister.cs:65`), arriving on the same `2106` beam path as metal (`WorldsAdriftRebornGameServer.cs:870-875`). Consumed by 6 real recipes (torch, hipLamp, headTorch, campFire, stove, lamp). **Nothing burns it:** no production code matches `combustion\|burn\|fuelLevel\|FuelTank`, and `1104 FuelConsumerState` / `1105 FuelGaugeState` / `1106 FuelTankState` are unserved. Engines do not consume fuel. |
| — dangling doc reference | **housekeeping** | `docs/research/findings-combustion-fuel.md` is cited from four code sites (`FuelPods.cs:48`, `WorldsAdriftRebornGameServer.cs:1526,2941,3148`) and **does not exist on main**. |
| **Atlas Shard** | **LIVE** | `Multiplayer/AtlasShardCatalogue.cs:57` (`ItemTypeId = "atlasShard"`); every release deposit registers a shard, gated by `WAREBORN_SPAWN_ATLAS`/`WAREBORN_ATLAS_RATE`. 328 shards live in tier 1. **One data defect:** `atlasShard` is categorised `"Metal"` in `itemData.json`. `resource-economy` deliberately unbundled that fix, so it is **open** — see §4.1. |
| **Update 27 second economy** (plant fibre, berries, meat, leather, chitin, cloth, pigment, glass) | **PARTIAL, in flight** | `clothMakeshift` ("Makeshift Cloth") is the only `Component` row in `itemData.json`. Plant fibre and berries are **landed on `feat/resource-economy`** (commit `0aa0fe8`, paid off the same cut that pays wood). Meat is blocked on creature mortality (their Phase 7). Leather/chitin/pigment/glass: **MISSING**, and note `loot-containers` §0.3 **corrects the audit** — the recovered scrap `rewards` are metals, woods and fuel, *not* cloth/leather/glass/pigment. |
| **`YieldRule` quality defaults to 0** | **PARTLY FIXED, in flight — and there is a second cause** | Confirmed at `Multiplayer/Gathering/YieldRule.cs:26,53`, with all five registration sites omitting the argument. Fixed on `feat/resource-economy` `d756972`. **Two things that fix does not cover, both still open:** (a) the yield table is keyed by **metal name, not by node** (`Multiplayer/Gathering/HarvestYield.cs:36,50-64` — `_rules[sourceKey] = rule` *overwrites*), so two iron nodes of different quality clobber each other; (b) **crafted output quality is hardcoded to `0`** independent of inputs at `Handlers/PlayerCraftingInteractionState_Handler.cs:297`, and `SchematicRecord.CraftingRequirement` has no quality field at all. Quality is served to the client and honoured for stacking, so the plumbing exists — only the values are zero. Confirm with that branch which of these it claims. |

### 2.2 Environment

| item | status | evidence |
|---|---|---|
| **Weather** | **MISSING — deliberately, and documented** | `1139 WeatherCellState` and `1269 RadialStormState` are in `Multiplayer/ComponentAbsencePolicy.cs:120,146` and its `KnownAbsentComponentIds` (`:265-291`); `Game/Components/ComponentsSerializer.cs:134-137` short-circuits them before the vtable scan. The old seed branches were **deleted with a comment forbidding restoration** (`ComponentsSerializer.cs:1659-1674`). See §5.6 for the full implementation path — this is the best-understood missing system in the repo. |
| — lightning rumble | **LIVE (suppressed storm branch)** | `1254 IslandLightningTimerState` is seeded `50*1000` with a second field of `0`, and the comment states `0` is required "or you will set the island into a storm" (`ComponentsSerializer.cs:1675-1690`). |
| — weather walls | **MISSING as gameplay / LIVE as cartography** | 44 typed segments recovered in `docs/research/world-data/wamap-islands.json`; palette named in `WorldsAdriftServer/Admin/MapWallPalette.cs:38-45` (Wind Rift, Storm Rift, Typhon, Sand Storm, Ice Storm, World End). `HANDOVER.md:1007`: "weather-wall gameplay are not spawned". `1204 WallSegmentState` has **0** server refs. |
| **Biome** | **LIVE as a lookup, PARTIAL as a driver** | `1253 GlobalBiomeVoronoiCentresState` is served and is in the initial load barrier (`ComponentsSerializer.cs:3166-3200`; `LoadBarrierPolicy.cs:136`). Model at `Multiplayer/Islands/IslandBiome.cs:39,51-69`, from Bossa's own 20 Voronoi centres. It drives exactly one thing today: the manta's tail mesh. Biome == tier for 253/254 islands. |
| **Islands** | **LIVE** | 254 in `release-runtime-catalog.json`, 266 in the MapFile, tiers 1–4 (T1=46, T2=51, T3=81, T4=76). Per-peer terrain checkout live and visually accepted for the one-client teleport and ship-approach lifecycles (`HANDOVER.md` §9). Full 254 rollout implemented, **not deployed, not visually accepted**. |
| — Wilderness / teleport shrine | **LIVE** | `Multiplayer/Wilderness/WildernessShrine.cs:75,348`; production is at `tier1` so it moves people (`HANDOVER.md:252-254`). |
| **Wreckage** | **MISSING** | Repo-wide grep for `shipwreck\|wreckage\|\bwreck\b` over all sources hits **only two negative test assertions** (`Multiplayer.Tests/LoadBarrierPolicyTests.cs:91,124`). The client prefab census carries `havenruinedshiprespawner` (line 81) and the decompile carries `Bossa.Travellers.Preprocessors/RuinedShipSpawnerPreprocessor.cs` — **both unreferenced by the server**. Path exists; nothing built on it. |
| **Trees** | **LIVE** | see Wood. |
| **Chests / loot containers** | **MISSING on main; Phase 1 landed unmerged** | `Multiplayer/Islands/IslandResourceInventory.cs:135` — `public int LootContainers => 0;`. **44** loot prefabs are client-resolvable (not 48): `lootchest_001`, `lootchest_kioki`, 6 × `lootcontainer_*`, 24 × `lootruinpile1..24`, 12 × `lootruinpilekioki01..12`. `feat/loot-containers` commit `ebed3c2` implements the vertical slice. |
| **Creatures — Manta Rays** | **PARTIAL (LIVE but inert)** | Spawned and moving in production. Server owns **transform and species and nothing else** — `Game/IslandFaunaService.cs:50-51`: *"IN: nothing. No client sends anything about a creature; there is no update handler, and there is nothing to interact with yet."* |
| **Creatures — Jellyfish** | **PARTIAL (LIVE but inert)** | Same. `FaunaJellySpecies { Seed, Flower, DesertA, DesertB }` exists as a type (`IslandFaunaPolicy.cs:47-60`) but **only the generic `JellyFish` prefab is served**. |
| **Creatures — Thuntomites** | **MISSING — and the name needs care** | **PROVED:** the string "thuntomite" appears **zero times in the entire retail decompile**, and in this repo only as food/knowledge data (`Multiplayer/Knowledge/KnowledgeSpendPolicy.cs:167`, item rows `thuntomiteSteak`/`thuntomiteStew`, 8 cooking icons). **INFERRED (clearly labelled):** the creature the wiki calls a Thuntomite is the shipped **`Beetle`** — the meat item is its product, `4325 BeetleVariantState` exists, `beetle` and `beetleegg` are in the client prefab census (lines 14–15), and the decompile carries `BeetleAging.cs` and `BeetleVariationParams.cs` with Young/Old/**Dead** colour swatches. This is inference from the food chain, not a recovered mapping. The Beetle is the third renderable animal, has complete shipped art, and **is not served**. |
| **Creature health / damage / drops** | **MISSING** | `1160 HealthState` (creature) has no branch in `ComponentsSerializer.cs`. `1171 MortalityState` has **0** refs. Owned by `feat/resource-economy` Phase 7. |
| **Sky whale** (not on the wiki index) | **LIVE** | One whale in the whole world, migrating zone to zone; `Multiplayer/Islands/SkyWhalePolicy.cs`, prefab `DiscoWhale`, call component `4347 BigCallState`. `WAREBORN_SKY_WHALE=1` in production. Retail whale *behaviour* was cut by Bossa — 5 Wwise events declared, present in 0 of 20 shipped banks — so path/period/speed are **WAREBORN TUNING**. |

### 2.3 Culture

| item | status | evidence |
|---|---|---|
| **Civilizations — Kioki Unity, Sabor** | **PARTIAL — a real, fully populated data axis with zero gameplay hooks** | `Bossa.Travellers.World.CivilizationType { Kioki, Saborian }` is a **real schema enum**, served on `1253` at `ComponentsSerializer.cs:3182`. Every one of the 254 islands carries a `culture` field — **165 Saborian / 89 Kioki**; the MapFile's 20 biome zones carry `Civ` (12 / 8); art is culture-tagged (`Ruins (Saborian)/Ornamental/Father Statue 04`, the deployable `KiokiRevivalChamberA`, `lootchest_kioki`, `lootruinpilekioki01..12`). It surfaces **only on the web map** (`WorldsAdriftServer/Web/Assets/map-render.js:513`). Nothing spawns differently and nothing gates on it — `Islands/IslandBiome.cs:27` says so outright: *"zero discriminating power inside a tier."* |
| **Lore — Ruins** | **PARTIAL — decoration only** | Ruins exist as `IslandProps` meshes (`HavenStructures.cs:19,49` — `Ruins (Miscellaneous)`, `Ruins (Saborian)`) and as island names. `8073 ScannableRuinState` is served but its own comment says *"this branch is databank-only in practice"* (`ComponentsSerializer.cs:1376`). The decompile has `RuinLootPreprocessor.cs` and `LootableRuinVfxVisualizer.cs`; the loot spawner's own enum is `IslandLootSpawnerCategory { Ruin, Container, Chest, Marauder }`. **Ruins as a lootable/scannable content type: MISSING.** |
| **Lore — Codex Collection / Codex Pieces** | **PARTIAL — the UI renders, always empty, while 604 pieces of recovered Bossa text sit unused on disk. This is the single cheapest real feature in the repo.** | See §4.2. Components 1240/1241 are seeded *and* 1241 is already granted authoritative (`MirrorSendPolicy.cs:413,483`). The client is actively requesting pieces every 5 s and being dropped. **One missing handler.** |
| **Knowledge** | **LIVE** | Scan→grant→spend→persist all work. Scan: `Handlers/ScannerToolPlayerState_Handler.cs:87-128`. Spend: `Handlers/KnowledgeClientState_Handler.cs:75-148`. Persistence write-through on both paths; `LearnedSchematicsReconciler` at login. Grant = 25 (`Multiplayer/Databanks.cs:60`, WIKI-sourced, replaced a 10,000 test value). **The tree itself is fully RECOVERED**: `Game/Knowledge/Config/knowledge-tree.json` — 20 branches, 228 nodes, with per-node `knowledgeCost`, `nodeType`, `parents` and `schematicList`. |

### 2.4 Cooking

| item | status | evidence |
|---|---|---|
| **Cooking as a crafting mode** | **MISSING — unreachable in principle** | `Multiplayer/Crafting/StationCraftRouting.cs:85-86` routes only two categories, and `ExpectedCategoryFor` (`:69-70`) is a two-way ternary. The client's full enum is `Shipyard=0, Personal=1, CraftingStation=2, Cooking=3, Clothing=4, None=5` (documented at `Multiplayer.Tests/Knowledge/ReferenceDataCrashSafetyTests.cs:24`). **The dead-ness is currently asserted as intended behaviour** by `Multiplayer.Tests/Crafting/StationCraftRoutingTests.cs:168-177`, which explicitly `InlineData("Cooking")`s the rejection. Any fix must change that test. |
| **Food items** | **PARTIAL** | Exactly 4 rows, category `Food`: `thuntomiteStew`, `mantaSteak`, `thuntomiteSteak`, `moonshine`. All 4 are the dead Cooking category. All 4 are **learnable** — `KnowledgeSpendPolicy.cs:167-174` maps knowledge nodes onto them, so a player can spend knowledge, get the "SCHEMATIC LEARNED" card, see the recipe, and have every selection rejected. |
| — **Manta Steak / Thuntomite Steak / Thuntomite Stew** | **MISSING (recipe exists, unreachable)** | as above. Their `craftingRequirements` also mis-name `iron` — see §4.1. |
| — **Berries** | **PARTIAL, in flight** | Landed on `feat/resource-economy` (`0aa0fe8`). Not in `itemData.json` on main. |
| — the rest of the cooking tree | **MISSING, fully RECOVERED** | The retail cooking branch is 9 nodes with real costs: `Campfire` 60 → `Thuntomite Steak` 120 / `Plain Rice` 120 → `Manta Steak` 180 / `Flour` 180 → `Bread` 240 → `Manta Burger` 240 / `Breaded Mushrooms` 240 → `Stove` 240 (`knowledge-tree.json`). Only 4 of these 9 have item rows. |
| **Food effects** | **MISSING, contract fully RECOVERED** | `4335 FoodState` = `{ itemTypeId, List<BuffEffect> buffEffects, float buffDuration, float eatDurationSec, Option<int> amountLeft }`. `4329 PlayerBuffState` = `{ List<Buff> }`, `Buff = { itemTypeId, buffEffects, endTime, duration }`. `1186 StomachState` = `{ float fullRatio }`. **The complete buff vocabulary is six strings, PROVED** from `acs/Assets.Scripts.Player/PlayerBuffBehaviour.cs:75-108`: `movementSpeed`, `jumpForce`, `sprintAcceleration`, `climbSpeed`, `drunk`, `halloween`. Semantics: multiplier `= 1 + buffValue`. All three components have **0** server refs. |

### 2.5 Tools

| item | status | evidence |
|---|---|---|
| **Gauntlet** | **LIVE** | The four `gauntlet_*` rows are **UI/hotbar shells, not real items** (`docs/research/gathering/findings-tools.md:103-105`); the modes are innate, selected by `1211.itemSlot`, and all four are unlocked by the static seed `ToolState.Data(new ToolStateData(30))` at `ComponentsSerializer.cs:1804-1806`. This is what actually mines and fells. **No tool tier, quality or durability exists** (`findings-tools.md:149`). |
| **Grappling Hook** | **LIVE, including co-presence** | Innate, no `[Require]` at all. Rope is **client-simulated by the owner**, who is the authoritative writer of `1098 RopeControlPoints` (`Multiplayer/MirrorSendPolicy.cs:73,417`); the server only ever seeds an empty point list. 1098 is in `RemoteSeedComponents` (`:113`) and not relay-excluded, so **other players see your line**, drawn by the stock game (`WorldsAdriftReborn/Patching/Multiplayer/RemoteGrappleLine.cs:56-63,142`). |
| **Glider** | **LIVE** | `Glider_Patch.cs:8-23` forces infinite energy; item granted to every new character (`Game/Items/ItemHelper.cs:146`) but **unslotted**, so the player must equip it. Replicated to others through `6910 UtilitySlotActivatedState` head/body/feet bools, re-emitted bools-only on transitions because the raw relay caused a ~170 Hz flood and a peer drop (`Handlers/UtilitySlotActivatedState_Handler.cs:47-95`). |
| **Atlas Lifter** | **PARTIAL — a prop that does nothing** | Item, recipe (Metal×2 + atlasShard, 10 s) and knowledge node all exist. Placement registered at `Multiplayer/Placement/Deployables.cs:246-247` with seed `TransformOnly` and the inline TODO `// +1021 LifterState`. **`1021 LifterState` has zero code references** anywhere except that comment. It can be crafted, placed, and is inert. |
| **Torch** | **PARTIAL — item only, emits no light** | Item + recipe exist; it is one of only six starter recipes (`Multiplayer/Crafting/StarterSchematics.cs:23`) precisely because it has no knowledge node and would otherwise be forever uncraftable. As a `Tool`-slot item it is **excluded from the wearable pipeline** (`Multiplayer/Inventory/WearableInvariants.cs:70-74,104-108`) so the client builds no UtilityItem for it. Placed `campFire` and `lamp` props exist with unseeded `1012 CampfireState` / `1108 LampState` (`Deployables.cs:248,250`). |
| **Day/night cycle** | **MISSING** | The only occurrence is a fauna scheduling constant, `Multiplayer/Islands/IslandFaunaMovement.cs:152` `DayNightCycleSeconds = 600.0`, consumed only by fauna rhythm. **No world clock is replicated to any client.** This is why "Craft a Torch to light up the dark" has nothing to light. |
| **Gear** | **PARTIAL** | Equip/unequip is live and server-authoritative (`Handlers/InventoryModificationState_Handler.cs:66-67`, policy `Multiplayer/Inventory/InventoryPolicy.cs:260,308`). Durability is derived into `1280 WearableUtilsState` and drained client-side. **Quality and rarity are inert**: carried on the wire and in snapshots with no gameplay consumer (`Game/Inventory/InventoryWire.cs:124,154`; `Multiplayer/Inventory/InventorySnapshot.cs:132,201`). |
| **Weapons** | **MISSING. There is no damage system of any kind.** | See §2.7 — this is the largest single hole and it deserves its own row. |

### 2.6 Ships & Crafting

| item | status | evidence |
|---|---|---|
| **Shipyard** | **LIVE end-to-end** | place → access (`ShipyardBuildAccess.cs` sets `1219 ShipyardId`) → build lists → select → fill → craft → spawn (`Game/Crafting/ShipBuildCompletion.cs:34+` → `BuiltShipSpawner`). |
| **Assembly Station** | **LIVE** | `Deployables.cs:214-216`, `isCraftingStation: true`. Note the asset is named `"CraftingStation"` because **no loadable `AssemblyStation` prefab exists in the client bundles** (`:203-208`) — without the workaround the station could be selected and never placed. |
| **Assembly Crafting** | **LIVE** | 37 recipes; `Handlers/PlayerCraftingInteractionState_Handler.cs:352-587` — realizability gate, at-most-one guard, atomic consume, timed hold, spawn, refund on failure. `LoosePartCatalogue` has exactly 37 rows: 1:1 coverage. **All 37 audited row by row in §11**: 36 render, 4 are interactable, 6 more should be, and 23 can be placed only on a flat deck. |
| **Inventory Crafting** | **LIVE** | 18 recipes; same handler `:271-331`. **My brief's lead that the 1003 handler is a stub is FALSE** — it is 681 lines and the most fully realised handler in the crafting stack. Do not cite it as a stub. |
| **Cooking Crafting** | **MISSING** | §2.4. |
| **Schematics — items** | **LIVE** | 60-record catalogue served over `1097 SendSchematicData`. Recipes are **server-supplied, not in client bundles** — exhaustive UnityPy scan, 0 hits (`findings-crafting.md`). Starter grant is 6 recipes (`StarterSchematics.cs:23-34`), two flagged temporary. |
| **Schematics — ship frame designs** | **LIVE** | Load/Update/Save/Reset/Unload/Rename with per-command acks (`Handlers/ShipHullAgentClientState_Handler.cs`), blobs validated by `ShipPlanModel.TryDecode`. |
| **Schematics — ship blueprint bill of materials** | **STUB** | `Multiplayer/Crafting/ShipBlueprintRecipe.cs:107-115` is a banner comment reading `TEST RECIPE - NOT THE ORIGINAL WORLDS ADRIFT NUMBERS`. **Every** blueprint resolves to the same hardcoded `TestMakeshiftShip()` — 3 birch + 2 iron, 10 s — regardless of what the player selected (`Handlers/PlayerShipBlueprintInteractionState_Handler.cs:139`). Ships are effectively free. |
| **Ship part salvage** | **LIVE** | `Multiplayer/Ship/ShipPartSalvagePolicy.cs`, full recipe refund, 15 m work radius, owned-shipyard gate. ⚠ It inherits the iron bug symmetrically: salvaging a sky-core module refunds **iron**, not atlas shards. |
| **Ship components (the 37 assembly-bench parts)** | **LIVE, with one invisible row and six inert ones** | Full per-part audit in **§11**. Every prefab name resolves, every seeded id has a serialiser branch, every recipe is knowledge-reachable. The `window` renders nothing (a mesh-selection failure, fixed on `docs/ship-components`); the four storage containers, the personal reviver and the sky core are visible and dead. |
| — instrument mounting surfaces | **PARTIAL — narrower than retail, knowingly** | 23 of 37 rows are authored `"deck"`, so an altimeter goes on a flat deck and nowhere else. Retail chose the surface from a per-item server string with seven flag values; **those values are unrecoverable** (no item table ships in the client). §11.6. |
| **Hull material → flight** | **LIVE** | `Game/ShipFlightService.cs:1033-1047` — `HullMassCalculator.HullMassKg(...)` → `AgilityScale`. Pre-feature ships degrade to `HullMaterials.Legacy`. |
| **Helm flight** | **LIVE** | Gated at `ShipFlightService.cs:63` on `WAREBORN_HELM_FLIGHT`, which **is `1` in production**. |
| **Ship docking (1205)** | **MISSING** | Explicit follow-up: `ShipBuildCompletion.cs:20-23` — *"DOCKING (1205.dockedShipId) is NOT wired: the ship spawns FREE next to the yard."* |
| **Sails** | **PARTIAL** | Functional scalar propulsion per unfurled sail, **not** retail wind/tacking/rigidbody torque (`HANDOVER.md` §6, §10). Blocked on weather for the real version. |

### 2.7 Gameplay

| item | status | evidence |
|---|---|---|
| **Getting Started** | **PARTIAL — deterministic spawn and starting kit; no tutorial** | Starting inventory at `Game/Items/ItemHelper.cs:137-150` (4 gauntlet shells on hotbar 0-3, glider, `torso_poncho`, `head_devhat`, plus stash cosmetics). Spawn at `Multiplayer/SpawnPolicy.cs:135`, 5.5 m from Haven's ruined metal camp. The code says it plainly (`SpawnPolicy.cs:74-76`): *"its bundle contains no teleporter, no barrier dome, no respawner, no starter ship… This is a small pretty island with a ruined metal camp, not a tutorial."* `8055 NewPlayerState` is seeded **false** on purpose, because `true` would be a permanent prison — its only exit, `8056 LeaveHavenRequest`, has no handler. **But see §4.3: the retail tutorial is recoverable and nearly free.** |
| **Patch List** | **LIVE** | `/patchnotes` generated from the commit log (`tools/patchnotes/build-changelog.sh`), 510 rows live; in-client PATCH NOTES button redirected by `Patching/LandingScreen/PatchNotesButton_Patch.cs`. `WAPatch.exe` served from `/download`. `feat/patchnotes` fully merged. |
| **Mining** | **LIVE** | §2.1. |
| **Rope Physics** | **LIVE (client-simulated)** | §2.5. Grapples *across ships* remain a hard frontier needing domain affinity (`docs/roadmap.md:71`). |
| **Clothes** | **PARTIAL — the wardrobe exists but is mostly unnamed** | Appearance replication is **LIVE**: `1088 PlayerPropertiesState` carries `bossaNetCharacterData`, published once at spawn by the mod (`Patching/LoadInGame/CharacterCustomisationVisualizer_Patch.cs:37-60`) and re-seeded to late joiners; worn garments show on others because `1081 InventoryState` is seeded **per-entity from the real store** including each item's worn `slotType` (`ComponentsSerializer.cs:617-648`). **The data is the problem:** `itemData.json` holds **164 garment rows** (55 Head, 81 Body, 28 Feet) — but **135 of them have a blank display name**, and **52 rows across the whole catalogue have a blank `itemTypeID`** and are therefore unaddressable. Starter clothing is 2 items. |
| **Weapons / damage / death / respawn** | **MISSING — no part of it exists** | The server writes `1077 HealthState` **once as a static seed** — `HealthStateData(200, 200, true, 0f, true, {}, 1f, 1f)` at `ComponentsSerializer.cs:698-700` — and never updates it. Stated three times in code: `WorldsAdriftRebornGameServer.cs:4217` and `Multiplayer/SpawnPlan.cs:87` ("this server writes no HealthState, so there is no fall damage to end it"), and `SpawnPolicy.cs` ("a bad spawn is an endless fall rather than a death"). Zero non-test refs for `1096 PistolState`, `1249 PlayerPistolState`, `1084 DealDamageClientRequestState`, `1091 DamageOverrideState`, `1195 DamageDealerState`, `1171 MortalityState`. The complete client hand-item enum is `{None, Multitool, ScannerTool, Pistol, Food, MusicalInstrument}` and `Weapon : UtilityItem` is a 24-line stub — **there is no melee tool** (`findings-tools.md:111-112,123`). |
| — but a pistol is obtainable | **PARTIAL** | `pistol` and `pistolBullets` are real item rows with real recipes and reachable knowledge nodes (`KnowledgeSpendPolicy.cs:177-178`). `PlayerPistolBehaviour` is component #28 on the shipped player prefab. Firing produces **nothing server-observable**. |

### 2.8 Systems the wiki index omits entirely

Found while auditing. Each is a real shipped client system with a component id
and no server implementation. Listed so the roadmap is honest about the true
size of the gap.

| system | ids | server refs |
|---|---|---|
| Revival chambers / respawn network | `1092`, `1093`, `1094`, `1029`, `6905 AncientRespawnerState` | ~5 total, none a working respawn |
| Ship wiring (wiring kit, wireable, triggers) | `1213`–`1216` | **0** |
| Locks and lockpicking | `1217`, `1218`, `1220`, `1221` | **0** |
| Turrets — ship and island | `1122`, `1371`–`1374`, `4444`, `4445` | **0** |
| Territory control | `1273 OwningAllianceState`, `1262 AllianceRadioState`, item `territory_control_beacon` | **0** (the beacon's recipe is the one dead `Shipyard`-category recipe) |
| Photo camera / photo book / picture frames | `1024`–`1028` | **0** |
| Musical instruments | `1023` | 19 (items exist: guitar, marauder guitar, horn) |
| Atlas anchors / compass chests | `1136`–`1138`, `1265 AtlasCompassChestState` = `{Option<Coordinates> target, Option<float> timeUntilSelfDestruct}` | **0** |
| Ship lift | `1258` | 12 |
| Bombs / explodables | `1014`, `1015`, `1350` | **0** |
| World bounds | `1250` | **0** |
| Marauders (the 4th loot category) | `IslandLootSpawnerCategory.Marauder` | **0** |

---

## 3. WHAT IS UNRECOVERABLE — OURS TO DECIDE

The GSim was Scala and is gone. These are **design decisions we must make**,
not facts we can restore. Anything shipped here must be labelled
**WAREBORN TUNING**.

1. **Damage → yield.** The formula lived in the lost Scala worker. The
   decompile has the field names (`metalDepositDensity`, `minMetalRockDeposits`)
   and confirms the island reports its LOD0 mesh count to the spawner, but not
   the maths. **Note the existing correction in `HANDOVER.md:1065-1069`:
   the 0.05/cell deposit density was previously called "the recovered retail
   figure" — it is not. The SHAPE is retail; the value is ours.**
2. **Quality → stat.** Item `quality` and `rarity` reach the client and no
   consumer reads them. What Q7 iron should *do* to a crafted part is ours.
3. **Loot tables and drop weights.** `loot-containers` §0.2 proves there is
   **no loot table anywhere in the shipped client** (7 search terms, 0 hits
   across `acs/`, `gencode/`, `ecs/`). What *is* recovered: the container-count
   `DoMath` formula, `maxDataBanks = 5`, the placement rule, the refill schema,
   and the 134 `Salvage` rows keyed by tier. The 19 tuning constants of
   `1244 LootablePerAreaDataState` did not ship.
4. **8 creature material display names.** Unrecoverable. `resource-economy`
   correctly leaves them UNKNOWN rather than inventing them.
5. **Ship blueprint costs.** `TestMakeshiftShip()` is ours and is labelled as
   such. The real bill of materials per hull is a design decision.
6. **Buff values.** The six buff *type strings* are PROVED; the *values*
   Bossa attached to each food are not.
7. ~~**Lore text.**~~ **STRUCK — this is recoverable and is already recovered.**
   178 titles and 604 pieces of genuine Bossa lore text are already in
   `itemData.json`, unused (§4.2). What remains ours is only the *distribution*:
   which piece is found where, and whether the Codex ties into anything.
8. **`cobalt` and `aurium`** are **ours**, not Bossa's. Retail's three
   unshipped metals were magnesium, palladium and platinum. `resource-economy`
   declares renaming out of scope; it stays a known cosmetic debt.
9. **Sky whale path, period, speed, altitude.** Retail whale behaviour was cut
   by Bossa before shutdown (5 Wwise events declared, 0 present in 20 banks).
10. **Weather dynamics.** Four of retail's six weather systems are **not in the
    client's FixedUpdate config**, so client-side weather can never be dynamic
    by design. Every dynamic must be authored server-side. The *shape* is
    retail; the weather model is ours.

---

## 4. THE CHEAP UNLOCKS, ORDERED

Biggest gameplay gain for least risk. **None of these needs a schema
migration. None needs a client-mod change.** Each is unclaimed by the four
branches in flight unless noted.

**Do them in this order:** §4.2 (the Codex — most content per line of code in
the entire repo), §4.1 (the atlas-shard sink — a one-file data edit that gives
mining a point), §4.3 (the tutorial — biggest payoff, most unknowns), then
§4.4 and §4.5 (two small serialiser branches). They are numbered below by
theme, not by priority.

### 4.1 — Seven recipes demand Iron where the UI promises Atlas Shards

**Cost:** a data edit in one JSON file. **Risk:** near zero.

`SchematicRecord.Name` is the *only* match key (`Multiplayer/Crafting/CraftingPolicy.cs:70-90`);
`Component` is a cosmetic label the player reads and the server ignores
(`Multiplayer/Crafting/SchematicRecord.cs:64-71`). Thirteen recipes / fourteen
rows exploit this. Seven of them are the sky-core modules — `skyCoreGenerator`,
`skyCoreAirFilter`, `skyCoreCoolantSystem`, `skyCoreStabiliser`,
`skyCoreComputer`, `skyCoreCircuitryNetwork`, `skyCoreEfficiencyModule` — each
carrying `"name": "iron"` under the label `Atlas Shards`.

This **bypasses the atlas-shard sink entirely**: the one thing the whole mining
loop pays out has almost no use. `atlasShard` is already used correctly by 3
other rows, so the fix is proven-shaped.

- **Ownership:** `feat/resource-economy` explicitly **unbundled** the atlas
  fix, and its Phase 6 covers only the 4 Cooking rows. The 7 sky-core rows and
  the 2 Personal rows (`clothMakeshift` → "Plant Fibers", `loom` → "Strings")
  are **open**. Coordinate on the file, since that branch edits `itemData.json`
  and `schematicData.json` too.
- **Also fix while there:** `atlasShard` is categorised `"Metal"` in
  `itemData.json`, which is why it sorts with the ores.
- **Watch:** salvage refunds are symmetric (`ShipPartSalvagePolicy.Refunds`
  sums `CraftingRequirements`), so changing the requirement changes the refund.
  That is correct, but it means existing built parts refund differently after
  the change. Decide whether that matters before shipping.

### 4.2 — The Codex renders today. It is empty because nobody fills it.

**Cost:** one serialiser change plus one event handler. **Risk:** low — half
the work is already shipped and proven.

**PROVED contract, read off `gencode/Bossa.Travellers.Player/`:**

- `1240 LorePiecesCollectorGsimState` — data `List<string> knownLore`;
  server-fired event `RequestLorePiecesDataResponse(Map<string, LorePiece> pieces)`.
- `1241 LorePiecesCollectorClientState` — no data; client-fired event
  `RequestLorePieces(List<string> ids)`.
- `LorePiece = { string title, int pieceNumber, int totalPiecesInSet, string text }`.

**What is already done.** Both ids are wired. `MirrorSendPolicy.cs:461-483`
records that 1240 is injected-but-not-granted specifically because
`LorePiecesCollectorVisualizer` `[Require]`s both, and with only 1241 granted
the visualiser's `_serverState` stayed null →
`LoreUI.RefreshLore` → `LogbookUI.ProtectedInit` → **an uncaught NRE that took
down the entire character sheet** (verified in a real client log). The
serialiser answers 1240 with an **empty** `KnownLore` list
(`ComponentsSerializer.cs:1794-1798`) purely to stop that crash.

**So the Logbook tab opens and renders — with nothing in it.** The client
already carries the whole UI: `LoreUI.cs`, `LoreCategory.cs` (categories with
unread highlights), `LoreItem.cs` (piece counters, "n of N"), `LoreTextPiece.cs`.
And `itemData.json` already has the row `lore | Codex - Lore Piece`.

**And the text is already on disk. This is the headline.** The `lore` row in
`Game/Items/Config/itemData.json` carries an `entries` field holding
**178 titles and 604 individual lore pieces** — genuine recovered Bossa text,
present since the upstream import in commit `50fa6da` (2023-02-13). Titles
include *A History of Sabor*, *Hette Boege Guide to the Kioki Unity — Central
Karem*, *Astor's Logbook*, *Book of Fire/Ice/Soil/Wind*, *Deities of Gall*,
*Court of the Empress*. **Nothing reads that field** — `grep -rn "entries"` over
`Game/Items/` returns zero hits, and the `lore` item row is never granted.

Its shape (`title -> [text, text, ...]`) maps **one-to-one** onto
`LorePiece{title, pieceNumber, totalPiecesInSet, text}`. That is not a
coincidence; it is the same data.

**The client side is not merely present, it is actively asking.**
`acs/LorePiecesCollectorVisualizer.cs:54-77` diffs `KnownLore` against a local
cache, fires `TriggerRequestLorePieces(missing)` on 1241, and **retries every
five seconds until satisfied**. `acs/LoreUI.cs:141` groups pieces into
Incomplete/Completed with unread markers persisted to `PlayerPrefs`. And
`acs/Travellers.UI.PlayerInventory/LogbookUI.cs:9-13,32` shows the Logbook has
`enum LogbookTab { Diary, Codex, Photos }` with **Codex as the default tab**.

**The work is one handler.** There is no handler for 1241 anywhere —
`Game/Components/Update/Handlers/` has 21 handlers and none of them is it, so
the client's `RequestLorePieces` event arrives and is dropped. Add: (a) a 1241
handler that answers with `RequestLorePiecesDataResponse` built from the
`entries` bank; (b) writes into `knownLore` when a player collects a piece.
`docs/research/gathering/findings-progression.md:135-146` reached the same
conclusion independently: *"the cheapest authored content in the game… One new
handler. Zero new seeds, zero client patches, zero world entities."*

**Two honest caveats:**
1. **Lore feeds nothing.** That same research notes an exhaustive search found
   no link between lore and knowledge. Collecting the Codex is its own reward
   unless we invent a tie — and inventing one is a design decision, not a
   restoration.
2. **There is no carrier yet.** Nothing in the world drops a lore piece. The
   obvious physical carrier is the **24 `lootruinpile*` prefabs** that already
   exist and are unreferenced (§8), which puts the *complete* Codex loop just
   behind Phase 1's loot work rather than inside Phase 0. Phase 0 can ship the
   handler and a starting grant; Phase 8 gives it places to be found.

### 4.3 — Bossa's tutorial is sitting in the client, gated behind two components

**Cost:** serve one component, grant one component. **Risk:** low-medium
(needs a live client check). **Payoff: the entire onboarding experience.**

This is the biggest gain-per-line item in the audit.

**PROVED.** `acs/Travellers.Quests/QuestManager.cs:11-18` is
`[WorkerType(WorkerPlatform.UnityClient)]` and `[Require]`s exactly two things:

```
[Require] private PlayerQuestState.Writer  _questState;        // 8053
[Require] private PlayerQuestRequestState.Reader _gsimStateReader; // 8054
```

`8053 PlayerQuestState` = `{ List<Quest> runningQuests, List<int> completedQuests }`.
Both ids have **zero references anywhere in this server** (verified by grep;
the only `quest` hits in the C# are the substring in `Request`).

**Quest definitions are client assets, not server data.** `QuestGivingManager`
holds them as `[SerializeField] QuestData` — `EatBerriesQuest`,
`EquipScannerQuest`, `LearnShipBuildingQuest`, `RegisterToReviverQuest` — and
evaluates every condition client-side. The engine is **124 files** with
conditions including `UseGrappleCondition`, `CraftShipPartCondition`,
`HasLearnedKnowledgeNodeCondition`, `ConsumeItemCondition`,
`PlaceItemInWorldCondition`, `ItemPresenceInInventoryCondition`.

**And the quests themselves are already extracted, with Bossa's own text**, in
`docs/research/loop/data/quests.json`:

| id | asset | title | steps |
|---|---|---|---|
| 100 | `100-ExitAncientSpawner` | Escape the crash site | 3 |
| 101 | `101-Scanner` | Craft and Equip a Scanner Tool | 8 |
| 104 | `102-UnlockRevivalChamber` | Unlock the Reviver network | 4 |
| 105 | `103-LeaveHaven` | Access the Revival chamber located at the center of the island | 2 |
| 110 | `201-UnlockShipbuilding` | Gather Knowledge and learn Shipbuilding | 4 |
| 111 | `202-BuildFirstShip` | Build your first ship solo or with a crew. | 12 |
| 120 | `104-Torch` | Craft a Torch to light up the dark | 10 |
| 130 | `105-EatBerries` | Heal by eating food. | 5 |

Quest 100's first step is *"Use your grapple to escape from your ship"* with a
`UseGrappleConditionData` — and the grapple works today.

**Honest assessment of which would actually complete:**

- **Plausibly completable now:** 100 (grapple), 101 (scanner + craft), 110
  (knowledge + shipbuilding), 111 (build a ship). These map onto systems that
  are LIVE.
- **Would stall:** 104 and 105 (need revival chambers — no server system of
  any kind exists), 130 (needs food and healing — both missing), 120 (torch is
  craftable but there is no dark and no light).

So this unlock is best shipped as **a curated subset first**, not all eight.
That is a decision, not a limitation.

**Open questions before building it** — flag as UNKNOWN, do not assume:
1. Whether the four `QuestData` assets survive on the shipped prefab (they are
   `SerializeField`s; the reference should be baked, but this is unverified).
2. Whether granting authority over 8053 to the client is safe under
   `MirrorSendPolicy`'s first-time-setup rules (rule 6: grants may only run
   against the sender's own entity).
3. Whether `PlayerQuestRequestState` (8054) needs any payload at all, or
   whether an empty seed suffices — its `Data` struct appears to carry nothing,
   the same shape as `1241`.

### 4.4 — Placed fires and lamps emit nothing

**Cost:** two serialiser branches. **Risk:** low, with one specific trap.

`campFire` and `lamp` are craftable, placeable and inert:
`Deployables.cs:248,250` carry the TODOs `// +1012 CampfireState` and
`// +1108 LampState`. `1012 CampfireStateData = { float elapsedTime, float intensity }`.

**The trap, which the loom already demonstrates:** every interest call site
passes `failOnComponentInitError: true`, so **one id with no seed drops the
ENTIRE batch, including 190602 TransformState**, leaving the prop at the
world origin. A serialiser branch is mandatory, not optional
(`ComponentAbsencePolicy.cs:84-90`).

### 4.5 — The Atlas Lifter is a prop

**Cost:** one component, `1021 LifterState`. **Risk:** low to build, UNKNOWN to
verify (needs a live client to see whether anything actually gets lighter).

Same shape as 4.4. `Deployables.cs:246-247` already carries the TODO.

### 4.6 — Already claimed, listed for ordering only

- **`1264 InventoryItemCraftingStationState` / the loom** → `feat/resource-economy`
  Phase 4. Contract is RECOVERED: `{ Option<EntityId> craftedBy, bool isReady,
  bool itemTaken, bool hasTriedAddingToInventory, Option<string> craftedSchematicId,
  List<SlottedMaterial> materialsUsed }` — i.e. the station's *output* state.
  That branch flags a broader open question worth adopting: **the same missing
  activation component may affect 16 of 18 deployables.** Nobody owns that
  audit. It belongs in Phase 1 below. **Note the scope carefully:** that is the
  *deployable* table (`Placement/Deployables.cs`). The 37 *ship components* are a
  different table with a full seed contract and are audited separately in **§11**
  — they are not under-seeded, and two of their three symptoms have other causes.
- **Cooking routing** → `feat/resource-economy` Phase 6. Note it must also
  change `StationCraftRoutingTests.cs:168-177`, which currently asserts the bug
  as intended.
- **Quality reaching the item** → landed, `feat/resource-economy` `d756972`.
- **Per-island deposit metals** → landed, `feat/resource-economy` `058877d`.

---

## 5. THE PHASED ROADMAP

Ordered by **what most unblocks the game per unit of risk**, not by wiki order.

Each phase states: what it delivers · what a player can newly DO ·
dependencies · schema migration · networked state (soak gate) · main risk.

**Standing rule, from `docs/multiplayer.md` and the standing multiplayer-safety
rule:** any phase that adds a new high-rate or reliably-relayed component must
run `tools/relaybot/run-soak.sh` and show a FLAT curve before deploy. Phases
below are marked **SOAK** where that applies.

**Standing rule, from `HANDOVER.md`:** a phase that changes the client DLL
needs a **patcher release**, which is a different shipping path from a server
deploy. Those phases are marked **CLIENT MOD** and listed together in §6.

---

### PHASE 0 — The cheap unlocks
*Deliver the five items in §4.1–4.5.*

- **Player can newly do:** read 604 pieces of real Worlds Adrift lore in the
  Logbook; follow Bossa's own tutorial from the crash site; light a campfire;
  spend atlas shards on the sky cores they were always meant to buy.
- **Depends on:** nothing.
- **Migration:** no — **except** that `knownLore` is per-character progress and
  should eventually join the four existing per-character stores. Note
  `ProgressionState` has **no `KnownLore` field** today even though
  `findings-progression.md:158` says the schema should carry one. Ship the
  handler first with an in-memory ledger; add the column deliberately.
- **SOAK:** no (no new periodic sender). **CLIENT MOD:** no.
- **Main risk:** the quest grant (§4.3) is the only item with real unknowns —
  authority over 8053, and whether the QuestData assets are baked. Ship §4.2,
  §4.1, §4.4 and §4.5 first; treat §4.3 as its own live-verified step.

---

### PHASE 1 — Make what is already built actually reachable
*No new systems. Close the gaps between "implemented" and "usable".*

1. **Ship blueprint bill of materials.** Replace `TestMakeshiftShip()` so a
   hull's cost depends on the hull. Today every ship in the game costs 3 birch
   + 2 iron regardless of size — the entire mining and logging economy has no
   sink. **This is the single biggest balance hole in the game.**
   The numbers are **ours** and must be labelled WAREBORN TUNING.
2. **Ship docking (`1205.dockedShipId`).** The documented follow-up at
   `ShipBuildCompletion.cs:20-23`.
3. **The deployable activation audit.** `resource-economy` §2 flags that the
   loom's missing `1264` may be one of **16 of 18** deployables missing a
   client-required activation component. Nobody owns this. Audit all 18
   against their client `[Require]` sets and produce the list. Cheap, and it
   tells us how much of Phase 3 is already half-built.
   **Two corrections from §11, which did the equivalent audit for ship parts.**
   (a) `[Require]` coverage alone is **not enough** — `InteractiveObjectVisualizer`
   caches the entry matching its *prefab-baked verb* once in `OnEnable`, so a
   fully-seeded prop served the wrong verb still shows no prompt at all. Audit
   both halves. (b) There is a third failure shape neither half catches: a
   component that is present and correctly typed but carries a **value the
   client's art cannot render** (§11.3). Budget for it.
4. **Land `feat/loot-containers` Phase 1 + 2a.** Their work, sequenced here
   because chests are the first new *content* a player meets.
5. **Clothing catalogue repair.** 135 blank display names and 52 blank
   `itemTypeID` rows. Naming is ours; the blank ids are a data bug.

- **Player can newly do:** loot a chest; build a ship that costs something;
  dock it; wear clothes that have names.
- **Depends on:** Phase 0 only for ordering.
- **Migration:** `loot-containers` Phase 2b is a migration
  (`loot_container_state`) — **defer it**; their plan says ship 2a first and
  warns that a split deploy has already destroyed player progression once.
- **SOAK:** yes for loot streaming. **CLIENT MOD:** no.
- **Main risk:** file collisions. `feat/loot-containers` and
  `feat/resource-economy` both edit `Game/Inventory/InventoryService.cs`,
  `Game/Gathering/WorldResourceActivation.cs` and
  `WorldsAdriftRebornGameServer.cs`. Merge order must be decided explicitly.

---

### PHASE 2 — The death loop
*Health, falling, dying, and coming back. No weapons yet.*

This is the foundation everything violent depends on, and it is worth doing
**before** weapons because a game where you can kill but not die is worse than
one with neither.

1. **Player health becomes authoritative.** Today `1077 HealthState` is a
   static seed written once (`ComponentsSerializer.cs:698-700`). Make the
   server own it.
2. **Fall damage.** The code notes in three places that its absence is why a
   bad spawn is "an endless fall rather than a death". `FallWatch` already
   tracks the fall verdict for interest purposes — reuse it.
3. **Revival chambers.** `HANDOVER.md:1061-1065` records these as DEFERRED
   because *"there is no server system of any kind"*, while the release
   catalogue already carries **surveyed revival chambers on 12 tier-1
   islands**, and Haven's revival tower is already stood up (`81fde92`). The
   ids are `1092`/`1093`/`1094`/`1029` and `6905 AncientRespawnerState`.
4. **Death and respawn.** Register at a reviver, die, wake there.

- **Player can newly do:** die. Register at a Reviver. Respawn. **And
  complete retail quests 104 and 105**, which are otherwise permanently
  blocked.
- **Depends on:** Phase 0's quest work to get the payoff.
- **Migration:** likely yes — a registered-reviver binding is per-character
  state and belongs in Postgres alongside inventory/knowledge/logout-position.
  Follow the four-store pattern already proven at boot.
- **SOAK:** yes — health is a per-player replicated component.
- **Main risk:** the highest-consequence phase in the roadmap. Death that
  loses inventory, or a respawn that lands a player inside terrain, is worse
  than no death. Every `HANDOVER.md` spawn-safety lesson applies. Also:
  **invisible per-life state** — anything reset on death with no visible tell
  will be forgotten by some reset path.

---

### PHASE 3 — Weapons, and things that can be killed
*The damage spine.*

1. **The pistol first.** It is the only weapon that already has an item, a
   recipe, reachable knowledge nodes (`PistolsRootSchematic`, `PistolsSchematic2`),
   ammo (`pistolBullets`), and a behaviour on the shipped player prefab
   (`PlayerPistolBehaviour`, component #28). Ids: `1096 PistolState`,
   `1249 PlayerPistolState`, and the validation pair `1247 ShotValidationRequestState`
   / `1248 ShotValidationResponseState`, plus `1295 DeterministicProjectileState`.
2. **Creature mortality.** `feat/resource-economy` Phase 7 owns this
   (`1160`/`1161 HealthState`, `1171 MortalityState`, corpse fall **reusing the
   log grounding already on main**). Their 7a-iii — the trigger between firing
   and a corpse — is explicitly **UNSIZED**, and it is exactly the seam this
   phase fills. Coordinate directly.
3. **Melee is a bigger job than it looks.** `Weapon : UtilityItem` is a 24-line
   stub and the client's hand-item enum has no melee entry. The knowledge tree
   carries four full melee branches — `1hblade`, `1hblunt`, `2hblade`,
   `2hblunt`, 40 nodes between them — with **no items behind any of them**.
   Treat melee as its own later step, not part of this phase.
4. **Jellyfish contact damage.** `4323 ContactFixedDamageState` /
   `4324 ContactFixedDamageFsimState` — the jelly shock. Small, once damage
   exists.

- **Player can newly do:** shoot; kill a creature; get meat.
- **Depends on:** Phase 2 (health must exist first).
- **Migration:** no.
- **SOAK:** yes — projectiles and shot validation are new networked traffic,
  and shot validation is request/response, which is exactly the shape that
  built the reliable-retransmit spiral before.
- **Main risk:** shot validation is a round trip on the hot path. The relay
  history in `HANDOVER.md` (49 KB in flight, 6.8 s RTT) is the warning. Also:
  the client-side salvage-chop hack and the native 1211 tool path may contend
  for the left mouse button — flagged as **never run** at
  `MirrorSendPolicy.cs:400-411`, and adding a firing weapon makes it urgent.

---

### PHASE 4 — Cooking, food and the second economy payoff
*Now that creatures can die, the food chain closes.*

1. **Cooking routing** (`resource-economy` Phase 6) — plus the test change.
2. **The stove and the loom** (`resource-economy` Phase 4, `1264`).
3. **`4335 FoodState` and `4329 PlayerBuffState`.** The contract and the
   **complete six-string buff vocabulary are PROVED** (§2.4). Buff *values*
   are ours.
4. **`1186 StomachState`** — hunger, if wanted. It is one float. Decide
   whether Wareborn wants a hunger meter at all; retail had one and it is a
   taste decision, not a restoration one.
5. **Fill out the cooking tree.** 9 recovered nodes with real costs, only 4
   with item rows. `Plain Rice`, `Flour`, `Bread`, `Manta Burger`,
   `Breaded Mushrooms` need items and inputs.

- **Player can newly do:** cook; eat; move faster / jump higher / get drunk;
  **complete retail quest 130 (EatBerries)**.
- **Depends on:** Phase 3 for meat; `resource-economy` Phase 3 for berries.
- **Migration:** no. **SOAK:** buffs are low-rate per-player state — light.
- **Main risk:** the four Cooking recipes currently mislabel `iron` (§4.1), so
  fixing routing without fixing the data ships recipes that eat the wrong
  thing. Do both together.

---

### PHASE 5 — The Beetle, and creature ecology worth watching
1. **Serve the Beetle** — `4325 BeetleVariantState`, prefabs `beetle` and
   `beetleegg`, complete shipped art (3 heads, 20+ clips), `BeetleAging` with
   Young/Old/**Dead** swatches. It is the third renderable animal and the only
   one that walks. Given Phase 3, it can also be the first thing a player hunts
   — and, INFERRED, it is what the wiki calls a Thuntomite.
2. **Eggs and hatching** — `1169 EggLayingState`, `1170 HatchState`,
   `21012 EggHolderState`.
3. **The remaining jellyfish species** — `Seed`, `Flower`, `DesertA`, `DesertB`
   exist as a type today and only the generic prefab is served.

- **Player can newly do:** meet an animal that isn't a fish; hunt it.
- **Depends on:** Phase 3.
- **Migration:** no. **SOAK:** yes — more creatures on the wire. The per-peer
  ceiling is 24 creatures (`IslandFaunaInterestPolicy.DefaultPerPeerCreatures`);
  adding a species does not raise it, but budget allocation changes.
- **Main risk:** the `AgeVisualizer` trap already documented at
  `ComponentsSerializer.cs:3496-3524` — an adult must be sent
  `secondsOld >= secondsTillFullyGrown` or **every animal in the world shrinks
  to a quarter size at once**. Beetles inherit this.

---

### PHASE 6 — Weather
*The best-understood missing system. Do not attempt it before reading
`ComponentAbsencePolicy.cs:55-135` in full.*

**The problem is not "send 1139". It is "create entities that are legitimately
weather cells."** Everything below is PROVED.

- The client turns `1139 WeatherCellState` into a grid cell by **flooring the
  entity's own position onto a 500 m lattice** and keying a dictionary on the
  **Cantor pair** of that cell (`AddWeatherCellCoordsS.cs:28,38-39`; spacing
  `WeatherCellGenesisS.cs:22` = `500f`). Cantor pairing is a bijection, so
  equal ids mean equal cells — never a hash accident.
- Putting 1139 on gameplay entities is **actively destructive**: every entity
  on one ~60 m island lands in the same cell, one wins the map and the rest hit
  a branch that logs but never marks the entity, so they lose again every tick,
  forever. **Measured on a live two-player session: 31,144 errors in 158 s,
  ~197/s, each with a 14-frame stack trace built on the client main thread.**
- Retail laid cells out one apart via `WeatherCellGenesisS`, injective by
  construction. It was an "I am a weather cell" marker. A player is not a
  weather cell.

**The path:**

1. **A dedicated weather-cell entity band.** Cells must be their own entities
   at exact multiples of 500 m, one per tile — alongside the existing disjoint
   bands (`TreeFall.FirstLogEntityId = 2_000_000_000`,
   `IslandFaunaPolicy.FirstFaunaEntityId = 2_100_000_000`, the whale 100 M
   above that).
2. **Remove 1139 from `KnownAbsentComponentIds`** and add a serve branch
   guarded on membership of the weather-cell registry — the same shape the
   fauna branches use (`ComponentsSerializer.cs:3396,3403` guard on
   `Fauna.SpeciesOf(entityId)`, not on an id band). Payload:
   `WeatherCellStateData(float pressure, Vector3f wind)`.
3. **A per-peer streaming service**, shaped like `IslandFaunaService`.
4. **All dynamics server-side.** Four of retail's six weather systems are not
   in the client's FixedUpdate config (`docs/research/ecs_config.json` — the
   whole configured gameplay ECS is seven systems, two of them weather
   bookkeeping). The client can never animate weather itself.
5. **A complete lattice, or none.** `GlobalWeather.GetWeatherAt` samples FOUR
   cells and interpolates; the current no-cell fallback is a *uniform* wind
   `(1,0,-2)`, pressure `0.5` (`GlobalWeather.cs:55-69`). **A partial lattice
   is worse than none** — it puts a visible wind/pressure seam wherever the
   cells stop. This is the single most important design constraint.
6. **Then sails become real.** `SailBehaviour.cs:64`, `SailVisualizer.cs:75`,
   `StormDebris.cs:82`, `WeatherTextureGenerator.cs:200` all route through
   `GetWeatherAt`. This is what upgrades the documented scalar-propulsion
   approximation into actual wind.
7. **Wind walls are nearly free once cells exist.** `1204 WallSegmentState` =
   `{ int wallType, int wallId, Vector3d orientation, float length }`, and **44
   typed segments with real geometry are already imported** and already drawn
   on the admin map. Also available: `1202`/`1203 WindMultiplierSphere/AABox`
   and `5129 WindReceiverState = { Vector3f wind }` for per-entity delivery.

- **Player can newly do:** feel wind; sail with it; see and avoid a wind wall.
- **Depends on:** nothing technically. Deliberately placed after the gameplay
  loop because it is presentation-heavy and risk-heavy.
- **Migration:** no. **SOAK:** **yes, emphatically** — this adds a new class of
  streamed entity across the whole world.
- **Main risks:** (a) re-opening the exact error storm that caused its removal;
  (b) three named UNKNOWNs below.

**UNKNOWN before starting — do not assume:**
1. **Can a weather-cell entity exist with no prefab?** `ClientEntityPrefabs.CanResolve`
   gates AddEntity naming and there is no weather-cell entry in
   `client-entity-prefabs.txt`. Unresolved.
2. **`WeatherCellGenesisS.RemoveExistingWeatherCellEntities()` deletes anything
   that `Contains<WeatherCellState>()`.** Whether that can fire on a live
   client and delete our cells is **unverified**.
3. Whether any client visualiser `[Require]`s `WeatherCellState`.

---

### PHASE 7 — Storms, lightning and the Blight
*Separated from Phase 6 because it is genuinely harder.*

`1269 RadialStormState = { float weight }` is trivial. The obstacle is that
**every shipped consumer is a Blight system whose filter also requires
`BlightLocalComponent`**, which nothing in the client attaches to a Traveller,
island, hull or tree — and two of them additionally require *authority* over
1269, which this server grants to nobody
(`ComponentAbsencePolicy.cs:130-141`). **Serving 1269 alone is unreachable
code.** Also in scope: `1222`–`1227` (lightning generator, attractors,
pockets), `1254` (the storm branch currently suppressed on purpose),
`1225 LightningStrikableState`, `1256 SandStormAffecteePositionalState`.

- **Depends on:** Phase 6. **Migration:** no. **SOAK:** yes.
- **Main risk:** may prove to require a **CLIENT MOD** to attach
  `BlightLocalComponent`, which changes the shipping path. Establish that
  before committing.

---

### PHASE 8 — The world's content layer
*Ruins, wreckage, marauders — the reasons to visit an island.*

1. **Ruins as content.** `IslandLootSpawnerCategory` has four members —
   `Ruin`, `Container`, `Chest`, `Marauder` — and `1237 IslandLootSpawnerState`
   keeps a **separate budget, spawn-time and opened list per category**
   (`lootRuins`/`lootContainers`/`lootChests`). `loot-containers` Phase 3 covers
   ruin piles and the Kioki set; the *scannable* ruin (`8073`, currently
   databank-only in practice) is open.
2. **Wreckage.** `RuinedShipSpawnerPreprocessor` and
   `havenruinedshiprespawner` exist and nothing references them. A wrecked
   hull that can be salvaged reuses `ShipSalvagePolicy` wholesale.
3. **Marauder camps** (`loot-containers` Phase 5) and
   `1265 AtlasCompassChestState = { Option<Coordinates> target, Option<float>
   timeUntilSelfDestruct }` — a chest that points at a place and expires. A
   complete treasure-hunt loop in two fields.
4. **The Codex made worldly.** Phase 0 gives the Codex machinery; this phase
   gives it places to be found.

- **Player can newly do:** explore for a reason.
- **Depends on:** Phase 1 (loot), Phase 3 (if marauders fight back).
- **Migration:** the loot state migration lands here if deferred from Phase 1.
- **SOAK:** yes. **Main risk:** content volume, not technical.

---

### PHASE 9 — The long tail
Ordered within itself by cost, all genuinely optional:

- **Torch/light + a day/night cycle.** These are one feature. A torch with no
  night is a prop; a night with no torch is a complaint. Needs a replicated
  world clock, which does not exist.
- **Turrets** (`1122`, `1371`–`1374`, `4444`, `4445`) — needs Phase 3.
- **Territory control** (`1273`, `1262`, the `territory_control_beacon`, which
  is the single dead `Shipyard`-category recipe) — needs alliances, which are
  live.
- **Ship wiring** (`1213`–`1216`) and **locks** (`1217`, `1218`, `1220`, `1221`).
- **Photo camera and photo book** (`1024`–`1028`) — pure delight, zero
  dependencies.
- **Musical instruments** (`1023`) — items already exist.
- **Melee weapons** — 40 recovered knowledge nodes across four branches with
  no items behind them.
- **Ship-to-ship grapples** — the standing hard frontier; needs temporary
  domain affinity or merging (`docs/roadmap.md:71`).

---

## 6. CHANGES THAT NEED A PATCHER RELEASE

A server deploy and a client release are different shipping paths, and the
2026-08-19 outage is the reminder of what a bad client release costs: two
published manifests carried a connect defect and **every player who patched got
an infinite load** (`HANDOVER.md:179-205`).

**Nothing in Phases 0–5 requires a client-mod change.** Everything there is
server-side seeding, handlers and data.

Candidates that probably do:

| item | phase | why |
|---|---|---|
| `BlightLocalComponent` attachment for storms | 7 | the client never attaches it; a Harmony patch may be the only route |
| Any Harmony reach into a closed-generic ECS system | 6/7 | `AddToIdComponentToEntityMapS\`2` — **unverified whether Harmony can reach it at all** |
| Day/night clock presentation | 9 | if the stock client has no server-driven clock hook |
| Melee hand-item enum | 9 | the enum has no melee entry; adding one is a client change |

**Standing rule already in memory and worth restating here:** any client-mod
change must be followed by a patcher update in the same session, or players
fall behind.

---

## 7. COLLISION MAP — THE FOUR BRANCHES IN FLIGHT

| branch | state at 2026-08-19 | this roadmap's relationship |
|---|---|---|
| `feat/resource-economy` | 7 commits, **Phases 1–3 landed** (quality, per-island metals, fibre+berries). Gates: 3737 tests, soak FLAT. | **Owns** harvest yields, item quality, per-island metals, scrapping, cooking routing, the loom's `1264`, creature mortality and yields. This roadmap references it in Phases 3–5 and never re-plans it. |
| `feat/loot-containers` | 2 commits, **Phase 1 landed** (`ebed3c2`, 30 files). | **Owns** loot placement, streaming, serving, opening, cross-inventory moves, scrap loot tables, marauder camps. Sequenced into Phases 1 and 8. |
| `fix/log-grounding` | **MERGED to main** (`2cc9f02`). | Done. Its output is a **dependency** of `resource-economy` Phase 7a-ii ("the corpse falls — REUSE the log grounding"). |
| `feat/emblem-objects-wired` | 2 commits, 200 emblem objects; the wiring half uncommitted. | No overlap. **One constraint to respect:** the emblem object index is append-only — a live crest stores devices by number and editing the table is unrecoverable. |

**Explicit dependencies this roadmap declares:**

- Phase 3 (weapons) **needs** `resource-economy` Phase 7a-iii, which their plan
  marks **UNSIZED**. This is the one place the two plans must be reconciled
  before either ships.
- Phase 4 (cooking) **is** `resource-economy` Phase 6 plus the food components.
- Phase 1 item 4 **is** `loot-containers` Phases 1–2a.
- Phase 8 item 1 and 3 **are** `loot-containers` Phases 3 and 5.
- Phase 1 item 3 (the deployable activation audit) is **unowned** and was
  surfaced by `resource-economy` §2. It should be claimed.

**File-level collision warning:** `feat/loot-containers` and
`feat/resource-economy` both edit `Game/Inventory/InventoryService.cs`,
`Game/Gathering/WorldResourceActivation.cs` and
`WorldsAdriftRebornGameServer.cs`. Phase 0's §4.1 also touches
`schematicData.json` / `itemData.json`, which `resource-economy` edits. Decide
merge order before starting Phase 0.

---

## 8. CONTENT ALREADY ON DISK AND NOT IN THE GAME

The most useful thing this audit found. **None of this needs inventing — it
needs wiring.** Ranked by leverage.

| asset | what it is | status |
|---|---|---|
| **The lore bank** — `Game/Items/Config/itemData.json`, the `lore` row's `entries` field | **178 titles, 604 pieces of recovered Bossa lore text**, imported 2023-02-13 | **UNUSED.** Zero readers. One handler away (§4.2) |
| **`docs/research/loop/data/quests.json`** | **8 fully decoded retail `QuestData` objects** with Bossa's own titles, steps and resolved condition classes | **UNUSED.** Two unserved components away (§4.3) |
| **`docs/research/loop/data/knowledge-tree.json`** | 20 branches / 228 nodes with real `knowledgeCost` and `parents` — including 7 weapon branches and a 9-node cooking tree | **PARTLY USED.** Loaded and spendable; ~half the branches have no items behind them |
| **`docs/research/world-data/haven/guidlut.json`** | **1,347-entry GUID → prefab-path table**, described in `findings-haven.md:163` as *"reusable for ALL 255 islands; the most valuable artefact here"* | **UNUSED beyond Haven** |
| **`docs/research/world-data/props-949069116.json`** | **956 prop placements** on Shattered Mausoleum with pos/rot/scale. All 124 unique GUIDs verified resolvable against `guidlut.json` (124/124) | **UNUSED.** Zero references anywhere. Together with `guidlut.json` this is a proven, repeatable path to per-island prop and ruin manifests for all 255 islands |
| **24 × `lootruinpile*` + 12 × `lootruinpilekioki*` + chests/containers** | 44 loot prefabs, all client-resolvable today | **UNUSED on main.** `feat/loot-containers` Phase 1 begins using them |
| **`beetle` / `beetleegg` prefabs + `BeetleAging` + `BeetleVariationParams`** | the third renderable animal, complete art, Young/Old/**Dead** swatches | **UNUSED** (Phase 5) |
| **44 weather-wall segments** in `wamap-islands.json` | real typed geometry (Wind Rift, Storm Rift, Typhon, Sand Storm, Ice Storm, World End) | **UNUSED as gameplay**; drawn on the admin map (Phase 6) |
| **`haven-structure-props.txt`** — 253 authored ruin placements | a clearance/exclusion oracle | **Loaded but has no production consumer at all** — `HavenStructures` is referenced only by three test files |
| **`docs/research/gathering/data/salvage_yields.json`** + the 134 `scrapItem-*` rows | tier-keyed `rewards` already shipped in `itemData.json` and already reaching the client | **UNUSED.** `resource-economy` §0.3: scrapping needs **no new wire message, component or client change** |
| **`tutorial-content.json`, `quest-conditions.json`** | extracted tutorial copy and condition definitions | **UNUSED** |

**Method note for anyone extending this:** do not re-decompile. The canonical
component index is `/home/ttanurhan/Games/WAReborn-decompiled/component-map.tsv`
(444 rows), the decompiled sources are `acs/` (2,158 files) and `gencode/`, and
in-repo dumps already exist at `docs/research/loop/data/prefab-component-census.tsv`
(985 rows), `prefab-names.tsv` (354 rows) and `client-entity-prefabs.txt`.

---

## 9. WHAT I COULD NOT ESTABLISH

Stated rather than guessed. Each line says what it would take.

1. **Whether the Codex/quest/lore UI actually renders correctly once fed.**
   The Logbook renders empty today (proved by the NRE fix). Whether a populated
   `knownLore` produces a usable panel needs **one live client session**.
2. **Whether the four `QuestData` assets are baked on the shipped prefab.**
   Needs an asset-bundle inspection or a live client.
3. **Whether a weather-cell entity can exist with no prefab.** Needs a
   headless AddEntity experiment against a real client, or a bundle check for
   a resolvable name.
4. **Whether `WeatherCellGenesisS.RemoveExistingWeatherCellEntities()` can fire
   on a live client** and delete server-authored cells. Needs a live client.
5. **Whether Harmony can patch a closed-generic ECS system**
   (`AddToIdComponentToEntityMapS\`2`). Flagged unverified in two research
   documents. Needs an experiment.
6. **Whether the standalone databank draws and is scannable** when spawned as
   its own entity rather than by the island spawner. Author-flagged as
   live-client-only (`Multiplayer/Databanks.cs:20-26`). The grant path does not
   depend on it, but the visible one does.
7. **Whether the pistol's firing animation plays at all today.** No live-client
   trace exists.
8. **Whether placed lamps and campfires light anything client-side** once
   `1108`/`1012` are seeded. No observation recorded.
9. **Whether the native `1211` tool path and the salvager chop hack contend for
   the left mouse button.** Flagged as **never run** at
   `MirrorSendPolicy.cs:400-411`. Phase 3 makes this urgent.
10. **The exact mapping of "Thuntomite" to the `Beetle` asset.** INFERRED from
    the food chain and the shipped art. It cannot be PROVED — the string does
    not exist in the decompile. If it matters for naming, it is a decision.
11. **Whether `feat/resource-economy`'s three landed phases still pass against
    current `main`.** That branch was cut at `2fc5846`; `main` has moved (it now
    carries the log-grounding merge). Its own gates were green against the older
    base. Nobody has rebased or re-run them. This is a merge-order question, not
    a correctness claim.
12. **Whether `props-949069116.json` + `guidlut.json` really generalise to all
    255 islands.** The GUID resolution is verified 124/124 for one island. That
    it repeats is INFERRED from the lookup table's size (1,347 entries) and
    `findings-haven.md:163`, not demonstrated.
13. **`WAREBORN_BUILD` on production reads `f212e70`** while `HANDOVER.md`
    records the deployed game server as `5a69250`. Probably a stale label on
    the env var rather than a stale binary, but I did not confirm which, and
    the handover explicitly warns that the box is the authority on live config.

---

## 10. THE ONE-PARAGRAPH ANSWER

Wareborn today is a **world with a working extraction and construction loop and
no consequences**. Mining, logging, fuel, atlas shards, knowledge, schematics,
crafting, ship design, ship building, flight, salvage, islands, streaming,
co-presence, gliders, grapples and a live ecology all work. What is missing is
almost entirely one connected cluster: **nothing can be hurt, nothing can die,
nothing can be eaten, and nothing can be found in a box** — plus **weather**,
which was removed on purpose for a measured and excellent reason. Two of the
in-flight branches are already closing the "found in a box" and "can be eaten"
halves. The highest-value unclaimed work is, in order: **fill the Codex** — 604
pieces of recovered Bossa text are on disk, the UI is the Logbook's default tab,
and the client has been asking for them every five seconds this whole time;
**give the player Bossa's own tutorial**, eight decoded quests sitting behind
two unserved components; **make ships cost something**, because right now every
hull in the game costs three birch and two iron; and then **build the death
loop** that weapons, hunting, cooking and half the recovered quest list all
depend on. Three of those four require no new system at all — only the wiring
of content this project already has.

**Added 2026-08-19:** §11 audits the 37 assembly-bench ship components against
that same standard and finds the construction half in better shape than expected
— 36 of 37 render — but narrower than retail in one specific, fixable way: an
instrument can be mounted on a flat deck and nothing else.

---

## 11. SHIP COMPONENTS — THE ASSEMBLY-BENCH AUDIT

**Added:** 2026-08-19, branch `docs/ship-components`, cut from `main`.
**Why:** three symptoms reported from live play — *"some of them don't show up"*,
*"the ones that show up that should be interactable aren't"*, and *"the altimeter
can only go on the floor; I put a fence down and I want to place it on that"*.

This section is the audit those three demanded. It is **not** the same subject as
§4.6 / §5 Phase 1 item 3, and the difference matters: that item is the
**deployable** activation audit (`Multiplayer/Placement/Deployables.cs`, 17
hand-placed ground props, 16 of which seed only a transform). **Ship components
are a different table, a different spawn path and a different seed contract** —
`Multiplayer/Ship/LoosePartCatalogue.cs`, 37 rows, spawned by
`Game/Crafting/LoosePartSpawner.cs`. They are *not* under-seeded. The headline of
this audit is that the ship-part table is in far better shape than the deployable
one, and that its three symptoms have three genuinely different causes.

### 11.1 The path, in one line

`1003 StartCrafting` at a placed Assembly Station
(`Handlers/PlayerCraftingInteractionState_Handler.cs:124,352-560`) →
`LoosePartCatalogue.ForSchematic` (`:370`) →
prefab realizability gate (`:389-398`, `StationCraftOutputGate`) →
atomic consume (`:445`) → `LoosePartSpawner.Spawn` (`:482-488`) →
AssetLoadRequest + AddEntity + **one** `SendAddComponentOp(...,
failOnComponentInitError: true)` (`LoosePartSpawner.cs:404-433`) →
the player lifts it with the scanner and mounts it with `1070 PlacePart`
(`Game/PartMountService.cs`).

**The count is closed and triple-pinned at 37:** 37 rows in `LoosePartCatalogue`,
exactly 37 `"category": "CraftingStation"` recipes in `schematicData.json`, and
`Multiplayer.Tests/Ship/LoosePartTests.cs:31-40` pins the same 37 names. 36
distinct prefabs (`powerGenerator` and `powerGenerator01` share
`PowerGenerator01`). **PROVED.**

Three checks that each killed a plausible theory before it reached this table:

- **Every one of the 36 prefab names resolves** against the real client
  entity-prefab set (`docs/research/loop/data/client-entity-prefabs.txt`, 359
  names). Not one is a dead bundle string. **PROVED.**
- **Every seeded component id has a `ComponentsSerializer` branch** — all of
  `190602, 190601, 1016, 1099, 1013, 1120, 8066, 1246` plus `1107, 1108, 1118,
  1303, 1518, 12281, 1236`. So the all-or-nothing batch does **not** drop, and no
  ship component is sitting at the world origin. **PROVED.** (This is the trap
  §4.4 warns about; it does not currently bite this table.)
- **All 37 recipes are reachable.** 36 are granted by a knowledge node that
  really exists in `knowledge-tree.json` and `lamp` is a starter
  (`StarterSchematics.cs:23-34`). Zero unreachable rows, so "doesn't show up"
  is not the torch problem. **PROVED.**

### 11.2 THE TABLE — all 37, what the client needs, what we serve

Common to **every** row, from `LoosePartDefinition.BaseShipPartComponents:116-121`:
`190602, 190601, 1016, 1099, 1013, 1120, 8066, 1246`. The first six are exactly
`ShipPartVisualizer`'s `[Require]` set (`acs/Assets.Scripts.Visualisers.Ship/
ShipPartVisualizer.cs:22-38`), which is why **every row renders and lifts** — the
"extra" column below is what is appended on top.

`1210 InteractiveState` is **not** seeded; it is served on demand when the client
asks for it (`ComponentsSerializer.cs:719-989`), which is sufficient because the
prefab's own interest declares it.

| # | schematicId | prefab | attach → surface | client `[Require]` beyond the base | we add | appears? | interactable? | placeable where? |
|---|---|---|---|---|---|---|---|---|
| 1 | `helm` | Helm01 | deck → ShipDeck | `HelmVisualizer` → **1111** (on the *ship*, not the part) | — | **yes** | **yes** — `Man`, served by the dedicated isHelm branch (`:888-902`) | flat deck only |
| 2 | `sail` | Sail01 | deck → ShipDeck | `SailVisualizer` → **1303** | 1303 | **yes** | **yes** — `Activate` (furl) | flat deck only |
| 3 | `deck` | Deck01 | deck → ShipDeck | `ShipDeckVisualizer` → **1518 + 1099** | 1518 | **yes** | no (retail: structure) | flat deck only |
| 4 | `proceduralEngineDefault` | ModularEngine | engine → ShipSide | `ModularShipPartVisualizer` → **12281 + 1099** (builds the mesh); `EngineVisualizer` → 1116, 1235, 1252, 1251 | 12281 | **yes** | no (retail: driven by the helm) | hull side |
| 5 | `proceduralWingDefault` | ModularWing | wing → ShipSide | same, plus `WingVisualizer` → **1124** | 12281 | **yes** | no (retail: driven by the helm) | hull side |
| 6 | `atlasSkyCore` | CoreMain | deck → ShipDeck | `ShipCoreVisualizer` → **1236 + 190602** | 1236 | **yes** | **no — should be.** Retail bakes `Activate` (`ShipCorePreprocessor`); we serve `None` because the shipped client has no consumer for the resulting interact | flat deck only |
| 7–14 | the 8 `skyCore*` modules | Core* | coreModule → CoreModule | `ShipCoreModuleVisualizer` → **1236 + 190602** | 1236 | **yes** | no (retail: passive animators) | **a socket on CoreMain** — the only true socket path in the game |
| 15 | `smallPanel` | Panel01 | side → ShipSide | `ShipPanelVisualizer` → **1118**; variation → 1246 | 1118 | **yes** | no | hull side |
| 16 | `mediumPanel` | Panel02 | side → ShipSide | same | 1118 | **yes** | no | hull side |
| 17 | `largePanel` | Panel03 | side → ShipSide | same | 1118 | **yes** | no | hull side |
| 18 | `window` | Window01 | side → ShipSide | same | 1118 | **NO — see §11.3. Fixed on this branch, unverified in game** | no | hull side |
| 19 | `stairs` | Stairs1 | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 20 | `railing` | RailingStraight | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 21 | `railingCorner` | RailingCorner | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 22 | `trunk` | ContainerSmall | deck → ShipDeck | `InWorldInventoryVisualiser` → **1210 + 1081**; `IsTooDamagedToWorkVisualizer` → **1236**; baked verb **Inventory** | — | **yes** | **NO — should be. §11.4** | flat deck only |
| 23 | `mountedBox` | ContainerMount | deck → ShipDeck | same | — | **yes** | **NO — should be. §11.4** | flat deck only |
| 24 | `storageContainer` | ContainerMedium | deck → ShipDeck | same | — | **yes** | **NO — should be. §11.4** | flat deck only |
| 25 | `shippingContainer` | ContainerLarge | deck → ShipDeck | same | — | **yes** | **NO — should be. §11.4** | flat deck only |
| 26 | `barrel` | Barrel01 | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 27 | `cupboard` | Cupboard | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 28 | `horn` | Horn01 | deck → ShipDeck | `HornVisualizer` → **1107** | 1107 | **yes** | **yes** — `Activate` (honk) | flat deck only |
| 29 | `lamp` | Lamp01 | deck → ShipDeck | `LampVisualizer` → **1108 + 1236 + 1099** | 1108, 1236 | **yes** | **yes** — `Activate` (switch) | flat deck only |
| 30 | `altimeter` | Altimeter | deck → ShipDeck | `AltimeterVisualiser` → **1236** | 1236 | **yes** | no — **correct**, retail made it a local readout | flat deck only |
| 31 | `fuelGauge` | FuelGauge | deck → ShipDeck | `FuelGaugeVisualizer` → **1105 FuelGaugeState** | **1236 — the wrong id. §11.5** | **yes, but the needle is dead** | no | flat deck only |
| 32 | `headingIndicator` | HeadingIndicator | deck → ShipDeck | `HeadingIndicatorVisualiser` → **1236** | 1236 | **yes** | no — correct | flat deck only |
| 33 | `artificialHorizon` | ArtificialHorizon | deck → ShipDeck | `ArtificialHorizonVisualiser` → **1236** | 1236 | **yes** | no — correct | flat deck only |
| 34 | `airspeedIndicator` | AirspeedIndicator | deck → ShipDeck | `AirspeedIndicatorVisualiser` → **1236** | 1236 | **yes** | no — correct | flat deck only |
| 35–36 | `powerGenerator`, `powerGenerator01` | PowerGenerator01 | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 37 | `personalReviver` | Respawner01 | deck → ShipDeck | `RespawnerVisualizer` → **1094 + 8066** | — | **yes** (the prop; `ShipPartVisualizer` renders it) | **NO — should be. §11.4** | flat deck only |

**The headline numbers.**

- **36 of 37 appear.** One does not: the **Window**. That is a mesh-selection
  failure, not a missing seed, and it is fixed on this branch.
- **4 of 37 are interactable today** — helm (`Man`), sail, lamp, horn
  (`Activate`). **6 more should be and are not**: four storage containers, the
  personal reviver, and the sky core. The other 27 are correctly inert; retail's
  preprocessors add no `InteractiveObjectVisualizer` to them at all. **PROVED**
  part by part in `Multiplayer/Ship/PartInteractionPolicy.cs:27-82`, which is
  already the written audit of "what did retail let you do with this part".
- **23 of 37 can be placed only on a flat deck.** 4 go on the hull side, 2 on
  engine/wing side mounts, 8 into a sky-core socket. **Not one row can be
  mounted on another placed component.** §11.6.

### 11.3 Symptom 1 — the Window is invisible, and it is a MESH problem

**PROVED, from three independent sources that agree.**

The Window spawns correctly. Its seed batch lands, `ShipPartVisualizer` enables,
`1118` is served. Then the client throws inside `OnEnable` and the part ends up
with no geometry at all:

```
No appropriate mesh found for requested ship panel size!
ArgumentException: The Object you want to instantiate is null.
  UnityEngine.Object.Instantiate[Mesh] (UnityEngine.Mesh original)
  ShipPanel.InitializeMesh ()
  ShipPanel.Init (...)
  Assets.Scripts.Visualisers.Ship.ShipPanelVisualizer.OnEnable ()
```

— the maintainer's own `BepInEx/LogOutput.log`, twice, against a live world state
holding **exactly two loose `window` parts**.

The chain, decompiled end to end:

1. `ShipPanel.Init` (`acs/ShipPanel.cs:84-121`) resolves the panel's material as
   `MaterialDefinitionFromName(materialsUsed[0].rawMaterial.materialTypeId) ??
   MaterialDefinitionFromName(_panelMaterial)` — **our seeded `1099` slot 0 wins
   over the prefab's own default.**
2. `PanelArt.MixPanel` (`acs/PanelArt.cs:92-176`) picks the mesh array by
   (size × window × wood/metal), finds it **empty**, logs the error, and returns
   a `PanelArtDefinition` whose `panelFilter` holds no mesh — **non-null**, so
   `Init`'s own null guard does not fire.
3. `ShipPanel.InitializeMesh` (`:341-352`) then calls `Instantiate` on that null
   mesh and throws out of `OnEnable`.
4. The twelve mesh arrays, **read directly out of the shipped client**
   (`ShipPanelDefinitions`, `level0` path_id 1515):

   | | 1×1 | 1×2 | 2×2 |
   |---|---|---|---|
   | metal panel | 5 | 4 | 4 |
   | **metal window** | **4** | **0** | **0** |
   | wood panel | 2 | 3 | 2 |
   | **wood window** | **0** | **0** | **0** |

   `metalWindowPanelMeshes1X1` is the **only** window mesh set in the game. A
   wooden window has no mesh at any size.
5. Window01's own `ShipPanel` component is `HasWindow=true`,
   `_panelSize=onebyone`, `_panelMaterial="iron"` — authored to be exactly the
   one window that exists. Our uniform Wood seed overrode it.

**Why we were seeding Wood at all, and why that stays.** `ComponentsSerializer`'s
`1099` branch writes eight slots of the deck's Wood material onto *every* loose
part. That was the **helm-freeze fix**: `PartGraphicsVariationByMaterial`'s
prefab getter is an unguarded `OriginalMaterials[_materialIndex]` index, and an
empty list pegged the client main thread with an exception loop. The choice is
right for the other 36 rows. Only the Window's art has no wooden variant.

**Fixed on this branch** (`5ca430d`): the material becomes a per-part lookup
(`Multiplayer/Ship/LoosePartSeedMaterial.cs`) keyed on `itemType`, so the two
windows already lying in the world come back fixed with **no persistence
migration and no new record field**. The category may only ever be `"Wood"` or
`"Metal"` — `PartGraphicsVariationByMaterial.GetPrefabFromMaterial` throws on
anything else — and a test pins that for all 37 rows. Server-only; no client
change, no new component, no change to send cadence or payload shape.

**A generalisation worth carrying forward:** this is a *fourth* failure shape,
distinct from a missing seed. **A component can be present, correctly typed and
still carry a value the client's art cannot render.** Nothing logs it
server-side. The only tell is a Unity error in the player's own log.

### 11.4 Symptom 2 — the inert ones, and this IS the loom's defect

Six rows should respond to `E` and do not. Two of them fail for the same reason
the loom and the loot chest do, and it is worth naming the shape precisely,
because it has now bitten this repo four times:

> **A Unity visualiser does not enable until EVERY `[Require]` resolves, and
> `InteractiveObjectVisualizer` caches `Interactions.FirstOrDefault(i => i.verb
> == Verb)` ONCE in `OnEnable`. So there are TWO independent ways to produce a
> prop that is visible, correct-looking and completely dead — an unsatisfied
> requirement, and a served verb that does not match the prefab's baked one.
> Neither logs anything.**

| row | blocker 1 (unsatisfied `[Require]`) | blocker 2 (verb mismatch) |
|---|---|---|
| `trunk`, `mountedBox`, `storageContainer`, `shippingContainer` | `InWorldInventoryVisualiser` needs **1081 + 1210**; we serve 1210 and never 1081. `IsTooDamagedToWorkVisualizer` needs **1236**, also unseeded — and the interact gate itself checks `verb == Inventory && !IsTooDamagedToWork` | the prefab's baked verb is **Inventory** (`ShipContainerPreprocessor.SetVerb`); we serve the generic **PickUp** entry (`ComponentsSerializer.cs:931-938`), so the cache lookup finds nothing, radius is 0, and **no prompt can ever appear** |
| `personalReviver` | `RespawnerVisualizer` needs **1094 + 8066**; we seed 8066 only | baked verb is **Activate**; we serve `None` deliberately, because a prompt without a respawn flow would be a lie |
| `atlasSkyCore` | none — `ShipCoreVisualizer` is satisfied | baked verb is **Activate**; we serve `None`. Retail's handler was GSim-side and the shipped client has no consumer, so this one is arguably *correct* until flight/lift wants it |

**This is the same class as `1264`/`1081+1210`, and the current code already
knows it** — the comment at `ComponentsSerializer.cs:776-780` says so in as many
words, and `PartInteractionPolicy` refuses to advertise a verb it cannot honour
precisely so that a prompt is never a lie. **That discipline is right and should
not be relaxed.** The containers are not blocked on new machinery; they are
blocked on the *same* `1081` serve that `feat/loot-containers` is building. When
that lands, four ship containers come alive for the cost of a verb branch.

**The `InventoryService.ForEntity` trap applies here and is worth restating:** it
falls back to `InventoryWire.DefaultModel`, the **player starter kit**, and
`Bind` runs its factory once. Serving `1081` on a ship trunk without giving it a
specific model hands that trunk a permanent inventory full of gauntlets.

### 11.5 The fuel gauge is gated on the wrong component

Four of the five instruments — altimeter, airspeed, artificial horizon, heading
indicator — live in `acs/Assets.Scripts.Visualisers.ShipParts/` and each
`[Require]`s **1236 alone**, reading altitude and heading off
`GetComponentInParent<Rigidbody>()` rather than off SpatialOS. We seed 1236.
They work.

The fuel gauge is not one of them. `FuelGaugeVisualizer` lives in
`acs/Assets.Scripts.Visualisers.Ship/` and `[Require]`s **`1105
FuelGaugeState`** — and `1105` has **zero server references** (§2.1 already
records it as unserved alongside `1104` and `1106`). The catalogue seeds it 1236,
which nothing on that prefab reads. **So the Fuel Gauge is craftable, placeable,
visible and its needle can never move.** RECOVERED from the decompile; the
catalogue's own comment groups all five instruments together, which is where the
mistake entered.

It is also the one instrument whose fix is blocked on something real: nothing in
this server burns fuel (§2.1), so a served `1105` would read zero forever. Wire
it with combustion, not before.

### 11.6 Symptom 3 — placement, and what retail actually did

**Retail's rule, PROVED.** There is one gate, and it is a **tagged layer-mask
raycast**. `PlacementPreview.cs:564` accepts a hit only if
`go.IsInLayerMask(mask) && (string.IsNullOrEmpty(tag) || go.CompareTag(tag))`.
The mask and the tag both come from one `[Flags]` value, `PlacementLocationType`:

| surface | Unity layers (`GetMask`, `:441-470`) | tag (`GetTag`, `:424-439`) |
|---|---|---|
| `Terrain` | Terrain | — |
| `ShipSide` | ShipAttachment + ShipAttachmentSolid | `"ShipSide"` |
| `ShipDeck` | ShipAttachmentSolid | **`"ShipDeck"`** |
| `Entity` | **Default + Terrain + Interactive** | — |
| `ShipSurfaces` | **Default + Terrain + Interactive** | — |
| `DeckGrid` | ShipAttachment | `"ShipDeck"` |
| `CoreModule` | all, then re-resolved to a named socket | — |
| `All` | all of the above | **explicitly empty** |

`Layers.Environment = Default | Terrain | Interactive` (`acs/Layers.cs:189`).

Three findings follow, and they answer the question directly.

1. **The surface was per-item DATA, not code.** For a ship part it is
   `1120 ShipPartState.attachmentType`, a string, parsed by
   `BuilderVisualizer.GetAttachmentType:71-86` and mapped by
   `ShipPartPlacement.DeterminePlacementType:235-269`. For a hand-deployable it is
   `ItemPlacementAgentState.placingType`, also a string, and
   `ItemPlacingBehaviour.cs:142-151` parses it as a **comma-separated list of
   enum names**. Both are server refdata. **The retail values themselves are
   LOST** — no item table ships in the client, and our `itemData.json` has no
   placement field. So we **cannot PROVE** what string retail gave the altimeter.
2. **There is NO per-component placement rule anywhere in the shipped client.**
   Across 3,540 decompiled files the only per-prefab placement hooks are one
   `IPlacementValidator` (the helm's "must be upright") and five
   `PlacementSpecialRule`s (all territory/alliance/sky-view). `Altimeter` appears
   in exactly one file, its needle visualiser, which contains no placement code.
   Instruments carry only `ShipInstrument.cs:9-11`, which is an *overlap
   exemption*, not a surface rule. **PROVED.** So "the altimeter can only go on
   the floor" is *entirely* a consequence of the string we author.
3. **Mounting on an already-placed object was retail's DEFAULT.**
   `PlacementPreview.UpdateTargetObject:472-486` walks the hit up to its owning
   entity and parents the placement to it; `ItemPlacingBehaviour.cs:431-435`
   sends the parent's `EntityId` and a parent-local point. Retail's *opt-out* is
   an explicit per-prefab marker component, `BlockItemPlacement`, checked at
   `ItemPlacingBehaviour.cs:289-294`. **The existence of an opt-out marker is the
   proof that placing on placed objects was the behaviour that needed
   suppressing.** PROVED — this is the strongest evidence in the section, and it
   is on the maintainer's side of the argument.

**Our rule.** Every instrument, decoration, railing, container and the helm is
authored `"deck"` (`LoosePartCatalogue.cs`), and
`PartMountSurfaces.NormalizeForBuiltShip:63-66` rewrites any legacy
`"shipSurfaces"` to `"deck"` **globally**. The reason is written down and is a
good one: our reconstructed hull exposes no Environment-layer skin, so a
`shipSurfaces` part had nothing to land on but one incidental collider — the
"helm only mounts in ONE spot" symptom. Deployables are worse: every one of the
17 rows gets the hardcoded `PlacementService.cs:56` `const string PlacementType =
"Terrain"`, so a lamp or campfire **cannot be placed on a ship at all**, and
`ItemPlacingBehaviour.cs:185` additionally rejects any slope past 36.9°.

**The gap, and the trap in closing it.** The naive fix — author instruments
`"deck,Entity"` or set `ShipDeck | Entity` — **does not work and is worse than
today**, for two reasons read straight off the client:

- `GetTag` applies **one** tag to **every** hit. With `ShipDeck` in the flags it
  returns `"ShipDeck"`, so an Environment-layer railing would be raycast and then
  rejected on its tag.
- Every behaviour switch is `==` on the whole flag value, not `&`:
  `PlacingOnDeck`, `PlacingOnSurface`, `NeedToBeOnShip`, `PlacingDeck`,
  `PlacingCoreModule` (`PlacementPreview.cs:122-130`). Any combination other than
  a single flag or `All` **silently drops the deck flatness rule, the
  ship-aligned base rotation and the ship requirement**.

`PlacementLocationType.All` is the only multi-surface value the client handles
coherently — mask everything, tag empty. That makes the honest options exactly
two, and they should be planned as two:

- **Server-only, no patcher:** author the instruments `"shipSurfaces"` again.
  Costs the deck (Environment does not include ShipAttachmentSolid) and buys
  every other surface. A straight trade, not a win.
- **Client mod + patcher release:** a Harmony patch that widens
  `ValidSurfaceTypes` for instrument-class parts. Our mod **already patches this
  exact class** — `Patching/Ship/ShipSidePanelExterior_Patch.cs` prefixes
  `PlacementPreview.PositionOnShip` and reads `__instance.ValidSurfaceTypes` — so
  this is in-pattern, not new machinery. It is the only option that gives
  *deck **and** fence*.

**UNKNOWN, and it decides which option is worth doing:** whether a mounted
`RailingStraight` actually presents a collider on Default/Terrain/Interactive.
That is prefab asset data — no code anywhere sets a ship part's layer except
`ModularWing.cs:76`. Haven's scene props do sit on `Default` (layer 0), which is
inside `Layers.Environment`, so it is **plausible**; it is not proved. **How to
settle it:** enumerate the `entityprefabs/railingstraight_unityclient` prefab's
colliders and read `m_Layer`/`m_Tag`. One asset read, no live client. Do that
**before** committing to a patcher release, because if railings are on
ShipAttachmentSolid without the `"ShipDeck"` tag, neither option above helps and
the answer is a third one.

### 11.7 So — one defect, or three?

**Three, and that is the useful answer.** The tempting conclusion after the loom
was that everything inert is one under-seeding bug. It is not:

1. **The Window** is a *value* bug, not a *presence* bug — every component it
   needs is served; one of them names art that does not exist. **Fixed here.**
2. **The containers and reviver** are the loom's defect exactly — partial
   `[Require]` sets plus a verb mismatch. They ride on
   `feat/loot-containers`' `1081` work and should not be planned separately.
3. **Placement** is not a defect at all. It is a deliberate, documented,
   correctly-reasoned narrowing (`"shipSurfaces"` → `"deck"`) that solved a real
   bug and created this one. Reversing it is a **design decision**, and the data
   retail used is unrecoverable.

The one thing that *is* general, and that §5 Phase 1 item 3 should adopt: **audit
by comparing the client's `[Require]` set to the served set, per prefab, and then
separately check the baked verb.** Both halves. The tooling already exists —
`docs/research/loop/data/prefab_requires.py` does the first half against
`component-map.tsv`; the census it reads covers only 7 ship prefabs today and
should be widened.

### 11.8 The phased plan

Same contract as §5: what it delivers · what a player can newly DO ·
dependencies · schema migration · networked state (soak gate) · main risk.

---

#### PHASE SC0 — The Window renders *(done on `docs/ship-components`, unmerged)*

- **Delivers:** `5ca430d`. Per-part `1099` material; the Window seeds iron.
- **Player can newly do:** craft a Window and see it. Retroactive — the two
  windows already lying in the world come back visible.
- **Depends on:** nothing.
- **Migration:** **no** — deliberately keyed on `itemType` so no `LoosePartRecord`
  field is added and old saves fix themselves.
- **SOAK:** no. `1099` was already seeded on every loose part; same eight slots,
  same cadence, same payload shape. **CLIENT MOD:** no.
- **Main risk:** low, and named: if `Window01` has no baked `_metalPrefab`,
  `PartGraphicsVariationByMaterial` returns null for the metal branch and the
  window breaks a different way. Mitigated by the prefab naming `"iron"` as its
  own default. **Only a live craft settles it.**

---

#### PHASE SC1 — The fuel gauge stops lying, and the audit tool grows up

- **Delivers:** (a) re-point `fuelGauge` at `1105 FuelGaugeState`, or —
  preferably — **mark it explicitly dormant** until something burns fuel, so the
  catalogue stops implying it works; (b) widen the prefab `[Require]` census from
  7 prefabs to all 36 and commit the generated table, so the next agent does not
  re-derive §11.2; (c) fold the **verb** check into the same tool, because
  `[Require]` coverage alone would have passed all four containers.
- **Player can newly do:** nothing directly. This is the cheap step that stops
  the next three phases being guesswork.
- **Depends on:** nothing.
- **Migration:** no. **SOAK:** no. **CLIENT MOD:** no.
- **Main risk:** serving `1105` with a hardcoded zero is worse than not serving
  it — a gauge pinned at empty reads as a bug, an unlit gauge reads as unfinished.
  Prefer (b) over a fake value.

---

#### PHASE SC2 — Ship storage opens

- **Delivers:** the four container rows become real chests: seed **1081** (with a
  container-specific model, **never** `InventoryWire.DefaultModel`) and **1236**,
  and serve the **Inventory** verb instead of the generic PickUp entry.
- **Player can newly do:** bolt a trunk to their ship and put things in it —
  the first ship-side storage in the game.
- **Depends on:** **`feat/loot-containers`.** Their `1081` serve, `inUseBy`
  handshake and `event_interact` echo are the same machinery, and doing this
  first would duplicate it. Sequence this immediately behind §5 Phase 1 item 4.
- **Migration:** **yes, probably** — a ship container's contents are per-world
  state and belong beside `loot_container_state`. Their plan's warning applies:
  ship 2a first, defer the migration, and remember that a split deploy has
  already destroyed player progression once.
- **SOAK:** **yes.** A per-container `1081` that changes on every item move is
  new relayed traffic on an entity that rides a moving ship.
- **Main risk:** the `DefaultModel` fallback. A container served `1081` without
  its own model gets a permanent inventory of gauntlets, and `Bind` runs its
  factory once, so it does not self-correct.

---

#### PHASE SC3 — Instruments and decorations mount where the player wants them

- **Delivers:** the answer to the actual complaint. **Two steps, in order:**
  1. **Settle the UNKNOWN in §11.6** — read the collider layer/tag off the
     shipped `RailingStraight`, `Panel02` and `Deck01` entity prefabs. One asset
     read. *Do not skip this;* it decides whether step 2 is worth building.
  2. If railings are on an Environment layer: a client-mod Harmony patch widening
     `ValidSurfaceTypes` to `PlacementLocationType.All` for the instrument and
     decoration classes only — the one value whose tag is empty and whose
     behaviour switches the client handles coherently. Keep the deck flatness
     rule by re-applying it in the patch rather than inheriting it, since `All`
     drops it.
- **Player can newly do:** mount an altimeter on a fence, a wall, a railing or a
  hull side — not only on the floor.
- **Depends on:** nothing, but SC1's table makes it much safer.
- **Migration:** **no** — `attachmentType` is already persisted per part and
  `PartMountSurfaces.NormalizeForBuiltShip` already rewrites legacy records.
- **SOAK:** no. Placement is a client-side preview; the server sees the same
  `1070 PlacePart` commit it sees today, and `PartMountService.cs:146-167`
  **already accepts a mount whose parent is another mounted part**.
- **Main risk:** **this needs a CLIENT MOD, and therefore a patcher release** —
  a different shipping path from a server deploy, and every player must update
  before it does anything. Second risk: `All` silently discards
  `PlacingOnDeck`/`NeedToBeOnShip`, so a careless patch lets a player mount an
  altimeter on the *terrain*. The flatness and on-ship checks must be
  re-asserted, not assumed.
- **Label:** whatever surface set we choose is **WAREBORN TUNING**. Retail's
  own strings are unrecoverable and this document must not pretend otherwise.

---

#### PHASE SC4 — Deployables get a surface of their own *(the ground-prop half)*

- **Delivers:** move `PlacementService.cs:56`'s `const string PlacementType =
  "Terrain"` onto `DeployableDef` as a per-row field. The client already parses a
  comma list (`ItemPlacingBehaviour.cs:145`) — but per §11.6 the **only safe
  multi-surface value is the literal `"All"`**; `"Terrain,ShipDeck"` would demand
  the `"ShipDeck"` tag on terrain too and break ground placement.
- **Player can newly do:** put a campfire, a lamp or a chest **on their ship**,
  and on other placed objects.
- **Depends on:** §5 Phase 1 item 3 (the deployable activation audit) — there is
  no point letting a player mount a loom on a deck while the loom is still inert.
- **Migration:** no. **SOAK:** no. **CLIENT MOD:** no — this one is server-side
  refdata, which is why it is cheap.
- **Main risk:** the server does **no** surface validation at all
  (`PlacementPolicy.cs:56-62` says so deliberately, and
  `ItemPlacingState_Handler.cs:102-109` accepts and discards the parent), so
  widening the client's permission widens it for a modified client too. That is
  acceptable for props and would not be for anything with reach or damage.

---

### 11.9 What only a live craft can settle

Stated rather than guessed, in the style of §9.

1. **Whether the Window now draws.** Nothing headless renders a `ShipPanel`. The
   fix rests on the decompile, the asset bytes and the client log. Craft one; the
   tell is the **absence** of `No appropriate mesh found for requested ship panel
   size!` in `BepInEx/LogOutput.log`.
2. **Which parts the maintainer meant by "some of them don't show up."** This
   audit proves exactly one (the Window) and proves the other 36 render. If a
   *different* row is invisible in game, this table is wrong about it and the
   client log will say which — every failure of this class logs a Unity error.
3. **Whether a mounted railing exposes a mountable collider.** §11.6. An asset
   read settles it without a live client, and SC3 should not start until it is
   done.
4. **Whether the sky-core module sockets restore correctly** on every module. The
   socket components are stripped from every shipped prefab and re-added by
   `Patching/SpatialOS/SkyCoreSocketRestore.cs` at template-compile time. Eight
   modules; only the chain as a whole has been live-confirmed.
5. **Whether `PartGraphicsVariationByMaterial` on `Window01` has a baked
   `_metalPrefab`.** SC0's named risk.

---

## 12. FUEL — how it worked, and how it works here

**Owner: `feat/ship-fuel`.** Written from the decompile
(`/home/ttanurhan/Games/WAReborn-decompiled`) and the shipped client asset
census, because before this section nobody on this project knew. Every claim
below carries a provenance label. The wiki is the **weakest** source here and is
used only where it is the sole survivor; where it disagrees with the decompile,
the decompile wins.

### 12.1 The answer, plainly: how fuelling worked in retail

**PROVED, end to end, from the client:**

1. **Fuel grew on islands as pods.** `IslandProxyVisualizer.cs:160-175` asks the
   island for a `GenerateFuelDepositSpawnRequest()` and spawns a fabric entity
   literally named **`"Egg"`** at the deposit location. `EggPreprocessor.cs`
   makes it a `RawMaterialBreakOnImpactVisualizer` on the client and a
   `FuelPod` (kinematic while lodged) on the worker.
2. **You salvaged them with the gauntlet**, exactly like a metal node, and got
   the raw material `"fuel"` — one of the three raw materials in the game
   (`InventoryItemManager.cs:18`: `{ "Metal", "Wood", "Fuel" }`). Recovered
   yield: 3 shots, 8 + 8 + 9 = **25 fuel per canister** (WIKI, already encoded
   verbatim in `FuelCanisterYield`).
3. **A ship carried one or more FUEL TANK entities.** Each held
   `1106 FuelTankState { capacity, fuel, subtanks }`. `subtanks` is an int, so a
   tank was **expandable** — you bolted sub-tanks on to raise capacity. The ship
   root aggregated them: `AccumulatedData.field5_fuel_tanks` is a
   `Map<EntityId, FuelData{capacity, fuel}>` — total ship fuel is the sum over
   its tanks.
4. **You refuelled by walking up to the tank and holding E.**
   `ShipFuelTankPreprocessor.ExportProcess` adds
   `InteractiveObjectVisualizer` with **`InteractVerb.Activate`** — the same
   generic verb as the lamp and the sail. **There is no `Refuel` verb**; the
   `InteractVerb` enum is `{ Default, Activate, PickUp, Man, Inventory, Craft,
   Harvest, Forced, Design, ReclaimShip, ShipBoost }` and that is all of it.
   The client sends only `(targetEntityId, InteractVerb.Activate)` — **no item
   reference at all** (`InteractAgentObserver.IssueInteraction`). The "you must
   be holding fuel" rule rode on `InteractionEntry.activatedByItem`, a string
   the server puts on `1210 InteractiveState` and which **the shipped client
   never reads** (zero non-gencode references). So the entire refuel decision —
   what it costs, how much moves, whether you are allowed — lived on the
   server. The player's verb is *hold E on the tank*, and nothing more.
5. **Engines were the consumers.** Each engine carried
   `1104 FuelConsumerState { fuelTankId: EntityId, attached: bool }` — an engine
   was **bound to one specific tank entity by id**, not to a global ship pool,
   and `attached` said whether that binding was live. Burn rate rode on
   `1116 ShipEngineState.field4_consumption` (per engine) and
   `1113 ShipControlState.field4_fuel_consumption` (per ship). Consumption was
   **continuous and throttle-driven**, not per-action: `ShipEngineState` carries
   `throttle`, `power`, `spinup`, `currentPercentSpin` and `consumption` as
   separate live floats, and the client scales its engine audio by
   `4f * consumption * throttle` (`EngineVisualizer.GetInefficiency` →
   `UpdateAudio`'s `EngineLoad`/`EngineDamage` Wwise params). A thirstier
   engine literally sounded more laboured.
6. **How thirsty an engine was, was a CRAFTING STAT.** `fuelEfficiency`
   (`SchematicData.cs`, display name "Fuel Efficiency", cipher colour Yellow) is
   rolled by an engine's **Mechanical Internals** and **Propeller** slots. So
   fuel economy was something you crafted for.
7. **The gauge was a separate instrument with its own component.**
   `1105 FuelGaugeState { capacity, fuel }` on the gauge part — the server
   aggregated the tanks and pushed the totals to each gauge. The gauge is
   read-only: **none of `1104`/`1105`/`1106` has a single event or command.**
   Fuel was pure server-authoritative state; there was nothing for a client to
   ask for.
8. **AI ships cheated.** `1067 RefuelShip
   { refuel_interval_seconds, refuel_amount }` lives in
   `Bossa.Travellers.Player.Ai` — NPC ships topped themselves up on a timer.
   Not a player mechanic.

**What happened at EMPTY. PROVED (by absence, three ways):** there is **no**
`fuel == 0` branch anywhere in the client. No warning light, no sound, no UI
message, no localization key — `LocalizationSchema.cs` has zero fuel keys, and
the strings `"Out of fuel"`, `"Low fuel"`, `"Refuel"`, `"Fuel Tank"` do not
exist in the binary. The needle simply pins at empty and the odometer reads
zero. And the client cannot make the ship fall on its own: it **replays**
server-supplied motion (1130) and never simulates it. So running dry is
**a resource sink, not a death trap** — the engines stop pushing and the ship
drifts to a halt. It does not lose altitude. Lift in Worlds Adrift comes from
the sky core (`ShipLiftState 1258`), which fuel never touched.

**RECOVERED but not reproducible: the fuel efficiency numbers.** Everything
about *actually moving fuel* — the transfer amount, the depletion loop, tank
capacities, per-engine burn rates, the value of one fuel unit — lived on the
GSim (Scala), which is gone. `ShipConfiguration.cs` ships ~40 flight tunables
and **not one fuel entry**; `ConfigKeys.cs` has no fuel key; every fuel schema
field defaults to proto zero. The only preserved number in the whole subsystem
is the 8/8/9 canister yield. **Everything else this server picks is WAREBORN
TUNING and is labelled as such in §12.5.**

### 12.2 The three components, exactly

All in namespace `Bossa.Travellers.Ship`. All fields optional, all
`IsRequired=false`; floats are `fixed32`, `EntityId` is an int64 varint.
**No events. No commands. On any of them.**

| id | name | fields |
|---|---|---|
| **1104** | `FuelConsumerState` | `1 fuelTankId: EntityId`, `2 attached: bool` — on the ENGINE |
| **1105** | `FuelGaugeState` | `1 capacity: float`, `2 fuel: float` — on the GAUGE |
| **1106** | `FuelTankState` | `1 capacity: float`, `2 fuel: float`, `3 subtanks: int` — on the TANK |
| — | `FuelData` (custom type) | `1 capacity`, `2 fuel` — the map value in `AccumulatedData.fuelTanks` |
| 1067 | `RefuelShip` | `1 refuelIntervalSeconds: int`, `2 refuelAmount: int` — AI only |
| 1116 | `ShipEngineState` | `4 consumption: float` is the fuel coupling; also `power/throttle/forward/spinup/overheatLimit/boost` |

`8068 DeprecatedBossaRigidbodyEngineData` is an empty marker with zero fields
and nothing to do with fuel. `190302/190303 EngineLatency*` are game-engine
metrics — a name collision, not propulsion.

### 12.3 The gauge: what was wrong, and what fixes it

**CONFIRMED — §11.5 was right, and here is the whole file.**
`acs/Assets.Scripts.Visualisers.Ship/FuelGaugeVisualizer.cs` has exactly one
`[Require]`:

```csharp
[Require] private FuelGaugeStateReader _fuelGauge;   // component 1105
```

and it never reads anything else off SpatialOS. It subscribes
`_fuelGauge.FuelUpdated += OnFuelUpdated` in `OnEnable` and polls
`_fuelGauge.Capacity` every frame. The catalogue seeds the gauge **1236**, which
that prefab has no reader for. A Unity visualiser does not enable until every
`[Require]` resolves and logs **nothing** when it does not — so the gauge is
craftable, placeable, visible, and its needle can never move. Same silent-failure
shape as the loom (`1264`) and the ship containers (`1081`+`1236`).

Enumerated **before** assuming one component is enough, per the standing rule:

| fuel-related client behaviour | its `[Require]` set | verdict |
|---|---|---|
| `FuelGaugeVisualizer` (the instrument) | **1105 only** | serve 1105 and it works |
| `FuelVisualizer` (added to every ship ROOT by `ShipPreprocessor.cs:77`) | **1106 only** | its one method `GetFuelPercent()` has **zero callers in the entire decompile**. Dead hook for a cut HUD readout. Serving 1106 on the hull buys nothing |
| `FuelTankShakeVisualizer` | none (walks `GetComponentInParent<ShipControlInputVisualizer>`) | shake is **throttle**-driven, not fuel-driven |
| `EngineVisualizer` | 1116, 1235, 1252, 1251 — **no fuel reader** | reads `ShipEngineState.consumption` for AUDIO ONLY; never gates thrust |
| `FuelPodVisualiser_fsim` | 2102 `LodgeableState` | worker-only; already served |

**So exactly one serve moves the needle: `1105` on the gauge part.** Nothing
else in the shipped client reads a fuel number for any purpose.

The gauge's own arithmetic, for what the player will see:

```csharp
Quaternion.AngleAxis(Mathf.Lerp(135f, -135f, Mathf.Clamp01(current / Mathf.Max(total, 1f))), Vector3.forward)
```

a **270° sweep, +135° at empty to −135° at full**, plus four odometer digits and
a magnitude roller in powers of 1000. Two smoothing stages sit in front of it: a
`DelayedInterpolator` with **`Delay = 2.0` seconds**, then
`Mathf.Lerp(current, target, 2f * Time.deltaTime)`. **The needle is meant to lag
about two seconds behind the wire.** That is retail behaviour, not a bug, and it
is why a 1 Hz server push is more than enough.

### 12.4 The two hard constraints this server has, that retail did not

**Constraint 1 — THERE IS NO FUEL TANK PREFAB.** The 349-name client entity
prefab census (`Ship/client-entity-prefabs.txt`, extracted from the shipped
`resources.assets` and re-verified against the ResourceManager container map)
contains `fuelgauge`, `fueldeposit`, `fuelextractor`, `fueleggspawnerequip` and
`egg` — and **no ship fuel tank**. The retail tank was a real entity carrying
1106, and we cannot spawn one: a name the client cannot resolve means the
materials are eaten and nothing appears. So the retail topology (per-tank 1106,
per-engine 1104 bound by tank id) is **not reproducible here**. Fuel must be
per-HULL state, which is what retail's own `AccumulatedData.fuelTanks`
aggregation did anyway, one level up.

**Constraint 2 — A VERB CANNOT BE INVENTED.** `InteractiveObjectVisualizer`
caches `Interactions.FirstOrDefault(i => i.verb == Verb)` **once**, in
`OnEnable`, where `Verb` is baked into the prefab at export time. Serving an
`Activate` entry to a prefab that has no `InteractiveObjectVisualizer` produces
no prompt at all. Of our 37 rows, only these prefabs bake one:
helm (`Man`), sail/lamp/horn (`Activate`), the four containers (`Inventory`),
`personalReviver` (`Activate`) and **`atlasSkyCore` (`Activate`,
`ShipCorePreprocessor.cs`)**. Of those, the sky core is the only one whose verb
is baked, unused and unclaimed by another feature.

**Therefore: the refuel point on this server is the ATLAS SKY CORE.** §11.4
records that we deliberately serve it `None` because "the shipped client has no
consumer for the resulting interact". This gives it one. It is a deviation from
retail (retail refuelled at the tank), it is stated as such, and it is the only
door the shipped client leaves open.

### 12.5 The numbers, and why

All WAREBORN TUNING unless marked. Every one is env-overridable, because none of
them is recoverable and the first live flight is the only real test.

| quantity | value | reasoning |
|---|---|---|
| one canister | **25 fuel** (8+8+9) | **WIKI/RECOVERED** — already in `FuelCanisterYield`, untouched |
| ship capacity | **250 fuel** | ten canisters. Large enough that refuelling is an errand, small enough that one salvage trip fills you. `WAREBORN_FUEL_CAPACITY` |
| burn at full throttle | **0.25 fuel/s** | a full tank is 1000 s ≈ **16 minutes of continuous full throttle**; one canister ≈ 100 s. `WAREBORN_FUEL_BURN_RATE` |
| burn shape | **proportional to abs(throttle)** | retail's `consumption` and `throttle` are separate live floats and the client's own audio scales load by their product. Half throttle costs half. Idling costs nothing |
| one refuel press | **everything that fits** | retail's per-press amount is unrecoverable. Moving the whole overlap in one hold beats making the player mash E |
| tank on introduction | **FULL** | see the risk note below |
| gauge push | **≤1 Hz, and only on a ≥1-unit change** | the client already delays the needle 2 s and lerps it; anything faster is invisible traffic |

**Why tanks start full, and why the gate is conditional.** Ships already fly on
this server. Shipping "no fuel, no thrust" against a live world would ground
every existing ship the moment it deployed, for a reason no player consented to.
Two decisions prevent that:

- a hull's tank is created **full** the first time it is seen;
- **a hull has a fuel system at all only if it has a mounted `atlasSkyCore`.**
  No core, no refuel door — so no core, no burn and no gate. A ship that cannot
  be refuelled can never be stranded by this feature. The core is the ship's
  power plant; that reading is thematically right and it is also the only
  non-punitive rule available.

### 12.6 The seam with `feat/ship-flight` — what this branch expects from theirs

This branch does **not** touch `ShipFlightService`, `FlightIntegrator`,
`FlightSession`, `FlightTuning` or `HullMassCalculator`. It meets flight at two
public seams only, and here is exactly what it wants:

1. **Today (this branch's own machinery).** `ShipControlInput_Handler` (1111)
   already forwards throttle deltas to `Flight.OnControlInput`. This branch
   mirrors the same delta into its own ledger using the **same production
   `FlightControlInput.Merge`**, so a held stick — which is *silent* on the wire
   — is still counted as burning. When a hull runs dry, the fuel service issues
   one ordinary `Flight.OnControlInput(pilot, throttle: 0f, …)`, the existing
   public API, and the handler clamps any later throttle command to zero while
   dry. The ship decelerates on its normal curve and stops. **No flight file is
   modified.**
2. **What we would rather have, from `feat/ship-flight`.** Two things, in
   preference order:
   - **`FlightIntegrator.Step` / `FlightSession.Advance` gain an `enginesLit`
     (or `fuelScale`) parameter**, exactly the shape `unfurledSails` already
     has, defaulting to the current behaviour. A dry ship should lose **engine**
     propulsion and keep **sail** propulsion — sails are wind, engines are fuel.
     Today's blunt throttle clamp kills both, which is wrong and is stated here
     as a known inaccuracy rather than hidden.
   - **A read-only "commanded throttle for this hull" accessor** on
     `ShipFlightService`, so the fuel service can stop mirroring the 1111
     stream. The mirror is small and uses their own merge type, but two copies
     of a truth is two copies.
   - Longer term, when engines are more than scenery: burn should scale with the
     **number of mounted engines** and with each engine's crafted
     `fuelEfficiency` stat, which is where retail put it. This branch keeps the
     rate flat and says so.

### 12.7 The phased plan

Same contract as §5 and §11.8: what it delivers · what a player can newly DO ·
dependencies · schema migration · networked state (soak gate) · main risk.

---

#### PHASE F1 — The needle moves, and fuel is real

- **Delivers:** the whole vertical slice, server-only.
  1. `ShipFuelPolicy` + `ShipFuelLedger` (pure, in `Multiplayer/Ship/Fuel/`):
     per-hull capacity and level, deposit, throttle-proportional burn, the dry
     transition.
  2. **`1105 FuelGaugeState` served on the `fuelGauge` part**, reading the hull
     the gauge is mounted on. Replaces the §11.5 defect. `1236` stays alongside
     it — it is served correctly and removing it is a separate, unrelated risk.
  3. **Refuel**: `Activate` on a mounted `atlasSkyCore` moves every unit of
     `"fuel"` in the player's inventory that fits into the hull's tank, using
     the same `CraftingPolicy` drawdown idiom the crafting path uses, then
     `InventoryPush.Push`.
  4. **Burn**: `ShipFuelService.Tick()` on the main loop, burning
     `rate × |throttle| × dt` for every hull under power.
  5. **Gate**: at zero, one `OnControlInput(throttle: 0)` and a clamp on later
     commands, per §12.6.
  6. **Gauge push**: 1105 broadcast to every mounted `fuelGauge` on that hull,
     rate-limited per §12.5.
- **Player can newly do:** salvage fuel canisters, walk up to their ship's sky
  core, hold E, and **watch the fuel gauge needle climb**. Then fly, and watch
  it fall. Then run out, and coast to a stop.
- **Depends on:** nothing. `atlasSkyCore`, `fuelGauge` and the `"fuel"` item
  all already exist and already work.
- **Migration:** **no.** Fuel lives in memory for the session; a restart
  refills. Deliberate — see F3.
- **SOAK: YES.** A per-ship level ticking continuously with a periodic 1105
  broadcast is exactly the new networked state the standing rule names.
- **CLIENT MOD: no.** Every piece is a component the stock client already
  reads, on a prefab it already resolves.
- **Main risk:** the sky-core prompt reads as the generic *Activate*, not
  "Refuel" — `InteractionEntry.description` is transmitted and **never rendered**
  by the shipped client (§12.1), so there is no way to label it. A player who
  does not read patch notes will not discover refuelling. Second risk: a ship
  with no sky core silently has no fuel system; that is the deliberate
  non-punitive rule, but it means two ships can behave differently for reasons
  the player cannot see.

---

#### PHASE F2 — Fuel is visible without a gauge

- **Delivers:** a chat/toast line on refuel ("Refuelled: 250/250") and a low-fuel
  warning at 10%, both on the existing native toast path
  (`HarvestReward`'s 8060). Retail had neither, and retail also had a tank you
  could walk up to and a gauge on every ship; we have one instrument and an
  unlabelled prompt.
- **Player can newly do:** find out they are nearly dry without having crafted
  and mounted a fuel gauge.
- **Depends on:** F1.
- **Migration:** no. **SOAK:** no — one-shot toasts on an existing path.
  **CLIENT MOD:** no.
- **Main risk:** toast spam if the threshold has no hysteresis.

---

#### PHASE F3 — Fuel survives a restart

- **LOUD:** this is isolated into its own phase **on purpose**. It is an
  additive `double Fuel` property on `BuiltShipRecord` in **`world-state.json`**
  — plain JSON, game-server only, whose default `0` must be read as "legacy,
  fill it" and not as "empty". It is **NOT** a Postgres migration and does
  **NOT** require a login-server deploy. The same additive-default discipline
  `MountedPartRecord.LampOff` documents (stored inverted so the JSON default
  means the legacy state) applies here, and the legacy sentinel must be chosen
  before a line is written.
- **Delivers:** a tank level that persists across a server restart.
- **Depends on:** F1.
- **Migration:** **JSON only, additive, no DB.** **SOAK:** no. **CLIENT MOD:** no.
- **Main risk:** reading a legacy `0` as an empty tank and grounding every ship
  in the world on the first restart after deploy. Use a nullable/sentinel, not a
  bare double.

---

#### PHASE F4 — Engines matter

- **Delivers:** burn scaled by mounted engine count and by each engine's crafted
  `fuelEfficiency` stat (the retail location for it, §12.1); `1104
  FuelConsumerState` served on each engine so `attached` is honest; the
  `enginesLit` seam of §12.6 replacing the throttle clamp.
- **Player can newly do:** craft an economical engine and get more range out of
  the same canister — the reason `fuelEfficiency` exists.
- **Depends on:** **`feat/ship-flight`** shipping the `enginesLit` parameter,
  and on engines being more than scenery.
- **Migration:** no. **SOAK:** yes — 1104 is new per-engine state.
- **Main risk:** it is not this branch's to sequence.

### 12.8 What only a live flight can settle

1. **Whether the needle actually moves.** Nothing headless renders a
   `GaugeRoller`. Craft a Fuel Gauge, mount it, refuel, and watch. The needle
   lags ~2 s by design (§12.3); it is **not** broken if it is late.
2. **Whether the sky core shows an E prompt at all.** The verb is baked
   (`ShipCorePreprocessor`), but this server has never served the core an
   `Activate` entry before, and `IsSeededInteractionAvailable` gates it on
   *mounted*. A loose core must show nothing; a mounted one must show a prompt.
3. **What the prompt says.** Predicted: the generic Activate glyph with no text,
   because `description` is never rendered. If it reads anything else, §12.1 is
   wrong about the client.
4. **Whether a dry ship coasts or stops dead.** The clamp goes through the
   normal deceleration curve, so it should glide to a halt over several seconds.
   A hard stop would mean the clamp is landing somewhere it should not.
5. **Whether a dry ship holds altitude.** It must. Fuel never touched
   `ShipLiftState 1258` in retail and does not here. If a ship sinks, that is a
   flight bug this feature revealed, not a fuel behaviour.
