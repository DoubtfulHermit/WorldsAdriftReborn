# PLAN — THE COMPLETE FEATURE ROADMAP

**Written:** 2026-08-19. **Branch:** `docs/feature-roadmap`, cut from `main`.
**Scope:** every item on the Worlds Adrift wiki's contents index, plus the
systems that index omits, audited against this repo and the retail decompile,
then ordered into phases.

This is a **planning document**. Section 0.0 below records what has since
been BUILT; the status tables in §2 were written before that work and are
stale wherever §0.0 contradicts them.

---

## 0.0 WHAT SHIPPED AFTER THIS DOCUMENT WAS WRITTEN

**Everything in this section is live on production** unless marked otherwise.
Added 2026-08-20 as a session handover. `main` was `ee86213` at the time.

### Shipped and confirmed by the maintainer in a live client

| item | was | now |
|---|---|---|
| **Client could not connect at all** | PLAY hung forever | fixed; `2026.08.19-2` unbroke the patcher for every player |
| **Trees** | vanished on cutting | fall, break piece by piece, rest on slopes |
| **Tree yields** | wood only | pay plant fibre and berries |
| **Loot containers** | `LootContainers => 0` | **409 live on tier-1**, openable |
| **Scrap** | unobtainable, unsalvageable | drops from containers, salvages to metals/woods/fuel |
| **Ship containers** | 4 of 37 parts interactable | **7** — trunk, mountedBox, storageContainer, shippingContainer |
| **Fuel gauge** | gated on the wrong component | needle moves |
| **Emblem editor** | 33 objects | **283**, mirror bit, grid snap, PNG export |
| **Player portal** | one long page | tabbed, redesigned on a token layer |
| **`/patchnotes`** | hand-written prose | generated from the commit log |

### Shipped, awaiting a live check

| item | detail |
|---|---|
| **Fuel** | lives in the **Power Generator** (100/generator, pooled). Prompt says "Refuel" because the client's own baked asset says it. `WAREBORN_FUEL_GATES_THRUST=0` until confirmed |
| **Real flight forces** | `WAREBORN_FLIGHT_FORCES=1`, `WAREBORN_FLIGHT_WIND_SPEED=4.0`. Bare hull drifts, sails faster, engines faster still |
| **Bar pipes** | implemented, and made **real Unity children** of the hull — the first part on this server with a real hierarchy key |
| **Inventory belt** | divider was at row 3, should be `height - 4` = 14 |
| **Deposits** | quality now reaches the item; metal already varied off Haven |

### Research completed — new documents

| document | what |
|---|---|
| `reality-inventory.md` | 2,115 lines. What the client CONTAINS vs what we built. **98 ship parts / we had 40. 443 component ids / we serve 135. 228 knowledge nodes / 16 have a schematic** |
| `findings-resource-catalogue.md` | every gatherable, how harvested, LIVE/PARTIAL/MISSING |
| `findings-combustion-fuel.md` | how fuelling worked |
| `research/basher.md` | the "unreferenced creature" is **Little Basher**, the client's only customisation pet, wired end to end |
| `research/archive/worlds-adrift-wiki/` | **425 wiki pages archived into the repo** |
| §11, §12, §13 below | ship components, flight physics, fuel — all written this session |

### Live defects found and NOT yet fixed — these outrank most of §5

1. **118 of 228 knowledge nodes take payment and grant nothing** (`learned = null`, no error).
2. **13 nodes grant something else** — buy Makeshift Bandages, receive a Personal Reviver.
3. **Five recipes are learnable and uncraftable.** The **Territory Control Tower costs 5000 knowledge** for a recipe the server always refuses. `StationCraftRouting` names only two targets; a third is missing.
4. **The relay two-state defect** — 40% of sessions eat 50 ms of avoidable latency. The soak gate now catches it (`REGRESSED`), so expect red on ~2 runs in 5 until fixed.
5. **The database credential is still in the systemd environment.**

### Corrections to this document, made in the same session

- **§2's "the per-island metal table is unused"** — wrong; production runs `tier1` and it is reachable. Only Haven's 40 are hardcoded iron, deliberately.
- **"Scrap salvages into cloth/leather/glass/pigment"** — wrong. All 133 rows yield metals, woods and fuel ONLY. The Update 27 economy has **no recovered bootstrap**.
- **"Retail's flight model is lost"** — wrong. The physics shipped in the same `Assembly-CSharp` as the client. Only per-part data is missing.
- **"Instruments mount on the deck only"** — wrong as a description of retail. The maintainer has seen players mount on pipes; the blocker is our `"~"` parenting, not the attachment type.
- **SC3 "blocked on a collider"** — wrong. `RailingStraight` carries colliders on layer `Default`, inside `Layers.Environment`.

### The error class this session kept meeting — FOUR instances

An agent searches for a thing, does not find it, and **designs around its absence**.

1. **Fuel** built per-hull because a search for a "fuel tank" found none — the tank is the **Power Generator**, line 219 of the same census.
2. **Instrument mounting** hacked because nobody knew **bar pipes** existed — they were in this repo's own `valid-icons.txt` line 873 for nine days.
3. **Flight** thought to need engines — the **sky core** is what makes a hull mobile.
4. **"Basher is unreferenced"** — a **mechanical** false negative: `grep` here is **ugrep**, which silently returns 0 and exit 1 on binary files unless given `-a`. Any earlier binary sweep without `-a` is suspect and should be re-run.

**The method that works:** community sources tell you what a thing is CALLED; the decompile tells you how it WORKS. The wiki archive is now committed for exactly this.

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
| **Fuel** | **LIVE end to end (`feat/ship-fuel`, then reworked onto GENERATORS on `feat/fuel-generators`, §13 + §13.11)** | Canisters are a **salvage target, not a pickup** — `Multiplayer/FuelPods.cs:10-17,60,87`; recovered per-shot yield 8/8/9 = 25 over THREE shots (`Multiplayer/FuelCanister.cs`), arriving on the same `2106` beam path as metal. Consumed by 6 real recipes. **And BURNED:** **the POWER GENERATOR is the fuel tank** — capacity 100 each, pooled by summation across however many are bolted to a hull (`Multiplayer/Ship/Fuel/ShipFuelLedger.cs`). **Refuel is holding E on the generator**, whose baked client prompt literally reads **"Refuel"**; throttle burns it; **`1105 FuelGaugeState` is served on the `fuelGauge` part so the needle moves**. `1104`/`1106` remain unserved and buy nothing — `FuelVisualizer` is the only 1106 reader, it lives on ship ROOTS, and its one method has zero callers. The per-hull tank and the sky-core/bunker refuel doors are BOTH superseded; see §13.11. |
| — dangling doc reference | **DONE** | `docs/research/findings-combustion-fuel.md` now exists and is indexed; it was cited from five code sites and was not in the tree. |
| **Atlas Shard** | **LIVE** | `Multiplayer/AtlasShardCatalogue.cs:57` (`ItemTypeId = "atlasShard"`); every release deposit registers a shard, gated by `WAREBORN_SPAWN_ATLAS`/`WAREBORN_ATLAS_RATE`. 328 shards live in tier 1. **One data defect:** `atlasShard` is categorised `"Metal"` in `itemData.json`. `resource-economy` deliberately unbundled that fix, so it is **open** — see §4.1. |
| **Update 27 second economy** (plant fibre, berries, meat, leather, chitin, cloth, pigment, glass) | **PARTIAL, in flight** | `clothMakeshift` ("Makeshift Cloth") is the only `Component` row in `itemData.json`. Plant fibre and berries are **landed on `feat/resource-economy`** (commit `0aa0fe8`, paid off the same cut that pays wood). Meat is blocked on creature mortality (their Phase 7). Leather/chitin/pigment/glass: **MISSING**, and note `loot-containers` §0.3 **corrects the audit** — the recovered scrap `rewards` are metals, woods and fuel, *not* cloth/leather/glass/pigment. |
| **`YieldRule` quality defaults to 0** | **PARTLY FIXED, in flight — and there is a second cause** | Confirmed at `Multiplayer/Gathering/YieldRule.cs:26,53`, with all five registration sites omitting the argument. Fixed on `feat/resource-economy` `d756972`. **Two things that fix does not cover, both still open:** (a) the yield table is keyed by **metal name, not by node** (`Multiplayer/Gathering/HarvestYield.cs:36,50-64` — `_rules[sourceKey] = rule` *overwrites*), so two iron nodes of different quality clobber each other; (b) **crafted output quality is hardcoded to `0`** independent of inputs at `Handlers/PlayerCraftingInteractionState_Handler.cs:297`, and `SchematicRecord.CraftingRequirement` has no quality field at all. Quality is served to the client and honoured for stacking, so the plumbing exists — only the values are zero. Confirm with that branch which of these it claims. |

### 2.2 Environment

| item | status | evidence |
|---|---|---|
| **Weather** | **MISSING — deliberately, and documented** | `1139 WeatherCellState` and `1269 RadialStormState` are in `Multiplayer/ComponentAbsencePolicy.cs:151,177` and its `KnownAbsentComponentIds` (`:367-396`); `Game/Components/ComponentsSerializer.cs:134-138` short-circuits them before the vtable scan. The old seed branches were **deleted with a comment forbidding restoration** (`ComponentsSerializer.cs:1764-1779`). See §5.6 for the full implementation path. **Line numbers corrected 2026-08-20; the old ones were stale. And see §14: 1269 is the BLIGHT, a separate system from the understorm, and neither it nor the weather walls depend on the 1139 lattice.** |
| — **understorms** | **LIVE but suppressed — the cheapest storm surface in the game** | `1254 IslandLightningTimerState` is seeded `50*1000` with a second field of `0`, "or you will set the island into a storm" (`ComponentsSerializer.cs:1780-1790`). **§14 proves the mechanism**: that second field is `estimatedMilliTillLightningEnd`, and `IslandLightningTimerVisualizer.IsLightningActive` is literally `> 0` on it (`acs/IslandLightningTimerVisualizer.cs:226`). One `[Require]`, no lattice, no client mod. **The understorm IS the island resource respawn** — its own audio loop is named `Play_IslandRespawn_Start` (`:129`). |
| — weather walls | **MISSING as gameplay / LIVE as cartography** | 44 typed segments recovered in `docs/research/world-data/wamap-islands.json`; palette named in `WorldsAdriftServer/Admin/MapWallPalette.cs:38-45` (Wind Rift, Storm Rift, Typhon, Sand Storm, Ice Storm, World End). `HANDOVER.md:1007`: "weather-wall gameplay are not spawned". `1204 WallSegmentState` has **0** server refs. **§14.4: walls need ONLY 1204 — `WeatherWalls` never calls `GetWeatherAt`, so they do NOT depend on the weather lattice. Typhon and Ice Storm have ZERO segments in the release map.** |
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
7. **Wind walls do not need cells at all — CORRECTED 2026-08-20, see §12.6a
   and §14.4.** `WallSegmentVisualizer` has exactly one `[Require]` (`1204`) and
   registers into `WeatherWalls`, a pure segment registry that never calls
   `GlobalWeather.GetWeatherAt`. That is enough for the independent visual-only
   wall phase built in §14.4. Mechanical wall wind must additionally ship with
   a complete `1229 GlobalWallDataState`, or the wall contribution lerps to dead
   calm. `5129` is not a client delivery mechanism; it is a worker-side report
   channel with no client reader. `1204 WallSegmentState` =
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

> **SUPERSEDED 2026-08-20 by §14.** This phase conflated **three unrelated
> systems** under the word "storm" and gave all of them the blocker of the
> hardest one. Separated: the **understorm** (`1254`, which we already serve) is
> reachable NOW and needs neither the 1139 lattice nor a client mod; **weather
> walls** (`1204`) need no lattice either; and the **Blight** (`1269`) is not blocked on Phase 6 either — its
> "needs a client mod" blocker turned out to be a **false zero** (§14.3.3).
> **§14 is the current plan.**
> `1256 SandStormAffecteePositionalState` is dead to us — its only consumer is
> `[WorkerType(WorkerPlatform.UnityWorker)]` and never runs on a player client.
> The text below is kept for its Blight analysis, which §14.3.3 extends.

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

**Nothing in Phases 0–5 requires a client-mod change** (SC3 does — see the first row below)**.** Everything there is
server-side seeding, handlers and data.

Candidates that probably do:

| item | phase | why |
|---|---|---|
| Deck parts mounting on placed objects | SC3 | **WRITTEN, unbuilt, unreleased.** `Patching/Ship/DeckPartsMountOnPlacedObjects_Patch.cs`. The placement mask and tag are decided entirely client-side, so no server string can reach a railing's `Default`-layer collider. §11.6 |
| ~~`BlightLocalComponent` attachment for storms~~ | 7 | **WITHDRAWN 2026-08-20, §14.3.3.** The client DOES attach it, from a JSON blueprint that ships inside `resources.assets`, via `ApplyBlueprintLocalComponentsS`. The "never attaches it" finding was a **false zero**: `WASystems.dll` and `SpatialTranslator.dll` are not in the decompile tree. **No client mod appears to be needed.** |
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

**Added 2026-08-19 — `fix/ship-interactions`** (cut from `main` at `a61e188`,
unmerged). The five findings from the maintainer's live session: §11.10 (the
container lock — `OwnershipRegistrationPolicy` + the two hull owner-list serve
sites in `ComponentsSerializer`), §13.10 (refuel moved off the sky core —
`PartInteractionPolicy`, `PartInteractionService`, `ShipFuelService`, new
`Multiplayer/Ship/Fuel/ShipFuelBunkerPolicy.cs`), §11.11 (the altimeter — the client
patch **DELETED**, the missing **Bar Pipe** implemented, and the root cause named:
`Parent(hull,"~")` breaks five client parent-walks. Removing the patch from
players is itself a patcher release, because manifest `2026.08.19-6` shipped it),
§11.5 (the fuel gauge: **not a defect**)
and §12.11 (helm momentum: **no code change**). Collisions to know about:

- It edits `ComponentsSerializer.cs`, which nearly everything edits.
- It does **NOT** touch `ComponentAbsencePolicy` or the serializer's absence
  declarations — `fix/component-init` owns those. **What it needs from them:**
  `1306 ShipAtlasPulseState` must come OUT of `KnownAbsentComponentIds` and gain
  a serve branch before the Atlas Pulse can be implemented (§13.10). Their `1120`
  work also bears on §11.11, since `1120 ShipPartState` is the mounting-surface
  component.
- It does not touch any flight file.

**Gates on `fix/ship-interactions`:** Multiplayer **4059 passed / 0** (baseline
4032), `WorldsAdriftServer.Tests` **1192 / 26 skipped** unchanged, both server
builds and the client-mod build green. **Relay soak FLAT on the first run** and
inside the `haven-spawn` baseline: drift +0.01 ms, trend +0.03 ms against 20 ms,
17,286 sends 100% delivered, missed ticks 0%, 0 gaps, 0 disconnects, 0 decode
errors, 0 timeline violations
(`tools/relaybot/run/soak-20260819-232341.csv`). Run because the bunker drain
adds `1081` pushes on an entity that rides a moving ship; the canister threshold
is what keeps that to roughly one push per 100 s of full throttle. **Sixteen
deliberate mutations of the new wiring were applied one at a time and every one
was caught** - cutting the drain call, dropping the container gate, dropping the
never-opened-container guard, bypassing the pure plan, restoring the sky-core
dispatch, re-advertising Activate on the core, ceiling instead of flooring the
tank's room, uncapping a draw, emitting zero draws, removing the wire rule,
firing it on any room at all, setting it high enough to block an empty tank, and
four ways of losing one or other identifier from the hull owner list (two of
which were invisible to the suite until a source-reading wiring guard was added
for the serve sites).

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

  **Scope correction, 2026-08-19.** That statement is true and it is about the 37
  LOOSE-PART rows only. "Has a branch" is not the same as "the branch answers for
  every entity that asks": `1013`, `1120` and `8066` are all gated on the
  `LooseParts` / `MountedParts` ledgers, and the entities that ask for them and
  are in NEITHER — a **built ship's hull and its deck sub-entities** — got
  nothing. That was the single largest source of `[error] failed to initialize
  component` on the live server. **It never dropped a batch** (every one of those
  requests arrives best-effort; there is not one `DROPPING the whole
  AddComponent batch` line anywhere in the journal back to 2026-08-08), so no
  ship component was at the origin and the headline above stands. See §11.10.
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
| 6 | `atlasSkyCore` | CoreMain | deck → ShipDeck | `ShipCoreVisualizer` → **1236 + 190602** | 1236 | **yes** | **no — and that is now the SETTLED answer.** Retail bakes `Activate` (`ShipCorePreprocessor`) and the client's own overlay labels it **"Activate Atlas Pulse"** — a real retail action (1306, the anti-boarding pulse) whose text no server can change. Fuel briefly borrowed this door and it was wrong; see §13.10 | flat deck only |
| 7–14 | the 8 `skyCore*` modules | Core* | coreModule → CoreModule | `ShipCoreModuleVisualizer` → **1236 + 190602** | 1236 | **yes** | no (retail: passive animators) | **a socket on CoreMain** — the only true socket path in the game |
| 15 | `smallPanel` | Panel01 | side → ShipSide | `ShipPanelVisualizer` → **1118**; variation → 1246 | 1118 | **yes** | no | hull side |
| 16 | `mediumPanel` | Panel02 | side → ShipSide | same | 1118 | **yes** | no | hull side |
| 17 | `largePanel` | Panel03 | side → ShipSide | same | 1118 | **yes** | no | hull side |
| 18 | `window` | Window01 | side → ShipSide | same | 1118 | **NO — see §11.3. Fixed on this branch, unverified in game** | no | hull side |
| 19 | `stairs` | Stairs1 | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 20 | `railing` | RailingStraight | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 21 | `railingCorner` | RailingCorner | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 22 | `trunk` | ContainerSmall | deck → ShipDeck | `InWorldInventoryVisualiser` → **1210 + 1081**; `IsTooDamagedToWorkVisualizer` → **1236**; baked verb **Inventory** | 1081, 1236 | **yes** | **yes** — `Inventory`, since `feat/ship-components` | flat deck only |
| 23 | `mountedBox` | ContainerMount | deck → ShipDeck | same | 1081, 1236 | **yes** | **yes** — `Inventory`, since `feat/ship-components` | flat deck only |
| 24 | `storageContainer` | ContainerMedium | deck → ShipDeck | same | 1081, 1236 | **yes** | **yes** — `Inventory`, since `feat/ship-components` | flat deck only |
| 25 | `shippingContainer` | ContainerLarge | deck → ShipDeck | same | 1081, 1236 | **yes** | **yes** — `Inventory`, since `feat/ship-components` | flat deck only |
| 26 | `barrel` | Barrel01 | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 27 | `cupboard` | Cupboard | deck → ShipDeck | — | — | **yes** | no | flat deck only |
| 28 | `horn` | Horn01 | deck → ShipDeck | `HornVisualizer` → **1107** | 1107 | **yes** | **yes** — `Activate` (honk) | flat deck only |
| 29 | `lamp` | Lamp01 | deck → ShipDeck | `LampVisualizer` → **1108 + 1236 + 1099** | 1108, 1236 | **yes** | **yes** — `Activate` (switch) | flat deck only |
| 30 | `altimeter` | Altimeter | deck → ShipDeck | `AltimeterVisualiser` → **1236** | 1236 | **yes** | no — **correct**, retail made it a local readout | flat deck only |
| 31 | `fuelGauge` | FuelGauge | deck → ShipDeck | `FuelGaugeVisualizer` → **1105 FuelGaugeState** | **1105 + 1236 — fixed on `feat/ship-fuel`, §13.3** | no — **correct, it is a dial.** `FuelGauge` has no preprocessor of its own, and `ShipPartPreprocessor` adds no `InteractiveObjectVisualizer` to anything, so no verb is baked and E can never do anything on it. Reported as a defect 2026-08-19; it is not one | flat deck only |
| 32 | `headingIndicator` | HeadingIndicator | deck → ShipDeck | `HeadingIndicatorVisualiser` → **1236** | 1236 | **yes** | no — correct | flat deck only |
| 33 | `artificialHorizon` | ArtificialHorizon | deck → ShipDeck | `ArtificialHorizonVisualiser` → **1236** | 1236 | **yes** | no — correct | flat deck only |
| 34 | `airspeedIndicator` | AirspeedIndicator | deck → ShipDeck | `AirspeedIndicatorVisualiser` → **1236** | 1236 | **yes** | no — correct | flat deck only |
| 35–36 | `powerGenerator`, `powerGenerator01` | PowerGenerator01 | deck → ShipDeck | **none** — but see §13.11: the prefab bakes `InteractiveObjectVisualizer(Activate)` + `TutorialHelper(MOUSE_OVER_GENERATOR)` | — | **yes** | **YES — `Activate`, and the client's own prompt reads "Refuel".** This part is the ship's FUEL TANK. Served on `feat/fuel-generators`, §13.11 | flat deck only |
| 37 | `personalReviver` | Respawner01 | deck → ShipDeck | `RespawnerVisualizer` → **1094 + 8066** | — | **yes** (the prop; `ShipPartVisualizer` renders it) | **NO — should be. §11.4** | flat deck only |

**The headline numbers.**

- **36 of 37 appear.** One does not: the **Window**. That is a mesh-selection
  failure, not a missing seed, and it is fixed on this branch.
- **7 of 37 are interactable today** — helm (`Man`), sail, lamp, horn
  (`Activate`), and the four storage containers (`Inventory`, delivered by
  SC2 on `feat/ship-components`; note the four share one row group, so the
  count is helm + sail + lamp + horn + 4 = 7 of 37). **1 more should be and
  is not**: the personal reviver. The sky core LEFT this list again on
  2026-08-19 — its verb is free but its LABEL is not (§13.10) — so it is now
  filed with the reviver as "retail verb known, blocked on serving the state
  behind it", `1094` for one and `1306` for the other. The other 27 are
  correctly inert; retail's
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

**DELIVERED for the four containers (SC2, branch `feat/ship-components`).** They
now seed `1081 + 1236` and serve the `Inventory` verb, `ShipContainerService`
binds each one its own grid before the 1081 serve can hand it the player starter
kit, the 1211 dispatch echoes `Interact(Inventory)` on the container's own 1210,
cross-inventory moves accept a MOUNTED ship container as one end, and the salvage
beam refuses a container that still holds anything. Contents are session-scoped
like a ruin chest's. The reviver and the core are untouched and the paragraph
below still governs them.

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

> **SHOULD AN INSTRUMENT RESPOND TO E AT ALL? NO, AND THE GAUGE IS NOT A
> DEFECT.** Reported 2026-08-19 as *"I am unable to interact with the fuel gauge
> with E, but I can see it ticking down visually and the pin on it."* That is the
> correct and complete behaviour. `FuelGauge` has **no preprocessor of its own**
> — an exhaustive listing of `acs/**Preprocessor.cs` has no fuel-gauge entry —
> so it runs the generic `ShipPartPreprocessor`, and that adds
> `JointDamage`/`ShipPart`/`DetachFromParent`/`ParentingMassAdder`/
> `LightningStrikable` plus the client set, and **no
> `InteractiveObjectVisualizer` at all**. No baked verb means no prompt is
> possible, from any server, ever. A gauge is a readout: the half the maintainer
> can see working IS the whole feature. Nothing to fix, and nothing to regress.

The fuel gauge is not one of them. `FuelGaugeVisualizer` lives in
`acs/Assets.Scripts.Visualisers.Ship/` and `[Require]`s **`1105
FuelGaugeState`** — and `1105` has **zero server references** (§2.1 already
records it as unserved alongside `1104` and `1106`). The catalogue seeds it 1236,
which nothing on that prefab reads. **So the Fuel Gauge is craftable, placeable,
visible and its needle can never move.** RECOVERED from the decompile; the
catalogue's own comment groups all five instruments together, which is where the
mistake entered.

It was also the one instrument whose fix was blocked on something real: nothing
in this server burned fuel, so a served `1105` would have read zero forever.
**That blocker is gone.** `feat/ship-fuel` builds the burn, the tank and the
refuel alongside the serve, exactly as "wire it with combustion, not before"
demanded — see **§13**, which also enumerates every other fuel-related
visualiser and what each `[Require]`s, because one component is rarely enough.

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

**SETTLED — 2026-08-19, and the answer is YES.** Read out of the shipped
`UnityClient@Windows_Data/resources.assets` (entity prefabs are baked into
`resources.assets` in this build; there are no separate
`entityprefabs/*_unityclient` bundles on disk), decoded against the `TagManager`
in `globalgamemanagers`:

| prefab | colliders | layer | tag |
|---|---|---|---|
| `RailingStraight` → `rail_single/double_straight_wood` | 4 × BoxCollider, enabled, non-trigger | **0 `Default`** | **`Untagged`** |
| `RailingStraight` → `rail_single/double_straight_metal` | 4–5 × CapsuleCollider, enabled, non-trigger | **0 `Default`** | **`Untagged`** |
| `Panel02` | none authored — `ShipPanel.SetPanelPositions:238-249` creates `PanelCollider-i-j` at runtime with `new GameObject`, which never assigns `.layer` or `.tag` | **0 `Default`** | **`Untagged`** |
| `Deck01` → `DeckMesh` / `WoodDeckMesh` | `MeshCollider`/`BoxCollider` added by `MeshGenerator.MakeDeck` | **12 `ShipAttachmentSolid`** | **`ShipDeck`** |

`Default` is inside `Layers.Environment`, and `Entity`/`ShipSurfaces` return an
EMPTY tag — so **an Environment-mask raycast hits a railing today**, and the
only thing keeping a deck part off one is the deck's own mask and `"ShipDeck"`
tag. The decode is validated by a control: across all 78k GameObjects in
`resources.assets` exactly 4 sit on layer 12 and 9 carry tag `ShipDeck`, and
they are precisely the objects `GetMask`/`GetTag` demand for the `ShipDeck` path.
Nothing relayers a ship part at runtime except `ModularWing.cs:76`
(→ ShipAttachmentSolid) and `ModularCannon.cs:143` (→ Interactive), both
self-targeted.

**So there is a THIRD option, and it is better than both of the above.** Neither
listed option is necessary, because `ValidSurfaceTypes` does not have to change
at all: `GetMask` composes with `&` per flag and is fine, and only `GetTag`'s
one-tag-for-every-hit rule and the mask's missing `Environment` bit are in the
way. Widening exactly those two, for `ShipDeck` phantoms only, keeps
`ValidSurfaceTypes == ShipDeck` and therefore keeps every `==` behaviour switch
true — the deck flatness rule, the ship-aligned base rotation and
`NeedToBeOnShip` all keep running. That is what
`Patching/Ship/DeckPartsMountOnPlacedObjects_Patch.cs` does (SC3, branch
`feat/ship-components`). It still needs a patcher release, but it does NOT need
the flatness rule to be re-implemented by hand, which was the main risk in the
`All` plan.

**Consequence worth naming:** blanking the tag also stops protecting the deck
path from untagged `ShipAttachmentSolid` geometry, of which the only instance is
`ModularWing`'s runtime-relayered skin. The ≥0.9 flatness gate limits that to a
wing's upper surface.

### 11.7 So — one defect, or three?

**Three, and that is the useful answer.** The tempting conclusion after the loom
was that everything inert is one under-seeding bug. It is not:

1. **The Window** is a *value* bug, not a *presence* bug — every component it
   needs is served; one of them names art that does not exist. **Fixed here.**
2. **The containers and reviver** are the loom's defect exactly — partial
   `[Require]` sets plus a verb mismatch. They ride on
   `feat/loot-containers`' `1081` work and should not be planned separately.
   **The containers are DONE** (SC2); the reviver still needs `1094` and a
   respawn flow to be worth a prompt.
3. **Placement** is not a defect at all. It is a deliberate, documented,
   correctly-reasoned narrowing (`"shipSurfaces"` → `"deck"`) that solved a real
   bug and created this one. Reversing it is a **design decision**, and the data
   retail used is unrecoverable. **What the 2026-08-19 asset read adds is that
   the decision is not a trade after all**: the deck's mask and tag are the only
   things excluding a placed prop, both are chosen client-side, and widening just
   those two keeps the deck. See §11.6.

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

- **(a) is DONE, and better than proposed.** `feat/ship-fuel` did not mark the
  gauge dormant; it built the fuel to put behind it (**§13, phase FU1**), so the
  row now seeds `1105 + 1236` and the needle reads a real tank. (b) and (c)
  below are still open.
- **Delivers:** (b) widen the prefab `[Require]` census from
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

#### PHASE SC2 — Ship storage opens *(DONE on `feat/ship-components`)*

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

#### PHASE SC3 — Instruments and decorations mount where the player wants them *(patch written on `feat/ship-components`; NOT built, NOT released)*

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
3. ~~**Whether a mounted railing exposes a mountable collider.**~~ **SETTLED by
   the asset read, §11.6: yes — layer 0 `Default`, `Untagged`, 4–5 enabled
   non-trigger colliders.** ~~What a live client still has to settle is one step
   further downstream: whether a mounted railing's runtime parent chain carries a
   `DockableVisualizer`.~~ **ALSO SETTLED, 2026-08-19, and the answer was NO — it
   was the exact failure this entry predicted. §11.11.**
4. **Whether the sky-core module sockets restore correctly** on every module. The
   socket components are stripped from every shipped prefab and re-added by
   `Patching/SpatialOS/SkyCoreSocketRestore.cs` at template-compile time. Eight
   modules; only the chain as a whole has been live-confirmed.
5. **Whether `PartGraphicsVariationByMaterial` on `Window01` has a baked
   `_metalPrefab`.** SC0's named risk.

---

### 11.10 THE CONTAINERS OPENED AND SAID "It's locked." — a THIRD identity gate

**Reported:** *"I can attach it to ship, when I press E it says its locked."*

SC2 was correct in every part a server can see. The four rows seed `1081 + 1236`,
`ShipContainerService` binds each its own grid, `PartInteractionPolicy` serves the
prefab-baked `Inventory` verb, availability flips on mount, and the 1211 dispatch
echoes `Interact(Inventory)` on the container's own 1210. **None of it was
reached**, because the client refuses the press before it ever sends the 1211.

**The producer is a single hardcoded string** —
`InteractAgentObserver.CheckInteraction`, `InteractAgentObserver.cs:391-394`:

```csharp
if (verb == InteractVerb.Inventory && !IsTooDamagedToWorkVisualizer.IsTooDamagedToWork(...))
{
    OSDMessage.SendMessage("It's locked.", MessageType.Server);
    return;              // the 1211 is NEVER sent
}
```

guarded by `flag2 = !IsSuperUser && !IsInUseBy(me) && !flag` (`:374`), where
`flag` is "this part is on a FRIENDLY ship", computed at `:355-361`.

**Not the locks.** `1217`/`1218`/`1220`/`1221` are the **shipyard code-lock** and
are innocent: `LockVisualizer` is attached by no preprocessor at all,
`LockAgentVisualizer` lives on the PLAYER prefab and only emits
`SHIPYARD_LOCK_REJECTED/INVALID`, and `ShipContainerPreprocessor` attaches no
lock visualiser of any kind. **Not a client default either** —
`InWorldInventoryVisualiser` contains zero lock logic, and
`InteractiveObjectVisualizer.UpdatePrompt(bool locked)` ignores its own parameter
and renders no text. **It is ours.**

**THE ROOT CAUSE IS A CROSS-AXIS COMPARE, and §11.6's own asymmetry note was one
consumer short.** `OwnershipRegistrationPolicy` documents two gates keying on
deliberately opposite identifiers: gate A (the shipyard) compares
`LocalPlayer.PlayerId`, gate B (`HostileItemPlacingPredicate`) compares
`SelectedCharacterUid`. There is a **third**. `InteractAgentObserver.cs:358` reads
`LocalPlayerInit.PlayerId` — the gate A identifier — and hands it to
`ShipPartVisualizer.IsShipPartInFriendlyShip`, which passes it straight to
`ShipVisualizer.IsShipOwner`, i.e. the gate **B** list. `"id"` is not a GUID, the
`Exists` misses, a container carries no `RespawnerVisualizer` to rescue it, and
the ship reads as hostile **to its own owner**.

**Why a ground loot chest opens and a ship trunk does not** is the whole
difference in one line: `ChestContainerLootPreprocessor` never runs
`ShipPartPreprocessor`, so a chest has **no `ShipPartVisualizer`** and `:360`
short-circuits to friendly. Bolting the same inventory to a ship is the only
change.

**Retail felt this too** — `ShipPartAttachedToOwnedShipCondition.cs:91` crosses
the same axes — but retail filled `4349 reviverInfosCache` by **registering a
personal reviver**, so almost no hull was ever "owned" and the branch almost never
ran. We fill it at build time, which is what made a latent inconsistency
load-bearing.

**Fixed** by seeding an owned hull's `8062`/`4349` list with **both**
identifiers. This cannot weaken gate B — `SelectedCharacterUid` is a real
per-player uid from BossaNet and never equals the `"id"` stub — and it changes
gate C from failing CLOSED (nobody, including the owner, may open anything) to
failing OPEN, which is where a shared 1086 identity leaves every PlayerId-keyed
gate anyway. When `feat/per-player-identity` (unmerged, `3fc1dcf`, flag-gated,
never soaked) lands, `PlayerId` becomes the character uid and the two entries
collapse; a test pins that they do not duplicate.

**THE SAME GATE WAS TAXING EVERY OTHER INTERACTION.** `flag2` also feeds
`:397-398`, `float time = flag2 ? interactTime + 10f : interactTime` — so the
sail, lamp, horn and sky core were all demanding a **ten-second hold** instead of
the instant toggle we serve. That is why the sky core showed a filling bar at all
(§13.10), and it is why "nothing happens" was a completely honest description:
the maintainer let go long before ten seconds.

**Generalisation, and it is the fifth failure shape this section has named:** a
component can be present, correctly typed, carry a renderable value AND satisfy
every `[Require]`, and still be refused by a client-side gate that compares a
field we author against a field we author **somewhere else**. The audit tool of
§11.7 checks `[Require]` sets and baked verbs; neither half sees this. The check
that would have caught it is *"for every client call site that compares against
something the server writes, which server field is it actually reading?"*

---

### 11.11 THE ALTIMETER — one root cause, five broken walks, and a missing part

**Reported in three rounds, and the third one is the important one:** *"the
altimeter can only go on the floor"* → (after the mask/tag patch) *"I can now
target the railing but I can't place it, it's blue"* → (after an 8066 redirect)
*"**it's red and it's not even the right direction. I feel like we are hacking
this together, which I don't like.** [The wiki] says we can put them on bar pipes
— don't even see how to craft that. I don't want to hack stuff I don't need to."*

**The maintainer was right, and this section is the correction.** Two independent
findings, and the first one is embarrassing.

#### 11.11.1 The Bar Pipe exists, and we never implemented it

`BarPipe_unityclient` (path_id **24983**) and `BarPipeBent_unityclient` (**39672**)
are baked into the shipped `resources.assets` with full ship-part component sets,
art meshes `bar_straight_LOD0..2` / `bar_bent_LOD0..2`, bundle keys
`entityprefabs/barpipe_unityclient`, and icons filed under **`ship parts/`** in the
client's own atlas (`docs/research/valid-icons.txt:873-874`). WIKI: *"structural
items that can be placed on a ship … used to attract lightning in a Stormwall or
to display Instruments."* **This is the fuel-tank mistake repeated**: an agent
searched for the thing it expected, did not find it, and built a workaround —
except here the thing was there under a name nobody had thought to search for.
**The wiki is what told us what to look for; the decompile cannot name a thing you
have not thought of.**

**The geometry proves the purpose.** The straight pipe is an inverted U: two
0.10 m capsules at x = ±0.189 rising to y = 1.90, joined by a crossbar whose
collider pad is **0.4375 × 0.1458 × 0.1125**, normal +Y. That 0.4375 is *exactly*
the Fuel Gauge's collider width and within 5 cm of all five instruments — a
mounting shelf, not structure. The bent variant rotates its upper section
**22.11°**, putting the pad normal at **|y| = 0.9265** — just inside the client's
own **≥ 0.9** flatness gate, whose limit is 25.84°. Authored hard against the
threshold: it is the *tilt your gauges toward the pilot* variant.

**How incomplete is our catalogue?** The client ships **98** `ship parts/` icons;
our table references **36**. Strip the wood/metal duplicates of parts we already
have and the genuinely missing functional structure is **bar pipe, bent bar pipe,
crow's nest** (plus the `Paint Can` / `Paint Drum` nodes, already used as hosts).
The recipe table never claimed to be retail's — the commit that created it says
*"the full recovered catalogue can be swapped in later untouched."*
**SHIPPED: the two bar pipes**, server-only, riding the two railing knowledge
nodes. The crow's nest is left for whoever wants it.

#### 11.11.2 The placement failure is ONE decision, not three gates

**Blue is a specific negative.** `PlayerScannerTool.cs:577`: green = `CanPlace`,
faint red = `!CanPlace && !_canDrop`, `DropHighlight` blue = `!CanPlace &&
_canDrop`. And `_canDrop` (`:524`) requires **`!flag4`**, where `flag4` (`:502`) is
`IsAttachedToShip(TargetObject)` → `GetComponentInParents<DockableVisualizer>()`.
So blue was runtime proof that the Unity parent walk failed. **Red was proof that
the redirect fixed that walk and the next one took over** — `flag4` flipped true,
`_canDrop` went false, and blue stopped being reachable. *The patch fixed the
thing it aimed at and moved the failure; both are true.* It also destroyed the
diagnostic: after it, the colour can no longer distinguish "no ship" from
"something else refused".

**The one decision underneath all of it:** we seed a mounted part
`Parent(hull, "~")`, and `RelativeParentTransformChildHierarchyBehaviour` treats
`"~"` as `SetNoParent()`. Only the DECK gets a real hierarchy key
(`BoltedPartTransform.HierarchyKeyFor` → `Deck.HierarchyKey`), which is exactly
why a deck works as a mounting surface and a railing does not. That single choice
breaks **five** separate client walks:

| # | walk | site | consequence |
|---|---|---|---|
| 1 | `DockableVisualizer` → `NeedToBeOnShip` | `PlacementPreview.cs:664`, `ShipPartPlacement.cs:132` | the **BLUE** preview |
| 2 | `DockableVisualizer` → `flag4`/`_canDrop`/`CanPlace` | `PlayerScannerTool.cs:502,516,524` | **cannot bolt down, at any attachment type** |
| 3 | `DockableVisualizer` → ownership | `ShipPartPlacement.cs:98` (`flag5`) | owner check silently unsatisfiable |
| 4 | `ShipVisualizer` → **retail's own instrument overlap exemption** | `ShipInstrument.cs:10` → `ShipPartVisualizer.cs:131` | the **RED** preview |
| 5 | `HasParentEntity` at commit | `ShipPartPlacement.cs:213` | green preview, click does nothing |

Plus a sixth, silent one worth its own line: the exclusion-radius test
(`ShipPartPlacement.cs:153`) is `ship.GetComponentsInChildren<…>()`, so **our
server has no exclusion-radius enforcement for any mounted part at all.**

#### 11.11.3 What retail actually authored, and why we cannot flip to it yet

`acs/ShipInstrument.cs` is four lines and it settles the design question:

```csharp
private void Awake() {
    PlacementRules r = gameObject.GetOrAddComponent<PlacementRules>();
    r.IgnoreOverlap(entity => ShipPartVisualizer.AttachedShip(entity));
}
```

An overlap **exemption** for anything attached to a ship, attached to the five
instruments and to nothing else. Bossa wrote the permission to clip a gauge onto
a bolted-down part; we never needed to invent it. Same shape of proof as the
`BlockItemPlacement` opt-out in §11.6.

So the instruments' retail `attachmentType` was almost certainly **`shipSurfaces`**,
and every symptom falls out of that one string:

- `GetMask` → `Layers.Environment`, `GetTag` → **empty**. Hits a railing or a bar
  pipe (layer 0 `Default`, `Untagged`) with **no client patch**. *That is patch
  (a) and (b), for free.*
- `IsClipping` → `case ShipSurfaces: return false` — **overlap never blocks**.
  *That is the RED, gone, and our `CanOverlapWith` hook made unnecessary.*
- Pose comes from `PositionOnShip`'s surface branch
  (`Quaternion.LookRotation(forward, hitNormal)`) instead of `PlacingOnDeck`'s
  `LookRotation(ship.forward, ±Vector3.up)`, which **throws the hit normal away**.
  *That is the wrong angle, gone* — and it is why the gauge stood bolt upright
  facing the sky on a horizontal rail: instrument socket up = dial normal
  (`PlacementSocket` on each instrument's `Pivot`, socket −Y into the surface).

> **⚠ AND IT MUST NOT BE FLIPPED YET.** `flag4` (walk 2 above) is **not gated on
> the attachment type**. A `shipSurfaces` instrument aimed at a bar pipe would
> raycast correctly, pose correctly and pass every overlap rule — and then fail
> `CanPlace`, leaving a beautiful, correctly-oriented **blue** phantom that
> free-drops as a loose item. Meanwhile the deck is lost, because the
> `ShipSurfaces` mask excludes `ShipAttachmentSolid`. **Net effect: nowhere left
> to put an instrument at all** — strictly worse than today. `ShipInstruments
> .MountSurface` therefore still reads `"deck"`, with the precondition written
> next to it.

#### 11.11.4 THE BLOCKER, verified from source: `flag4`

**NEW FACT, and it outranks every inference here: the maintainer has SEEN players
mount instruments on pipes in the live retail game.** So "can they" is settled —
yes. And a peer audit settled the other half: `attachmentType` is a STRING on
`ShipPartState` with nine legal values (`none, side, deck, deckGrid, deckForward,
engine, wing, shipSurfaces, coreModule`), and searching all six asset containers
plus `StreamingAssets/GameDB` for those literals returns **zero hits**. The
authored per-part values lived in Improbable's server-side templates and are
**unrecoverable**. Picking one is therefore not inventing retail behaviour — it is
supplying a value retail also supplied. There is no fidelity risk in choosing; only
a correctness risk in choosing one that does not work.

**And `shipSurfaces` alone does not work.** Read directly off
`PlayerScannerTool.UpdatePlacementMode`, not inferred:

```csharp
bool flag3 = flag2 && Preview.IsPlacing && IsWithinShipyardRange(Preview);   // :501
bool flag4 = ShipPartPlacement.IsAttachedToShip(Preview.TargetObject);        // :502
...
if (!flag && IsLiftingObject && flag3 && flag4 && !flag11 && !flag9)          // :516
    _canPlaceFrames++;  else _canPlaceFrames = 0;
_canDrop = !flag && IsLiftingObject && flag3 && !flag4 && (...);              // :524
```

`CanPlace` needs **`flag4`**; `_canDrop` needs **`!flag4`**; and `flag4` is
`GetComponentInParents<DockableVisualizer>()` on the target — **not gated on the
attachment type, not gated on anything we author**. Change `attachmentType` to
whatever you like and this line does not move. A `shipSurfaces` instrument aimed
at a bar pipe would raycast correctly, pose correctly and pass every overlap rule,
then fail `CanPlace` and free-drop — while ALSO losing the deck, because the
`ShipSurfaces` mask excludes `ShipAttachmentSolid`. **Nowhere left to put an
instrument.** `ShipInstruments.MountSurface` therefore stays `"deck"`.

(Worth noting from the same read: `flag3` gates BOTH branches on
`IsPlayerInsideShipyard() && IsShipyardActive() && IsWithinShipyardRange`. All
ship-part placement is a shipyard activity in retail.)

**So retail's pipes worked because retail's mounted parts were real Unity children
of the ship.** Ours are not, and that is the whole difference.

---

#### PHASE SC5 — Mounted parts join the ship's transform hierarchy *(BAR PIPES + AUTHENTIC INSTRUMENT SURFACE IMPLEMENTED; AWAITING DEPLOYMENT/LIVE PLACEMENT)*

> **STATUS 2026-08-23.** The bar-pipe hierarchy half is implemented, gated and soaked,
> and production persistence now proves a player successfully mounted a `barPipe` on
> built ship 4. The follow-on flips all five instruments from `deck` to the recovered
> `shipSurfaces` path and fixes the item-type classifier that previously rewrote that
> value back to `deck`. Existing persisted instruments migrate through
> `LoosePartDefinition` on restore; no world-state rewrite and no client patch are
> required. `Multiplayer/Ship/MountedPartHierarchy.cs`
> is the one per-part decision, and all three mounted-part transform sites read it:
> the checkout seed (`ComponentsSerializer`), the mount commit (`PartMountService`)
> and the in-flight republish (`ShipFlightService`). The fourth listed site,
> `ShipPartMotionService`, needed **no change** — its loop is
> `WorldEntityRegistry.BoltedParts()`, which returns only the four STATIC hull keys
> (`WorldEntities.IsBoltedPartKey`), so a crafted mount never enters it. Risk 1 —
> the wake churn — therefore lives entirely in `ShipFlightService`, where the
> Unity-child skip now is.
>
> **What is NOT settled, and cannot be settled offline:** the two prefab-baked
> `TransformNature` flags on `BarPipe` / `BarPipeBent`
> (`GameObjectCanBeParented`, `ShouldRemoveRigidbodyOnParented`). They fail safe —
> an unparentable prefab ignores the key and behaves exactly as today — which also
> means the server cannot tell whether they held. Only a player crafting a pipe,
> bolting it to a ship and trying to mount a gauge on it answers that.
> `ShipInstruments.MountSurface` is now `"shipSurfaces"`: it masks
> `Layers.Environment`, hits the mounted pipe/railing colliders, uses their hit normal
> for the authentic dial pose, and intentionally no longer treats the bare
> `ShipAttachmentSolid` deck as an instrument stand.
>
> **Soak: four 10-minute runs on port 7807, three FLAT at 100% delivery and 0%
> missed ticks (p50 0.25–0.3 ms), one ABORTED on a transport disconnect of botB at
> t=125 s** — not a level failure and not the known 50 ms relay state either; a
> re-run of the same tree came back FLAT. **Say what it does and does not prove**:
> the `haven-spawn` soak world contains no ship at all, so it gates the changed
> assembly against the standing relay-regression rule and nothing more. It does
> NOT exercise `BuildHullAndPartWakes`, which is the code this change actually
> touches. Only a player flying a ship with a bolted pipe tests that.

- **Delivers:** a crafted mounted part is seeded a REAL 190602 hierarchy key
  instead of `BoltedPartTransform.RelativeSlotKey` (`"~"`), so the client
  re-parents it under the hull instead of merely position-following it.
- **Player can newly do:** mount a gauge on a bar pipe or a railing — and, as
  free consequences, get ownership checks that resolve, exclusion radii that are
  actually enforced, and retail's own instrument overlap exemption working by
  itself. **All five broken walks in §11.11.2 close at once**, and
  `ShipInstruments.MountSurface` can then become `"shipSurfaces"` in the same
  breath, which is what supplies the correct pose.
- **The mechanism is already PROVEN on this server.** `Deck.HierarchyKey` does
  exactly this for the walkable deck and is live-confirmed (it is the carry fix);
  `Deck.cs:55-110` documents the client chain end to end, verified line by line.
- **Scope is four call sites and one filter:**
  1. `ComponentsSerializer.cs:402` — the checkout seed for a mounted crafted part,
     currently a hardcoded `RelativeSlotKey`.
  2. `PartMountService.cs:323` — the mount-commit broadcast.
  3. `ShipFlightService.cs:1002` — the in-flight re-publish.
  4. `ShipPartMotionService.cs:143,165` — the wake heartbeat, which already
     filters on `BoltedPartTransform.IsUnityChild` but is keyed on the STATIC
     hull's part keys, not on crafted mounts.
  Plus one new per-part decision, mirroring `HierarchyKeyFor`, so the seed and the
  wake filter can never disagree about which parts are real children.
- **Migration:** no. **CLIENT MOD:** **no** — this is the point. One server-side
  decision replaces three Harmony hooks.
- **SOAK: YES, mandatory.** It changes how every mounted part is transformed on a
  moving ship with two players aboard. That is precisely the class the standing
  multiplayer-safety rule names.
- **Main risks, all stated rather than discovered later:**
  * **The wake heartbeat MUST exclude the newly-parented parts** or the client
    churns an unparent+reparent — rigidbody destroyed and re-added — twice a
    second. `BoltedPartTransform.IsUnityChild` exists for exactly this and its doc
    comment says so.
  * **A real parent DESTROYS the part's client-side rigidbody**
    (`TransformManageRigidbodyBehaviour`, verified). Fine and desirable for inert
    structure; `BoltedPartTransform` explicitly says the helm, engine and sail
    "must keep their own rigidbody", so this cannot be applied blanket. Start with
    structure and instruments.
  * **Two prefab-baked assumptions are invisible offline** and only a live client
    settles them: each part's authored `TransformNature` needs
    `GameObjectCanBeParented = true`, and `ShouldRemoveRigidbodyOnParented = true`.
    They held for `Deck01`. If they do not hold for a given part, the key is
    ignored and that part stays exactly as it is today — it fails SAFE.
  * It touches parts already bolted to ships in the live world. **The
    lowest-risk first step is the two BAR PIPES ONLY**, because no bar pipe exists
    in any player's world yet, so step one cannot regress a single existing ship;
    then flip the instruments once a live craft confirms a pipe rides the hull
    without jitter.
- **Why it was NOT done on `fix/ship-interactions`:** it is a transform-model
  change wanting its own soak, its own live verification and its own rollback,
  and it arrived at the end of a branch that had already shipped four unrelated
  fixes. Bundling it would have made all five unshippable together.

### 11.12 The component-init errors in the live log, and why they are NOT §11.2

**Added 2026-08-19, branch `fix/component-init`.** The live game server had been
printing `[error] failed to initialize component NNNN of entity NN` continuously
since at least **2026-08-09** — the oldest day the journal still holds — and
nobody had looked, because the only post-deploy check anyone ran was a two-minute
window after a restart. That window is clean by construction: the server spawns
nothing on its own, so the errors only exist while somebody is playing.

**It is not a §11.2 defect, and this is the important part.** The failures land on
entities that are *not* in the 37-row table:

| id | what it is | who asks | verdict |
|---|---|---|---|
| **1013** `CraftableSpawningState` | the materialize-dissolve state | the built **hull** and every one of its **deck** sub-entities | **genuine missing seed — FIXED.** The branch served only `LooseParts`; it now falls back to `CraftableSpawnPolicy.Done` for everything else |
| **1120** `ShipPartState` | "I am a liftable ship part" | built **decks** | **deliberately absent, per entity.** Serving it enables `ShipPartVisualizer`, and its mere presence *is* the client's lift whitelist (`PlayerScannerTool.cs:508-511`) — a player in an active shipyard could pick their own ship's deck up off it. `attached=true` is no defence; nothing in the pickup path reads it and `CanPickUp` has no callers anywhere in the client |
| **8066** `ShipRootState` | which ship a part belongs to | built **decks** | **deliberately absent, with 1120.** Its only reader on a deck is the same `ShipPartVisualizer` that 1120's absence keeps disabled |
| **1259** `ReclaimableState` | the hull's dissolve-to-materials countdown | every **hull** checkout | **deliberately absent.** Serving it is actively dangerous: `ShipReclaimVisualizer` escapes only on a negative value, and otherwise dissolves the hull and `DisableBeamsColliders` — players fall through their own deck |
| **1304** `PhysicsHingesState` | hinge swivel angles | every crafted **sail** | **deliberately absent.** We run no hinge physics; the visualizer only slerps transforms |
| **4323** `ContactFixedDamageState` | the jellyfish shock | every **jelly** | **deliberately absent.** The reader is purely event-driven and nothing here ever raises the event |

**What it cost a player: nothing.** Every one of those requests arrives on a
**best-effort** batch, and there is not a single `[error] DROPPING the whole
AddComponent batch` line anywhere in the journal since 2026-08-08. No transform
was lost, nothing is at the world origin, and the deck a player walks on renders
through `ShipDeckVisualizer` (1518 + 1099), which needs none of the three. The
`failOnComponentInitError` trap §4.4 warns about is real and did **not** fire.

**It does not explain "some components don't show up".** Correlated against the
live journal minute by minute: the 1013/1120/8066 burst happens in one clump at
**login**, while the client streams the whole built ship in. Every part MOUNT in
the same session — `22:43:04`, `22:45:09`, `22:47:19` — completed with **zero**
component-init errors. §11.3's Window mesh diagnosis stands, and the
altimeter-on-a-railing symptom is §11.6's placement mask, not a missing 1120.

**The diagnostic itself was the real bug.** `outcome` starts as `NoClientVtable`
and was only overwritten when a branch produced bytes, so a branch that ran and
declined returned `NoClientVtable` — whose own log line reads *"the component does
not exist in the shipped client, so no branch here can fix it"*. For eleven days
the server told its log to stop looking, about ids the client had just asked for
by number. That is now a fifth outcome, `NoSeedForEntity`, and every failing
outcome prints its own repair instruction on the same line.

**And the check that missed it is now written down**: `tools/check-game-server.sh`
counts errors **per interest batch**, so a window with no players is
`INCONCLUSIVE` and never `PASS`, and compares the ids against a committed ledger
(`tools/game-server-error-baseline.txt`) so a **new** id fails at count one.

---

## 12. SHIP FLIGHT — THE PHYSICS AUDIT

**Added:** 2026-08-19, branch `feat/ship-flight`, cut from `main`.
**Why:** *"we need to know how much thrust the engines are doing, how they affect
the ship, under what situation — also the atlas generator, all that stuff"*, and
*"the weight of the ship [affects] what it can do and how much speed it gets… I
think our current thing is just faking all this."*

This section is that audit. It is a different subject from §11: that one is about
whether a ship part **renders and can be interacted with**, this one is about
whether it **does anything to the ship's motion**. A part can be perfect in §11's
table and physically inert here — and 33 of the 37 are.

### 12.1 The recovery, and why it went better than expected

The standing assumption in this repo — correctly, for everything else — is that
*the client REPLAYS motion and does not simulate it*, so the flight model is ours
to invent. The first half is true and load-bearing: `ShipPhysicalityVisualizer`
hardcodes `ClientDynamic() => false`, the ship rigidbody is permanently kinematic
on a player's machine, and motion arrives as `1130` control points.

**But the physics worker was a Unity worker, and it shipped the same
`Assembly-CSharp` as the client.** The split is one runtime boolean —
`WorldsAdrift.IsFSIM` — plus `[WorkerType(WorkerPlatform.UnityWorker)]`
attributes. Every force-producing class is therefore sitting in the decompile,
compiled and readable. What did *not* ship is the per-part **data** those
equations consumed, which the Scala GSim computed and sent over the wire.

So the split is unusually clean, and this section labels it everywhere:

- **The equations and the world constants are PROVED.** Drag, thrust, sail
  force, lift, steering torque, the wind fallback, the caps.
- **The per-part magnitudes are LOST.** Engine power, sail power, per-part mass
  and per-core lift were `1116` / `1303` / `1121` / `1258` values authored
  server-side. §3 already lists "quality → stat" as unrecoverable; this is the
  same hole seen from the ship's end.

Two caveats on the constants, stated once and applying throughout. First, they
are the **IL defaults** of `ScriptableObject` classes; the shipped
`Resources/Configs/ShipConfig.asset` may override them, and extracting it from
the bundles is unclaimed work. Second, both `ShipConfiguration` and
`EndOfTheWorldConfig` implement `RemoteConfigurationUpdater.IConfig`, so Bossa
could push new values as SpatialOS worker flags at runtime and we would have no
record of it.

### 12.2 THE WORLD CONSTANTS — the numbers everything else hangs off

All **PROVED** from `acs/ShipConfiguration.cs` unless noted. The drag pair is
stronger: **RECOVERED from the serialized shipped ShipConfig**, which overrides
those two field initializers (see `findings-storm-walls.md` §2.4).

| constant | value | what it decides |
|---|---|---|
| `AirResistanceCoefficient` | **`0.007`** | serialized shipped `ShipConfig`; overrides the decompiled 0.01 initializer |
| `AirResistanceExponent` | **`2.5`** | serialized shipped `ShipConfig`; top speed goes as (thrust/mass)^0.4 |
| `ShipThrustMultiplier` | `1.0` | global thrust lever, shipped centred |
| `AirBrakeMultiplier` | `1.0` | global airbrake lever |
| `MaxWingPowerSpeed` | `10 m/s` | speed at which wings reach full control authority |
| `SendInterval` | `0.24 s` | the `1130` control-point cadence we already match |
| `_liftSpeedCap` | `2 m/s` | **max climb/descent rate, any ship** (`ShipControlVisualizer`) |
| `_liftAccelerationCap` | `1 m/s²` | max vertical acceleration (`ShipControlVisualizer`) |
| torque deadband | `2500`, then `×0.5` | accumulated off-centre torque below 2500 does **nothing** (`ShipMotionVisualizer.LateUpdate`) |
| `MinEfficiency` | `0.3` | a badly trimmed sail still gives 30% (`SailBehaviour`) |
| default wind | `(1, 0, -2)`, ‖w‖=2.236 m/s | the client's fallback where no weather cell exists (`GlobalWeather`) |
| wind magnitude clamp | `100 m/s` | above this the field returns zero |
| altitude pushback onset | `Y = 800 m` | `WorldEdgePushback`, `WallData.WorldMaxY` |
| hard altitude clamp | `Y = 1000 m` | `WorldEdgePushback` |
| physics tick | `50 Hz` (`fixedDeltaTime 0.02`) | `FSimStateMachine/StartupState` |

**Client-side value ranges**, useful because they tell us what speeds Bossa
expected players to see. All **PROVED**:

| gauge | value |
|---|---|
| airspeed indicator full scale | **70 knots = 36.0 m/s** |
| helm wind VFX at full intensity ("fast") | 30 kn = **15.4 m/s** |
| helm wind VFX onset | 5 kn = 2.6 m/s |
| ship mass tiers (ambient SFX) | ≤500 / ≤1000 / ≤2500 / ≤4000 / >4000 kg |
| max designable hull | 6 × 2 m long, 4 × 1.7 m high, 16 m wide |

That gauge is the single best answer to "what should a ship's speed be": Bossa
built a dial to 70 knots and put the "this ship is fast" threshold at 30. Our
flat 12 m/s is 23 knots — inside the band, at the slow end.

### 12.3 THE PER-COMPONENT TABLE

For each: what force it makes, how much, when it applies, and how it combines.

---

#### 1. ENGINE — `1116 ShipEngineState`

- **Contributes:** a **force** in newtons along the engine's own forward axis.
- **How much — PROVED** (`EngineVisualizer.cs:272`):
  `F = ShipThrustMultiplier × spin × (boost + Power) × transform.forward`
  where `spin` chases `CurrentPercentSpin` at `6/s` (a ~0.17 s spool-up), and
  `boost` is added **only** while `IsBoosting`.
- **Scales with:** `Power`, a **server-sent** number. `1116` also carries
  `consumption`, `overheat_limit`, `spinup`, `heat_efficiency`, `boost`.
- **When:** only inside `if (WorldsAdrift.IsFSIM)`. Throttle enters *indirectly*,
  through `CurrentPercentSpin` — an engine does not respond instantly.
- **Combines:** purely **additive** across engines, applied at the propeller's
  world position, so an off-centre engine also yaws the ship. There is no cap and
  no diminishing return in the force itself — the diminishing return is drag.
- **LOST:** `Power` itself. Community work reconstructs it as
  `basePower × (1 + combustionBoost + propellerBoost)` where base power comes
  from the engine's head part (Rustbucket −1 … Starcaster 60) and the two boosts
  are per-material, per-quality and additive — **WIKI**, and specifically a
  *fitted* table, not a datamine (see §12.7).
- **Ours today:** WAREBORN TUNING, 1400 N per mounted engine, flat. This
  preserves the 12 m/s reference speed under the recovered serialized drag.

#### 2. SAIL — `1303 SailState { unfurled: bool, power: float }`

- **Contributes:** a **force** from the wind. Not a bonus, not a multiplier.
- **How much — PROVED** (`SailBehaviour.cs:44-54`): the boom trims to
  `LookRotation(forward×1.01 − ŵ)` flattened horizontal; then
  `efficiency = |dot(ŵ, joint.right)|`, `F = efficiency × ‖wind‖ × Power` along
  `joint.right`, sign-flipped so it always carries downwind, with a floor of
  `0.3 × ‖wind‖ × Power`. `ShipMotionVisualizer.AddSailForce` then **strips the
  component along the hull's right axis** — retail's implicit keel — leaving
  drive along the hull.
- **When:** **whenever `Unfurled` is true.** There is no velocity term and no
  throttle term anywhere in the sail path.
  **So yes: in retail, unfurled sails moved a stationary ship.** The
  maintainer's recollection is correct and is now PROVED, not remembered.
- **Combines:** one force per unfurled sail, additive, at the sail's position.
- **The schema is tiny and settles several questions by absence:** `unfurled` and
  `power`, nothing else. **No furl percentage, no canvas area, no rigging, no
  tacking state.** Unfurling is a binary toggle.
- **LOST:** `Power`. **Ours today:** WAREBORN TUNING, 30.

#### 3. WING — `1124 WingState`

- **Contributes:** **steering torque**, plus **airbrake drag**. Not lift.
- **How much — PROVED** (`WingVisualizer.UpdateMotion`):
  `p = InverseLerp(0, MaxWingPowerSpeed=10, ‖v‖) × Power`, then
  `torque = (axes.X·p·k_pitch, axes.Y·p·k_yaw, −axes.Z·p·k_roll)` where each
  `k` is `Lerp(0.2, 1.0, alignment)` of the wing's own up-vector against that
  hull axis — **a wing mounted flat rolls well, one mounted vertically yaws
  well**.
- **When:** **scales from zero at rest to full at 10 m/s.** A wing is an
  aerodynamic control surface and does nothing on a parked ship.
- **Airbrake:** when `dot(throttle × forward, velocity) < 0` — i.e. the pilot is
  commanding *against* travel — it adds `AirBrakeMultiplier × AirBrake × −v`.
- **Combines:** additive, and applied through `AddTorque`, which **bypasses the
  2500 deadband**.
- **Dead fields:** `_velocityDependantPowerFactor 0.6` and `_velocityForMaxPower
  50` in `WingTorqueData` have no consumer; `MaxWingPowerSpeed = 10` superseded
  them. Do not implement them.

#### 4. THE ATLAS SKY CORE — `1258 ShipLiftState`, `1115 ShipCoreState`

The maintainer called it "the atlas generator"; the game's own string is
*"Ship weighs more than its atlas sky core can lift."* (VERIFIED — the literal
at `acs/ShipControlsBehaviour.cs:283`.)

- **Contributes:** **lift capacity**, expressed as a mass budget in **kilograms**.
  It is not an altitude ceiling and it consumes nothing.
- **How much — PROVED** (`ShipLiftVisualizer`):
  `TotalLift = AtlasMultiplier × ShipLiftState.totalLift`;
  `IsOverloaded = totalMass > TotalLift`; `Load = totalMass / max(1, TotalLift)`.
- **How it flies the ship — PROVED** (`ShipControlVisualizer.UpdateFloating`):
  ```
  lift = −(mass × g) + compensationForce + commandedLift
  applied = clamp(lift, 0, TotalLift × |g|)
  ```
  So the core **exactly cancels the ship's weight** and then adds the pilot's
  vertical command. This is the whole reason ships hang in the sky: it is
  anti-gravity, not aerodynamic lift, which is why a ship holds altitude with no
  airspeed at all.
- **The overload rule falls straight out of that clamp.** If
  `TotalLift < mass`, the clamp cannot even reach the weight term and the ship
  **cannot hold altitude**. The client additionally refuses to send positive
  vertical input while overloaded. **This is the mechanism by which ship weight
  matters most, and it is the one we do not implement.**
- **Vertical is capped hard**: ±2 m/s and 1 m/s², for every ship, regardless of
  core. A bigger core does not climb faster — it lifts *more*.
- **Combines:** `8067 ShipPartAccumulateState` rolls per-part `lift` up the
  parent chain; the root publishes the sum as `1258.totalLift`. Additive.
- **Abandoned ships sink** at −0.05 m/s² (`ShipAbandonedBehaviour`, after a
  `CoreDampenTime` accumulator passes 86400).
- **LOST:** per-core lift. `MaterialCatalog.SkyCoreLiftKg` in this repo already
  RECOVERS the shape from the wiki's Atlas Core table —
  `lift = 1000 + rate × (10 + quality)`, reproducing twelve metals at both
  endpoints — and **has zero non-test callers.**

> **⚠ A LANDMINE, and the single most surprising thing in this audit.**
> `AtlasMultiplier` is not a tuning value. It is
> `EndOfTheWorldConfig`'s **doomsday clock**: it decays to zero across Bossa's
> shutdown window (`OutroDate` 2019-07-26 → `ApocalypseDate` +20 h). It is how
> Bossa ended the world — every ship lost lift and fell.
> **Evaluated at today's date it is `0.0`.** On an unpatched client every ship
> therefore reads `TotalLift = 0` and permanently overloaded, and the handheld
> **Atlas Lifter applies literally zero force** (`LiftableVisualizer.cs:53`
> multiplies by it) — which is very likely the real reason §4.5 records the
> lifter as "a prop". Any feature that reads client-side lift must force this to
> 1 first, and that is a **CLIENT MOD**.
>
> **UPDATE 2026-08-20 — that client mod already exists and has been shipped for
> a week.** `WorldsAdriftReborn/Patching/Flight/EndOfTheWorld_Patch.cs` is a
> Harmony prefix on the `AtlasMultiplier` getter that returns `1f` and skips the
> countdown. It landed in `a44aebb` on **2026-08-13** — *six days before this
> section was written* — after the live "can't go up and down" report, and it is
> present in the installed plugin DLL. The audit above searched the decompile and
> not our own patch set, which is the third time in two days a finding has been
> wrong because the search never left the decompile.
>
> So the standing conclusion inverts: **client-side lift is live and correct**,
> `TotalLift = 1.0 × whatever we serve`, and the reason ships climb today is that
> we serve `1258 = 1,000,000 kg` against hulls of a few hundred. The Atlas Lifter
> being a prop needs a different explanation than this one.

#### 5. HULL MATERIALS → MASS — `1257 ParentingMassAdderState`

- **Contributes:** mass, which divides every force in the game.
- **PROVED:** `ParentingMassAdderVisualizer` does `_rb.mass = _massState.Mass`
  verbatim — no scaling, no clamp. `8067` sums per-part `1121 OriginalMassState`
  up the tree; the root publishes the total.
- **A real trap:** mass is pushed only on the `MassUpdated` **event**, never in
  `Awake`. A value sent once at spawn and never updated leaves the rigidbody at
  Unity's default mass of **1.0 kg**.
- `centerOfMass` and `inertiaTensor` are **never assigned** — PhysX derives both
  from the colliders. So a ship's rotational inertia comes from its *shape*, and
  is not something the server ever set.
- **Ours today:** `HullMassCalculator` — per-material kg/unit **RECOVERED** from
  the wiki's Metal/Wood tables, quality explicitly free
  (*"without any additional cost of weight"*), and `UnitsPerHullCell = 2000` /
  `UnitsPerDeck = 500` / `MetalShareOfMixedHull = 0.20` **CHOSEN** and labelled.
- **⚠ CORRECTED 2026-08-20 — the corroboration claim that used to sit here was
  pointed at the wrong table.** It read: *"Corroborated independently: the
  community panel-mass table divides by exactly 40 to give the same per-material
  kg/unit figures, 20 rows out of 20."* The ÷40 relation is real and exact, but
  what it corroborates is that the **community "Large Panel Kg" column and the
  WAEngenius WEIGHT column are the same table ×40** — i.e. they are *not*
  independent of each other. That community table is **not the table we ship**;
  it differs from ours on **14 of 16** shared materials (cedar 0.15 vs our 0.13,
  tungsten 0.80 vs our 0.70, and so on). Citing it as corroboration of our
  numbers was double-counting one source *and* aiming it at the wrong epoch.
- **What our table actually is corroborated by**, verified row-for-row this
  session: the wiki Metal/Wood pages **and** the `weight` column of
  `sciencesheet.xls` (Worlds-Adrift-Engine-Science repo) agree on **all 15
  metals**, including orthite, epilar and eternium, which no community weight
  table carries at all. See `docs/research/findings-material-mass.md` §2.7 and
  the epoch guard in
  `WorldsAdriftRebornGameServer.Multiplayer/Materials/MaterialMassEpoch.cs`.

#### 6. WIND AND DRAG — the world, not a component

- **Self-drag — PROVED** (`WindPhysicsVisualizer.GetDrag`): deceleration is
  `0.007 × ‖v_rel‖^2.5`, plus a residual term capped at `0.03 m/s²` pulling the ship
  toward the local wind velocity. Drag is computed as an **acceleration** and
  only then multiplied by mass, **so mass cancels** — this is exactly why top
  speed depends on thrust-to-weight and not on mass alone.
- **Wind push is mass-attenuated — PROVED** (`ApplyDrag`):
  `windMultiplier = 1 − clamp01(mass/4000) × 0.75`. A 4000 kg ship feels only
  25% of the wind. **Heavy ships are shoved around less by weather but coast
  identically.**
- **Wind pushes stationary airborne ships even with no sails** — the early-return
  is skipped for any ship with working lift.
- Forces under 1 N are discarded. Wind is resampled every ~0.2 s.

#### 7. THE THINGS THAT DO NOT EXIST

The brief asked about rudders, keels and ballast. **They are not components and
never were.** Stated plainly so nobody looks for them again:

- **No rudder.** Steering is wing torque plus core torque, below.
- **No keel.** The keel is *implicit*, in two places: `AddSailForce` strips the
  hull-lateral component of sail force, and `SelfRighteningVisualizer` applies a
  `±2 × mass` couple that rights the hull — and sets `angularDrag = 1`, the only
  angular damping any ship has. `Rigidbody.drag` is never set on a ship at all.
- **No ballast.** Mass is hull materials plus mounted parts. There is no
  component that adds mass deliberately.
- **`1110 ReactionWheelState`** exists, has a `power` vector, and has **zero
  consumers anywhere** — dead legacy.
- **`1137` / `1138` atlas anchor** components are **field-less flags**.
- **`8068` / `8069`** deprecated rigidbody components are genuinely empty — there
  is no older physics dataset hiding in them.

#### 8. THE HELM — `1111 ShipControlInput` → `1113 ShipControlState`

Not a force producer, but it is where mass re-enters. **PROVED**
(`ShipControlVisualizer.UpdateTorques`):
```
torque = ShipAxes × CorePowerScale(0.5, 1.0, 0.5) × mass^CoreMassExponentialFactor
```
with the exponent **`1.0`**. Torque scales **linearly with mass**, so angular
*acceleration* is mass-invariant: **a heavy ship turns just as briskly as a light
one.** That is a deliberate design choice, and it means "heavy ships are less
manoeuvrable" — which our own code comments assert — is **false in retail**. Yaw
authority is twice pitch and roll. This torque bypasses the 2500 deadband.

`1111` is the **only** component the client ever writes: three axes plus vertical
plus throttle, at 20 Hz, all clamped to [−1, 1].

### 12.4 THE WHOLE-SHIP ANSWERS

**How mass becomes speed.** At equilibrium thrust balances drag:
`F/m = 0.007 v^2.5`, so

> **v_top = [thrust / (0.007 × mass)]^0.4**

That is the entire speed model, and it is RECOVERED rather than chosen. Its
consequences are worth stating because they are counter-intuitive and they are
what a ship-builder actually experiences:

- **Doubling your engines buys 1.32× top speed**, not 2×.
- **Doubling your mass costs 0.76× top speed**, not half.
- Mass and thrust matter **only as a ratio**. This is why every retail guide says
  power-to-weight is the only statistic that counts.

The one published community speed model, WAEngenius's
`speed = 50 × √(2 × power / weight)`, is **WIKI and weak** — a UI heuristic with
no validating measurement in the archive. It independently confirms diminishing
returns and power-to-weight as the controlling ratio, but its square-root exponent
is not the shipped asset's 0.4 and must not replace it.

**Is there a speed cap?** **No — retail set none anywhere.** Top speed is purely
where drag balances thrust. Our 60 m/s `ShipMotionPolicy` clamp is a **wire**
constraint, not physics: above it a hull moves far enough between two 0.24 s
control points to read as teleporting and the client's spline correction fights
it.

**No sails versus sails unfurled.** In retail, sails are an always-on wind force
independent of the throttle, so: a ship with no sails is slower; a ship with
sails unfurled is faster; and a stationary ship with sails unfurled **starts
moving**. All three of the maintainer's expectations are correct.

**What decides climb rate?** Not the core, and not mass. `±2 m/s` and `1 m/s²`,
flat, for every ship. The core decides *whether you can climb at all*.

### 12.5 THE VERDICT ON OUR MODEL

The suspicion was *"our current thing is just faking all this"*. Judged
component by component, that is **substantially correct for propulsion and
wrong about mass**, and the code deserves credit for never having claimed
otherwise — `FlightTuning.cs` opens with a "HONESTY NOTE, load-bearing" that
says its numbers are guesses.

**What was genuinely real, before this branch:**

- **Hull mass.** Real materials × real decoded cell and deck counts, with
  per-material densities RECOVERED from retail's own published tables and
  independently corroborated. This is good work and is not faked.
- **The sail ledger.** Real mount/interact/persist wiring, read live every tick.
- **Axis signs and the input mapping**, recovered from the client.
- **The `0.24 s` cadence and the `1130` wire shape**, verified against
  `ShipConfiguration.SendInterval`.

**What was faked:**

- **Top speed was a constant: 12 m/s for every ship ever built.** Mass did not
  affect it — deliberately, and the code said so. This is the single largest
  divergence, because in retail top speed is the *only* thing mass and thrust
  produce.
- **Thrust was a constant: 4 m/s², flat.** **Engines were never consulted.** A
  ship with eight engines and a ship with none flew identically. Engines were
  classified for *mounting* and then ignored.
- **Sails were a throttle multiplier, so a rigged ship with the lever centred sat
  motionless** — the exact opposite of retail, where the wind does not care about
  the throttle.
- **Lift and altitude did not exist.** No ceiling, no floor, no overload. Worse,
  `1258` is seeded at a flat **1,000,000 kg** for every hull, so the overload
  rule the mass model was explicitly built to feed **can never fire**. The mass
  calculator's own worked example — *"nobody should be able to fly a solid-gold
  hull on a stock core"* — is false in the running server.
- **`MaterialCatalog.SkyCoreLiftKg`** is a properly recovered formula with **zero
  non-test callers**.
- **Turn rate was a constant**, and mass touched only its ~0.8 s ramp. Ironically
  retail also made turning mass-invariant — so this was accidentally right, for
  the wrong reason, and our comments describe the wrong behaviour.

**One correction to the record.** §2.6 lists *Sails* as PARTIAL and blocked on
weather, and `HANDOVER.md` §10 repeats it. **The weather dependency was
overstated.** Retail's sail force needs a wind *vector*, and the client supplies
a constant one — `(1,0,-2)` — everywhere no weather cell exists, which on this
server is everywhere. A faithful sail model was always reachable; only a
*varying* wind field needs weather.

### 12.6 WHAT IS GENUINELY IMPOSSIBLE WITHOUT WEATHER

> **⚠ CORRECTED 2026-08-20 by the WIND pass (branch `feat/wind`). Read §12.6a
> below BEFORE this subsection: it is still too pessimistic in two places and
> the framing of the whole question turned out to be wrong.** Item 3 is simply
> false — wind walls need no weather cell at all — and item 1's premise, that a
> varying wind field is the retail behaviour we are missing, does not survive
> the shipped `WeatherCell` blueprint.

Distinguished carefully, because the previous answer was too pessimistic.

**Reachable with no weather at all** (the client already assumes a uniform wind):
sail force and its direction-dependence; the efficiency floor; the keel; drag;
wind-driven drift; every engine, mass, lift and torque behaviour above. **None of
this needs `1139`.**

**Genuinely blocked on weather:**

1. **Wind that varies by place or time.** `GlobalWeather` interpolates between
   four `1139 WeatherCellState` neighbours. With no cells every position returns
   the same constant, so sailing strategy can never become *local* — there is no
   "find the good wind", and a route can never be better than another route.
2. **Gusts, pressure and turbulence as gameplay.** `Pressure` is pinned at 0.5;
   `GetTurbulenceAt` is `‖wind‖/100`, so it is constant, and the `WobbleVisualiser`
   hull shake is therefore constant too.
3. **Windwalls and storm torque** — `1204`, `1229`, `1269`.
4. **Altitude and edge wind ramps.** These are implemented *in the wind field*
   (`ApplyTopWindIfNeeded` lerps `wind.y` toward 400 above Y=800). They also need
   `1250 WorldBoundsDataState`, which has **0 server refs**. The hard pushback in
   `WorldEdgePushback` is separate and does not need weather.

And the standing prohibition is unchanged: `1139`/`1269` stay in
`ComponentAbsencePolicy`. Seeding `1139` on ordinary gameplay entities produced a
measured **31,144 client errors in 158 s**. Retail used dedicated weather-cell
entities; anything here must too, and that is a research task before it is an
implementation task.

### 12.6a WIND — the corrections, and what is actually reachable

**Added 2026-08-20, branch `feat/wind`.** Server-only, nothing on the wire, so
no soak was required or run. Multiplayer **4182 passed / 0** (baseline 4132; 50
added), `WorldsAdriftServer.Tests` **1194 / 26 skipped** (baseline 1192).

#### The finding that reframes the whole subject

**Retail's own players never saw a weather cell either.** The shipped client
carries three entity blueprints with an explicit `EntityReadAccess` grant, as
TextAssets inside `resources.assets` — not as files on disk, which is why a
filesystem search for them returns nothing and means nothing. Verbatim:

| blueprint | `EntityReadAccess` |
|---|---|
| `Blight` | `["physics","visual"]` |
| **`WeatherCell`** | **`[ "social", "physics" ]`** |
| (a third, weather-cell-adjacent) | `[ "social" ]` |

`"visual"` is the Unity client, it is plainly in the vocabulary because `Blight`
beside it asks for it, and **`WeatherCell` does not grant it.** Its
`WeatherCellStateC` write access is `"social"` too. If the default granted every
worker, `Blight` would not have needed to name `visual`, so the list is
exhaustive and the omission is a denial. **PROVED** for the grant lists — read
off the shipped bytes. **INFERRED**, but hard to escape, for the consequence:
`GlobalWeather._weatherCellCoordMap` was empty on a real player's machine,
`GetCellSampleAt` always missed, and `GetWeatherAt` returned `(1,0,-2)` and
pressure 0.5 **in retail too**. Contrary evidence, stated rather than buried: a
`WeatherCell_unityclient` prefab exists and the client ships two ECS systems
that maintain a weather-cell coordinate map — both consistent with machinery
built and never fed, which this codebase has met many times.

So `2.236 m/s` toward +X/−Z is **not** "the becalmed case standing in for an
absent system". As far as a player was ever concerned it was retail's **only**
ambient wind, and the wind that *varied* — the thing the wiki's own sailing
guide tells players to steer by — was **wall wind**.

#### The client is ALREADY DRAWING wind, and we have never once fed it

All **PROVED** by reading the classes. None of these needs a client mod; every
one of them is rendering `(1,0,-2)` on production right now:

- `WindTrail.cs:79` — twenty wind-streak trails around the camera, oriented
  `SetLookRotation(wind, up)`, moving at `base + |wind| * k`. **These are the
  "windtrails in the sky" the wiki names.** Plain MonoBehaviour, no `[Require]`,
  live in `level0`.
- `WindControl.cs:162-164` — `transform.forward = LocalPlayer.Weather.Wind
  .normalized`, which drives the Unity `WindZone` (foliage/SpeedTree sway),
  every registered `Cloth`, and the global shader uniforms
  `_SinWindRotation`/`_CosWindRotation`.
- `FlagWind.cs:58-62` — a mounted flag points downwind. **This is the nearest
  thing the client has to the wiki's "windsock on your helm".** A windsock ship
  part does not exist: searched the decompile for
  windsock/windvane/weathervane/anemometer, and `resources.assets` with
  `grep -a` — the only hit is `3x2_Windsock`, a scrap-item icon
  (`valid-icons.txt:796`).
- `SailVisualizer.cs:75-80` → `SailControlVisuals.cs:218-236` — sail fill, luff,
  which side the canvas bellies to, blend-shape ripple, flapping SFX.
  **DIRECTION ONLY**: lines 76-79 normalise the wind below magnitude 1, so
  canvas can never show strength.
- `StormDebris`, `WeatherTextureGenerator`, `AmbienceSoundController`,
  `GliderControl`, and the dev-console `WeatherInfoProvider` readout.

**Consequence, and it is the constraint everything else hangs off:** we cannot
change what the client DRAWS without the forbidden lattice, but we entirely own
what a player FEELS — `WindPhysicsVisualizer` and `SailBehaviour` are on
`*_unityworker` prefabs only, so ship motion is whatever our 1130 says. Every
degree of divergence is a degree by which the streaks a player steers by lie.

#### Correction: WIND WALLS ARE NOT BLOCKED ON WEATHER

§12.6 item 3 and PHASE 6 item 7 ("nearly free *once weather exists*") are both
wrong. `GlobalWeather.GetWeatherAt:83` is
`Vector3.Lerp(cellWind, wallWind, wallQuery.Intensity)`, and
`WallSegmentVisualizer` has exactly **one** `[Require]` —
`WallSegmentStateReader`, i.e. `1204`. It registers into a static `WeatherWalls`
list that `GetWallWindAt` walks. No lattice, no Cantor pair, no 1139. The 44
authored segments are already imported and already drawn on the admin map.

**⚠ But 1204 alone makes it WORSE, and this is the phase's whole content.**
Every wall wind is scaled by a `GlobalWeatherDataVisualizer.*WindMultiplier`
static, and those are `0f` until **`1229 GlobalWallDataState`** is served with a
complete `FloatValues` map. `Lerp(ambient, zero, 1)` is zero: **a world given
1204 alone goes DEAD CALM inside its own wind walls** — streaks stop, grass
stops, sails empty — in the places meant to be the windiest in the world. And
`GlobalWeatherDataVisualizer.UpdateValues` `Debug.LogError`s once per missing
key across roughly forty keys, so a partial 1229 is its own error storm.
**1204 and a COMPLETE 1229 land together or neither lands.** That needs a soak;
it adds a new streamed entity class.

Honest limit, and the screenshots show it better than prose: a wall's reach is
**200 m at full strength, ramping to nothing at 400 m** (`WallData
.GetIntensityAt`, PROVED). Median distance from an island to its nearest wall is
**1,298 m**; only 1 of 266 islands sits inside 400 m and 53 inside 1 km. Wall
wind is 44 local features, not a world wind field.

#### Two levers the roadmap recommends that do not work

- **`5129 WindReceiverState`** is a REPORT channel, not a delivery one. Its only
  toucher in `acs` is `WindReceiverBehaviour`, which holds a `...StateWriter` and
  *publishes* `WeatherWalls.GetWallWindAt(...)` once a second, added on the
  `_unityworker` branch only (`SailPreprocessor.cs:24-29`). The one thing it
  reports is wall wind, so it is downstream of walls rather than an input.
  *INFERRED* that nothing reads it — an ECS reader could live in the missing
  `WASystems.dll` — but even then it is a worker-side reader of a worker-side
  writer. PHASE 6 item 7 names it as the per-entity delivery mechanism; it is not.
- **`1202`/`1203`** wind multipliers: readers ship and do run, but both only call
  `GlobalWeather.RegisterWeatherModifier(this)`, `_modifiers` is never
  enumerated, and `GetWindModifierAt` is a hard-coded `return 0f`
  (`GlobalWeather.cs:144-147`). Inert. **PROVED** — `_modifiers` is private to a
  class that is present and complete in `acs`.

**Also genuinely unblocked on 1139:** the altitude and map-edge wind ramps
(§12.6 item 4) are gated on `WorldBoundsDataVisualizer.CheckedOut`, i.e.
`1250`, not on weather. Not implemented here — our flight model has no vertical
wind term, and adding one is a flight change, not a wind change.

#### ⚠ METHOD WARNING that invalidated an earlier pass

`/home/ttanurhan/Games/WAReborn-decompiled/` does **not** contain
`WASystems.dll` or `SpatialTranslator.dll`. Any "no consumer exists" conclusion
drawn only from grepping that tree is a possible **false zero** — the same error
class as the ugrep binary-file trap. Everything above is read off `acs` (present
and complete for the classes named) or off the shipped assets directly.

#### What shipped on this branch

`WindField` (`.../Ship/Flight/WindField.cs`) is now the single answer to "what
is the wind at a place and a moment", composed of retail's published constant,
retail's recovered wall-wind geometry, and an **opt-in** variation model.

- **`WAREBORN_FLIGHT_WIND_FIELD`**, 0..1, **default 0 = production behaviour,
  bit-identical.** Above 0 it makes the wind vary by place (4 km cells) and time
  (10 min period) within a bounded excursion — **±40° of veer and ±35% of
  strength, WAREBORN TUNING** — and turns the bare-hull baseline back around to
  blow **downwind** the way retail's did, so a bare hull's heading finally
  matters and the streaks become something to steer by. Those two share one knob
  deliberately: the heading aim exists *only* because the wind is a constant, so
  they cannot be enabled separately.
- Anything the variation model does is an **invention, not a restoration** — per
  the blueprint finding, retail's ambient wind did not vary either. That is why
  it is off by default.
- **Admin map wind layer** (`admin-map-wind.js`, operator-only, a `wind` toggle
  plus three dials). It re-evaluates the server's own closed form in the browser
  rather than drawing an illustration — the same honesty the fauna layer buys —
  and a test pins the two copies against drift.

#### Recommended `WAREBORN_FLIGHT_WIND_SPEED`: **2.236** (down from 4.0)

Not because it is retail's becalmed default, but because **it is the magnitude
the client is drawing on every player's screen and the only wind it will ever
draw.** At 4.0 the streaks say 2.24 m/s while a bare hull drifts at 4.0 — seen
and felt disagree by 79%, and there is no way to fix that from the server side.

If a bare hull then reads as too slow, **raise `WAREBORN_FLIGHT_SAIL_POWER`
instead.** Sail power is LOST per-part data we are free to tune and label; wind
speed moves the bare-hull baseline and the sail force *together* and desyncs the
visuals as it goes. `2.236` is also below the client's own 5-knot helm-VFX
threshold, so a bare hull reads as drifting rather than sailing — which is the
intended feel already pinned by `BareHullBaselineDriveTests`.

### 12.7 PROVENANCE — WHAT NOT TO TRUST

The repo vendors a community archive at
`docs/research/world-data/external/wa-community-2026-08-16/`. Its own README says
it plainly: *"These are community measurements and reverse-engineered formulas,
not Bossa source data."* **Nothing in it is datamined.** Graded:

| source | grade | use it for |
|---|---|---|
| engine part → tier / head → basePower tables | **transcribed from the crafting UI** | safe to adopt |
| per-material `unitMass` | **transcribed, corroborated** (panel mass ÷ 40 matches all 20 rows) | safe to adopt |
| the 400-number combustion/propeller boost table | **fitted, weak** | rank order only |
| engine-science effectiveness | player measurement, hand-normalised | rank order only |
| wing-science | n=1 per material, **Closed Beta 0.1.3.3** — older than everything else | direction only |
| `speed = 50√(2P/M)` | **UI heuristic, weakest** | shape only; do not port |

Two specific traps found while grading:

- Three materials share a propeller boost value to **15 significant figures**.
  Independent measurements do not collide like that; it is copy-paste.
- The archive contains **three mutually inconsistent mass tables** (ratios ~0.743
  and ~0.88 between them). Resolve before adopting any of them. The
  panel-mass-÷-40 corroboration is the reason to prefer the `WEIGHT` column.

Also **do not** calibrate lift from `CompensationTest.cs:44`'s hardcoded
`GetMaxLift() => 1200f`. It is a dev scratch harness and implies ~122 kg of lift,
far below any real ship.

### 12.8 THE PHASED PLAN

Ordered so that each phase is separately shippable and separately reversible.
Every phase after F1 depends on F1's force model being on.

---

#### PHASE F1 — Ships fly on forces *(DONE on `feat/ship-flight`, `fcdc80e`)*
*Engines push, mass resists, drag decides top speed, sails catch wind.*

- **Delivers:** `ShipForceModel` (recovered constants and equations),
  `ShipPropulsion` (per-hull mass + engine thrust + canvas), a force path in
  `FlightIntegrator`, and derivation of all three from real mounted parts and
  real hull materials in `ShipFlightService`.
- **Player will FEEL:** a heavy hull is now permanently slower, not just slower
  off the line. More engines means more speed, with the recovered square-root
  return. **A ship with sails unfurled and the lever centred gets under way** and
  its heading changes how well it sails. A hull with no engines and no canvas
  does not move.
- **Depends on:** nothing. `WAREBORN_FLIGHT_FORCES=1`, **off by default.**
- **Migration:** no.
- **SOAK:** **yes** — it changes the speed distribution of the `1130` stream.
- **CLIENT MOD:** no.
- **Main risk:** ships built before this exist have no reason to carry engines,
  and under the force model they cannot move. That is why it ships behind a flag
  and wants a live flight before the flag is flipped. Second risk: our per-engine
  and per-sail magnitudes are WAREBORN TUNING, calibrated to reproduce today's
  12 m/s for a reference hull — they are a starting point for a balance pass, not
  an answer.

  > **THE FIRST RISK WAS OVERSTATED AND IS NOW FIXED — 2026-08-20,
  > `feat/flight-calibration`.** *"Under the force model they cannot move"* was
  > true of the code as written and **false of retail**, in two separate ways.
  >
  > **1. Sails, not engines, were the early game.** The maintainer, from playing
  > it: *"the ships can move without engines, that was never a problem. In the og
  > game you would first have sails until you figure out engines."* The stranding
  > set was never "hulls with no engines", it was "hulls with no engines **and no
  > sails**".
  >
  > **2. A bare hull was not immobile either.** *"No, the ship without sails can
  > move too, but really slowly."* This is **PROVED** in the decompile and the
  > audit above already contained the mechanism without joining it up:
  > `WindPhysicsVisualizer.ApplyWindDrag` evaluates the 2.5-power law on the
  > **relative** wind, `GetDrag(wind × windMultiplier − velocity)`. That single
  > expression is drag when the ship outruns the air and **thrust** when the air
  > outruns the ship, so a stationary hull is accelerated to the wind speed. And
  > the at-rest early return in `ManagedFixedUpdate` is skipped whenever
  > `IsFloatingShip` — i.e. **for any hull with a non-overloaded sky core**. So
  > the sky core, not the sail, is what makes a ship mobile at all, which is
  > exactly the maintainer's *"sky generator and a simple ship should hover
  > regardless and move slowly"*.
  >
  > Implemented as `ShipForceModel.BaselineDriveSpeedMps` — magnitude PROVED
  > (`|wind| × (1 − clamp01(mass/4000) × 0.75)`, ≈2 m/s, 3.9 kn on a legacy hull,
  > 1.1 kn on a 4000 kg barge), aim ours: retail pointed it downwind, and with our
  > single global constant wind a downwind-only baseline would let a bare hull
  > travel in exactly one compass direction for ever. It is aimed along the
  > heading, scaled by throttle, and gated on the pilot asking for drive so that
  > an abandoned hull still settles instead of emitting control points for ever.
  >
  > **Net effect on the flag: no ship can be stranded by it.** The progression is
  > bare hull → sails → engines, all three non-zero.

---

#### PHASE F2 — The atlas core starts mattering
*Lift becomes a budget a ship can exceed.*

1. Serve `1258 ShipLiftState.totalLift` from **`MaterialCatalog.SkyCoreLiftKg`**
   summed over mounted cores and modules, instead of the flat `1,000,000 kg`.
   The formula is already written, already tested and currently has no callers.
2. Enforce the recovered rule server-side: `mass > totalLift` ⇒ the ship
   **cannot climb**, and sinks. Cap vertical at the recovered ±2 m/s and 1 m/s².
3. Serve `1115 ShipCoreState.max_lift` per core so the shipyard UI can show the
   load ratio it is already built to render.

- **Player will FEEL:** the sky core becomes a real ship-building decision.
  Building in gold gets you a beautiful ship that will not leave the ground.
  Climbing becomes deliberate rather than instant — today's 6 m/s climb is
  **three times** the retail cap.
- **Depends on:** F1. Overlaps `feat/ship-components` on the sky core's
  interactability — coordinate, do not both edit the `1236` seed.
- **Migration:** no.
- **SOAK:** yes (vertical velocity distribution changes).
- **CLIENT MOD: YES, and it is a hard prerequisite, not a nicety.** This was
  the phase's open unknown when it was written; it is now **PROVED** and it is
  worse than expected. `ShipControlsBehaviour.UpdateVertical` (decompile
  `acs/ShipControlsBehaviour.cs:268-299`) resolves the driven ship's
  `ShipLiftVisualizer` and, **if `IsOverloaded`, returns before touching
  `_vertical` at all** — the axis is never updated, so the client simply stops
  sending vertical input — and OSD-spams *"Ship weighs more than its atlas sky
  core can lift."*

  Now combine that with the doomsday clock: `TotalLift = AtlasMultiplier ×
  state.totalLift` and `AtlasMultiplier` is **0.0** in 2026. So
  `TotalLift = 0` for **any** value we serve, `mass > 0` always, and **every ship
  is overloaded the moment a live `ShipLiftVisualizer` exists on it** — even at
  the current flat 1,000,000 kg seed. Serving a "correct" `1258` does not fix
  this; nothing served can fix it, because the multiplier is zero.

  **Two consequences, and the second is urgent.** (a) F2 cannot ship until
  `EndOfTheWorldConfig` is forced to 1 in the client mod. (b) Vertical flight
  demonstrably works in production today, so `ShipLiftVisualizer` must **not**
  currently be live on our hulls — most likely `1258` never reaches a checked-out
  visualizer, or `ParentingMassAdderVisualizer` is absent beside it. That is a
  cliff we are sitting next to: anything that makes `1258` properly live —
  including well-meant `[Require]`-completion work on the sky core in
  `feat/ship-components` — **would break climbing for every ship on the server**.
  Establishing exactly why the visualizer is inert today is the first task of
  this phase and is worth doing even if F2 never ships.

  > **BOTH CONSEQUENCES ARE WRONG — corrected 2026-08-20, on `feat/flight-calibration`.**
  >
  > **(a) is already done.** `EndOfTheWorld_Patch.cs` has pinned `AtlasMultiplier`
  > at `1f` since `a44aebb`, **2026-08-13**, six days before this was written, and
  > is in the shipped plugin DLL. F2's hard prerequisite is satisfied.
  >
  > **(b) is a wrong inference from a right observation.** Vertical flight does
  > work — but not because the visualizer is inert. With the multiplier pinned at
  > 1, `TotalLift` = the flat **1,000,000 kg** we seed at
  > `ComponentsSerializer.cs:616`, against a `1257` hull mass of 500–1700 kg. So
  > `Load ≈ 0.001` and `IsOverloaded` is false by three orders of magnitude. The
  > visualizer is live, correct, and simply never near its limit.
  >
  > **There is therefore no cliff to step off, and `feat/ship-components` is not
  > blocked.** Completing the sky core's `[Require]` set cannot break climbing,
  > because the two things that would have to be true for it to — a zero
  > multiplier and a realistic lift value — are both false. What F2 must not do is
  > swap the 1,000,000 kg seed for `MaterialCatalog.SkyCoreLiftKg` (~1000 kg for a
  > bare core) **without** first checking real hull masses against it: a 2-cell
  > legacy hull already computes to 1071 kg and would be overloaded on a bare core
  > the instant that seed changed. That is a genuine balance decision about live
  > ships, and it is a different and much more tractable problem than a doomsday
  > clock.
- **Main risk:** the seed swap in the note above — not the multiplier.

---

#### PHASE F3 — Steering comes off the ship
*Turn rate stops being a constant.*

1. Core torque from the recovered `CorePowerScale (0.5, 1.0, 0.5)` — yaw twice
   pitch and roll — with the mass exponent of 1.0, i.e. **mass-invariant angular
   acceleration**. Correct the code comments that claim heavy ships wallow.
2. Wing torque scaling from zero at rest to full at `MaxWingPowerSpeed = 10 m/s`,
   with the `Lerp(0.2, 1.0, alignment)` per-axis term, so **where** a wing is
   mounted decides **what** it is good at.
3. The airbrake: throttle against travel adds `AirBrake × −v`.

- **Player will FEEL:** a ship with no wings still turns (the core) but turns
  poorly at speed; wings make a ship carve. Pulling the lever back becomes a real
  brake. A wing mounted flat rolls; mounted upright it yaws.
- **Depends on:** F1. Needs per-wing `Power` and `AirBrake`, which are LOST —
  WAREBORN TUNING, same class as engine power.
- **Migration:** no. **SOAK:** yes (rotation changes every control point).
- **Main risk:** the integrator is rate-based, not torque-based. Converting it is
  a genuine rewrite of the turn path, not a parameter change, and it is the phase
  most likely to *feel* worse before it feels better.

---

#### PHASE F4 — The sky has edges
*Altitude ceiling and world bounds.*

1. Serve `1250 WorldBoundsDataState` (**0 server refs today**).
2. Server-side pushback: onset at Y=800, hard clamp at Y=1000, the recovered
   quadratic ramp.

- **Player will FEEL:** the world stops being infinitely tall. Flying up forever
  ends somewhere, deliberately, instead of by accident.
- **Depends on:** F1. **Migration:** no. **SOAK:** yes.
- **Main risk:** low, but `1250` is one of §2.8's never-served components — it
  wants the same absence-policy check any new component gets.

---

#### PHASE F5 — Engines become parts rather than a count
*Per-engine power from what the engine is made of.*

Today every mounted engine is worth an identical 1400 N. Retail's `Power` came
from the engine's head part, tier, and the material and quality of its combustion
internals and propeller. The **shape** is well attested by two independent
community efforts; the **coefficients** are one person's fit.

- **Player will FEEL:** building a *better* engine, not just more of them.
  Material and quality finally do something — §3 item 2 ("quality → stat") gets
  its first real consumer.
- **Depends on:** F1, and on `feat/ship-components` for per-part material and
  quality actually reaching a mounted part. **This is the meeting point with
  `feat/ship-fuel`:** if thrust is to require fuel, F5 needs from them a
  per-hull "is there burnable fuel, and at what rate" query. Ours consumes it;
  theirs owns it. Agree the seam before either side writes it.
- **Migration:** possibly — per-engine stats may need persisting.
- **SOAK:** yes. **Main risk:** adopting the community boost table's *digits* as
  though they were recovered. They are not (§12.7). Adopt rank order, re-derive
  magnitudes, label WAREBORN TUNING.

---

#### PHASE F6 — Real wind
*Blocked on weather; listed so the dependency is explicit.*

Sailing becomes local: wind varies by place, routes differ, and the 0.3
efficiency floor starts to matter because the other 0.7 is worth chasing.
Requires dedicated weather-cell entities and the `1139` research that
`ComponentAbsencePolicy` demands. **Do not start this before that research.**

### 12.8b THE SAIL CURVE — *"the optimum was 3 or 4 sails, then it was becoming too heavy"*

**Added 2026-08-20, branch `feat/flight-calibration`.** The maintainer remembered
an optimum sail count and asked for it to be checked. It is checkable arithmetic
rather than a matter of taste, and the arithmetic gives a clean and slightly
surprising answer: **the memory is real, but it is not a speed optimum, because a
speed optimum cannot exist.**

#### The speed curve has no peak, and that is a theorem rather than a measurement

Adding a sail adds thrust *and* mass, so the obvious model of the maintainer's
memory is that `v = [F/(0.007m)]^0.4` peaks somewhere. Write it out:

```
  v(n) = [ (F_e + n·f_s) / (0.007·(m_h + n·m_s)) ]^0.4
```

`v` is a monotone power of the ratio `R(n) = (F_e + n·f_s)/(m_h + n·m_s)`, which is a
**linear-fractional (Möbius) function of `n`**. Differentiate:

```
  R'(n) = [ f_s·(m_h + n·m_s) − (F_e + n·f_s)·m_s ] / (m_h + n·m_s)²
        = [ f_s·m_h + n·f_s·m_s − F_e·m_s − n·f_s·m_s ] / (…)²
        = ( f_s·m_h − F_e·m_s ) / (m_h + n·m_s)²
```

**The `n·f_s·m_s` terms cancel exactly, and `n` disappears from the numerator.**
The sign of `R'` is therefore constant for every `n`: the curve is monotone
everywhere and **has no interior maximum at 3, at 4, or anywhere else.** Which
way it runs is decided once, by one comparison:

| condition | consequence |
|---|---|
| `f_s/m_s > F_e/m_h` (the sail out-performs the ship) | **every** sail helps, for ever, approaching `[f_s/(0.007m_s)]^0.4` |
| `f_s/m_s < F_e/m_h` | the **first** sail already hurts |

So *any* model in which both thrust and mass are linear in the sail count cannot
produce a sweet spot. This is worth stating plainly because it closes off a whole
class of future tuning attempts: **you cannot get a 3–4 sail optimum by adjusting
sail power and sail mass.** No pair of values produces one.

#### Two things that ARE real, and together they are what the memory is made of

**1. The gain per sail collapses fast.** Even with no sail mass at all, `v ∝ n^0.4`.
On the live legacy hull (595 kg) at the 2026-08-21 sail power of 420, best heading
(historical baseline; superseded by the 2026-08-22 calibration audit):

| sails | settled speed | gain from the previous sail |
|---|---|---|
| 1 | 20.8 kn | +16.9 kn |
| 2 | 26.2 kn | +5.4 kn |
| 3 | 30.1 kn | +3.9 kn |
| 4 | 33.3 kn | **+3.2 kn** |
| 5 | 36.1 kn | +2.7 kn |
| 6 | 38.5 kn | +2.4 kn |
| 8 | 42.7 kn | +2.1 kn per added sail |

By the fourth sail, the next gain is barely a quarter of the first sail's gain.
Past four the return keeps shrinking under the 0.4-power law, which is exactly what
*"then it was becoming too heavy"* feels like from the helm even though nothing
is technically getting worse.

**2. The lift budget has a genuine cliff, and it lands in the right place.**
This is the better explanation, and note that the maintainer's own words are
*"too heavy"* — the vocabulary of the lift budget, not of the speed equation.
`IsOverloaded = totalMass > TotalLift` is a hard step, not a curve: one sail past
the line and the ship **cannot climb at all** and the client OSD-spams *"Ship
weighs more than its atlas sky core can lift."* With a bare core at **1000 kg**
(RECOVERED, and corroborated twice — our wiki-derived `SkyCoreLiftKg` and the
community `skycoreCalc.js` are the same expression with the same coefficients)
and our `1121 OriginalMassState` seed of **50 kg per mounted part**:

| hull | mass | + helm + core | sails before the cliff |
|---|---|---|---|
| cedar, 1 cell 1 deck | 325 kg | 425 kg | 11 |
| birch, 1 cell 1 deck | 500 kg | 600 kg | 8 |
| **legacy birch/iron, 1 cell 1 deck** | **595 kg** | **695 kg** | **6** |
| iron, 1 cell | 780 kg | 880 kg | **2** |
| legacy birch/iron, 2 cell 1 deck | 1071 kg | 1171 kg | already overloaded |

> **Re-verified 2026-08-20 (`feat/massalign`).** Every row above recomputes
> exactly from the shipped final-era table, so **none of these numbers moved**:
> cedar `2500 × 0.13 = 325`; birch `2500 × 0.20 = 500`; legacy
> `2500 × (0.20×0.8 + 0.39×0.2) = 595`; iron `2000 × 0.39 = 780`; legacy 2-cell
> `4500 × 0.238 = 1071`. The **1071 kg** figure is now pinned by
> `MaterialMassEpochTests.Changing_the_hull_mass_of_the_legacy_ship_is_a_deliberate_act`,
> so a future mass edit fails a test here rather than silently invalidating this
> table.

**Independently corroborated.** Players quote the mass gauge directly, and the
numbers land on our arithmetic: *"2 wings, 2 sails, 1 cannon and a barrel puts me
at the 'noob cap'"* at **950/1000 kg**, and *"Two wings, two engines, a sail and a
fuel tank is too much on a standard skyship?"* (**WIKI**, player-measured). Six
mounted parts on a starter hull sitting on the 1000 kg line is the same budget
this table computes, which is the strongest available check on both the 1000 kg
core and the ~50 kg part mass.

So the cliff is real and it lands between 2 and 11 sails depending on what the
hull is made of, clustering around 3–6 for the hulls a new player actually
builds. That is the maintainer's memory, and it is a **lift** phenomenon.

**3. AND THE COMMUNITY'S OWN ANSWER, which is neither of the above and is
probably the real one.** A Wayback sweep of the archived Bossa forum
(`www.worldsadrift.com/forums/topic/...`) finds an explicit consensus number, and
it is **3** — but the stated cause changes across two eras, and neither cause is
speed or weight:

- **Before Beta 0.2.2.5** the limiter was believed to be the wind itself:
  *"More sails means better speed, but only up to a point; you can't go faster
  than the speed of the wind. Way back in Alpha 5 some madman stuck 40 sails on
  his ship and opened them all. It was not noticeably faster than having 3."*
  (**WIKI**, player opinion + secondhand measurement.)
- **After Beta 0.2.2.5** it became a hard geometric fact: *"They added collisions
  to sails, essentially making it very hard to have more than 3-4 sails on a
  ship."* Sails must be free to rotate without striking each other, so a normal
  hull simply has nowhere to put a fifth. (**WIKI**, player opinion.)

**The collision limit is the best single explanation of the maintainer's "3 or
4"**, because it is the only one that produces exactly that number for everyone
regardless of what their hull was made of. Note it is a *placement* constraint —
we do not implement sail-boom collision, and nothing here proposes we start.

**The counter-case, which settles the theorem empirically.** *"I made a ship with
69 sails before. It went pretty close to that 70 knot theoretical maximum."*
(**WIKI**, player measurement.) A 69-sail ship being the fastest thing anyone
built is a direct observation that **sail speed is monotone increasing** — which
is what the algebra above proves and what the pre-patch "40 is no better than 3"
folklore denies. Where the community contradicts itself, the decompile decides,
and the decompile has no velocity term in the sail path at all.

**One number not to adopt.** The same posts assert an absolute ceiling of
70.71 knots, and it is seductive because it is exactly the airspeed gauge's full
scale. But it falls straight out of the community's own fitted
`v = 50·√(2P/M)` at a power-to-weight of 1:1, not out of the client: §12.2
records that **retail set no speed cap anywhere**. It is a property of their
formula, not of the game. That the gauge stops at the same place is worth
knowing and is not evidence for it.

**Caveat, stated because it is the weakest number here.** The 50 kg per-part mass
is **ours** (`ComponentsSerializer`, a placeholder), not retail's, and the cliff
position is directly proportional to it. The cliff's *existence* is recovered;
its *position* is a tuning value we chose. If F2 ever makes lift real, that
number becomes a balance knob worth deriving properly rather than a stub.

**Overload was not benign in retail, and it is worth knowing what it did.** The
same forum thread has a Bossa moderator confirming an overweight ship is
**blocked from undocking**, and a player describing one that became overweight in
flight — a **damaged core or expansion module stops contributing lift** — showing
the message and then **sinking into the abyss**. The gauge reads `current / max`
in kg (*"2300kg out of possible 3400kg"*, *"818/1000"*).

**It cannot sink a ship here, though, and the reason is structural.** The sinking
lives in `ShipControlVisualizer.UpdateFloating`, and that class is
`[WorkerType(WorkerPlatform.UnityWorker)]` — it only ever ran on the FSIM physics
worker. `ShipPhysicalityVisualizer.ClientDynamic()` hardcodes `false`, so a ship
is permanently kinematic on a player's machine and integrates nothing; its
altitude is whatever our `1130` stream says. The only client-side consequence of
overload is `ShipControlsBehaviour.UpdateVertical` returning early — vertical
input stops, the OSD spams, the ship holds station. **Sinking arrives only when
F2 implements weight and lift server-side, and then it is ours to author.**

**None of this is live today**, because `1258` is seeded at a flat 1,000,000 kg
so the overload rule cannot fire. The cliff is what players would meet **if F2
shipped**, and it is an argument for F2 being a good feature rather than against
it: it is the mechanism that makes the sky core a real ship-building decision,
and it produces the maintainer's remembered behaviour without anyone tuning for
it.

### 12.9 WHAT ONLY A LIVE FLIGHT CAN SETTLE

1. **Whether the force model feels right**, which is the only acceptance test
   that matters for a physics change. Fly with `WAREBORN_FLIGHT_FORCES=1` and
   compare a light hull, a heavy hull, and the same hull with canvas up and down.
2. **Whether 1400 N per engine and 840 per sail are the right magnitudes.** Engine
   power re-derives today's reference speed. Sail power is now independently
   now selects the stronger member of the one surviving retail balance bracket
   after the lower member failed live acceptance; four sails on the 800 kg
   reference total mass settle near 38 knots. The full evidence and matrices are
   in `docs/research/sail-speed-calibration-2026-08-22.md`. Both remain WAReborn
   tuning because retail's per-part data is lost.
3. **Whether a stationary ship under sail moves at a rate that reads as
   sailing** rather than as drifting. On an 800 kg total flight mass, two sails
   now settle at about 1.9–15.3 m/s depending on heading, against the roughly
   12 m/s still-air two-engine reference.
4. ~~**What the unpatched client does with a real `1258`.**~~ **ANSWERED, and it
   is a live hazard rather than a live flight question** — see F2. What a live
   flight *should* now check is the inverse: whether a piloted ship ever shows
   the *"Ship weighs more than its atlas sky core can lift"* OSD message today.
   If it never does, `ShipLiftVisualizer` is confirmed inert on our hulls and the
   cliff in F2 is real but not yet stepped off. **This is a 30-second check at
   the helm and it gates other people's branches, so it is worth doing first.**
5. **Whether a sailed ship left unmanned drifts away.** Under the force model
   sails keep pushing while the hull is in motion, which is retail-authentic and
   produces ghost ships. Retail answered this with `ShipAbandonedBehaviour`;
   we have no equivalent.

---

### 12.11 "THE SECOND I LET GO OF HELM IT STOPPED" — the design is already there

**Reported 2026-08-19,** then corrected by the maintainer: the stop is not a
regression, and they want retail's behaviour — a ship under way keeps going when
the pilot steps off.

**RETAIL, PROVED.** Nothing anywhere in the authoritative sim is conditioned on a
helm occupant. `ShipControlVisualizer` is `[WorkerType(UnityWorker)]` and reads
**1113 ShipControlState**, not a pilot: `FixedUpdate` runs `UpdateTorques`,
`UpdateDock`, `UpdateFloating` unconditionally, and `EngineVisualizer` applies
`ShipThrustMultiplier * spin * (boost + Power) * forward` off `ShipEngineState`
with no pilot test. The **client** zeroes its local lever when not driving
(`ShipControlsBehaviour.cs:167-172`), but that branch cannot reach `SendData()` —
so **retail never wrote a zeroed input on dismount**, and the ship's own 1111 kept
the last value. The proof that it persisted authoritatively is
`PilotVisualizer.cs:136-141`: taking control reads the **ship's** stored input
back into the local lever via `SetInitialInput`, which is only meaningful if the
previous pilot's lever survived their departure. Retail's throttle is a latched
accumulator (`UpdateAxis1f`, `value + delta`), not a spring.

**AND OUR SERVER ALREADY DOES THIS.** `FlightSession.Dismount()` sets
`_input = _input.LatchedThrottleOnly()` — throttle kept, steering and climb
released — with a comment saying exactly that, and
`FlightSessionTests.A_released_helm_keeps_its_latched_forward_or_reverse_command`
pins it. `Advance` integrates on `_manned || !IsAtRest || Throttle != 0`, so an
unmanned moving hull keeps integrating; `_activeHullIds` is only ever emptied by
ship salvage; `ShipFlightService` even logs `cruising unmanned on latched
throttle`. Only a **disconnect** (`Abandon()`), a dock or an admin stop neutralise
the lever, and each is documented as deliberately different from a clean release.
The publish gate keys on `pilot || MustRetainMotion || AnyoneAboard(hull) ||
IsPiloted(hull)`, and a pilot who steps off but stays on the deck satisfies the
third term — so the 1130 stream does not stop either.

**So this needs no code change, and none was made.** What the maintainer saw
contradicts the tree, which means the next step is two log lines from one flight,
not a blind edit. The candidates, in order:

1. **The lever never left the deadzone.** The retail client applies
   `_throttleDeadzone = 0.15f` in `SendData`, and `LatchedThrottleOnly` applies
   its own `0.01`. A lever nudged but not pushed past 0.15 sends throttle 0, and
   0 is what latches. **The tell:** `[flight] entity N DISMOUNTED helm ...` prints
   the session state; a `SpeedCmd` of 0 there means the command was already zero
   before the release, not because of it.
2. **A disconnect-shaped exit.** Only `OnPlayerGone` and `/admin release` pass
   `abandoned: true`. The line above says which path ran.
3. **It worked and read as a stop** because the legacy model's deceleration is
   4 m/s², i.e. 12 m/s → 0 in **3 s over ~18 m**, which does look instant.

**A REAL PROPERTY WORTH DECIDING, and it is a design call, not a defect.** Under
the production model (`WAREBORN_FLIGHT_FORCES` **off**) there is **no drag term
at all** — `FlightIntegrator`'s legacy branch commands `throttle * MaxSpeedMps`
directly. So a latched lever means a ship cruises **forever, in a straight line,
through islands** (nothing in `Ship/Flight/` references terrain or altitude
limits) until someone re-mans it or an admin stops it. Under the force model it
would coast on retail's own `0.007·v^2.5` plus the every-step 0.03 m/s² residual:
from 9 m/s **~75 s and ~131 m**; from 17 m/s **~77 s and ~154 m**; from 20 m/s
**~77 s and ~159 m**. (The former 123-125 s figures accidentally gated the
residual below 1 m/s; the complete decompile has no such gate.) Retail had both — drag *and* engines that kept burning unmanned —
so retail's answer to a runaway was `ShipAbandonedBehaviour` (24 h with no owner
aboard, and it makes the ship **sink**, not stop), not deceleration.

The smallest honest change, if the maintainer wants an unmanned ship to settle:
one branch in `FlightIntegrator`'s legacy path applying
`ShipForceModel.StepSpeed(speedCmd, 0, dt)` when the session is unmanned, which
reuses code that already exists and already carries retail's constants. **Not
made here**, because "keeps its velocity" and "eventually comes to rest" are
different products and the maintainer asked for the first.

A web sweep of the surviving community record (fandom, Wayback, reddit, Steam
guides, the WAEngenius and `worldsadrift.science` calculator **sources**, and
Bossa's own patch notes and forum posts) was run against the decompile findings
above. Everything here is **WIKI** unless marked otherwise, and it is recorded
because it is *independent* of the decompile — where the two agree, confidence
goes up a lot; where they disagree, the decompile wins.

**Two clean reconciliations — these are worth the whole sweep.**

1. **Our sky-core lift formula is independently confirmed.**
   `MaterialCatalog.SkyCoreLiftKg` derives `lift = 1000 + rate × (10 + quality)`
   from the wiki's Atlas Core table. The recovered source of the community
   calculator `worldsadrift.science/skycoreCalc.js` computes
   `lift = base + genMult[generatorMaterial] × (10 + generatorQuality)` with
   `genMult = { aluminium: 6, copper: 7.5, silver: 8, gold: 8.5 }` — **the same
   expression and the same coefficients**, arrived at by a different person from
   different data. Our formula is safe to build F2 on. Corroborating anchors: a
   bare core is **1000 kg**, eight upgrade modules take it to **6000 kg**, and a
   Q10 gold generator was reported at **7020 kg**.

2. **The "2800 m altitude cap" and the decompile's "Y = 800" are the same
   number.** The wiki records a global ceiling of 2800 m from Beta 0.1.3.7, which
   flatly contradicted `WorldEdgePushback`'s onset at global Y = 800. They
   reconcile exactly: `AltimeterVisualiser` displays **`height + 2000`**. Global
   Y 800 *is* an altimeter reading of 2800. **So the ceiling is confirmed from
   both sides, and F4's numbers are right** — but note the altimeter offset,
   because a player reporting an altitude is reporting global Y **+ 2000**.

**Corroborated, no conflict:**

- **Sails move an engineless ship.** Multiple independent sources, including
  Bossa-era guides describing sails as what *"allow first movement"* before a
  player can afford engines, and several documented engineless sailing rafts.
  This now has both PROVED and WIKI support.
- **Tacking was real and necessary** — players zig-zagged upwind. Consistent
  with the recovered model, where the worst heading still yields a small force
  through the 0.3 floor: upwind progress is possible and miserable, which is
  exactly the condition that makes tacking rational.
- **Wings provide no lift and only turn the ship** — the wiki says so outright,
  matching `WingVisualizer` producing torque and never force.
- **A wing at 45° is "70% as effective" on both axes** — a cosine projection,
  matching the recovered `Lerp(0.2, 1.0, alignment)`.
- **The core torque is real, and this explains a dead component.** A Bossa
  engineer, 2015: *"the ship cores provide torque to the ship, so it can rotate
  even in-place — I call them 'reaction wheels' in the code"*. That names
  **`1110 ReactionWheelState`**, which §12.3 records as having zero consumers.
  It is dead because the mechanism moved into `ShipControlVisualizer`'s
  mass-scaled core torque. Do not implement `1110`.
- **Ship physics was server-authoritative and degraded under load.** Bossa's
  Update 30 notes name the sim **FSIM** and describe its speed varying with
  **time dilation**; Update 29 adds *"server optimisations for ships with many
  engines and/or wings"*. Players measured roughly a 30% speed loss in busy
  zones. Our architecture assumption is correct, and worth remembering when
  judging any player-reported speed.

**Three corrections we should act on:**

1. **Reverse thrust should probably be 0.2, not our 0.4.** A Bossa engineer,
   2015: *"engines provide full 'puller' power and **20% 'pusher' power**, i.e.
   all engines are reversible, but not at full efficiency."* A 2018 player puts
   it nearer 25%. This is a **dev statement**, the strongest non-code evidence in
   the whole sweep — but it is from 2015 and it would change how the live game
   feels today, so `DefaultReverseFactor` is left at 0.4 and flagged here rather
   than changed quietly. **A deliberate decision, not an oversight.**
2. **Any sail number from before October 2018 is off by a factor of two.**
   Bossa's PTS Update 27 notes: *"Halved wind power, which functionally halves
   thrust from sails."* Our default wind `(1,0,-2)` is read from the **final**
   client, so it is already post-nerf and needs no adjustment — but it means the
   frequently quoted sail speeds of 45–60 knots describe a game that no longer
   existed at shutdown. Do not calibrate canvas against them.
3. **The community "power" unit is not newtons, and there is no constant bridge.**
   The community speed law `speed_knots = 50 × √(2 × power / mass_kg)` is a
   player fit, but it validates exactly against a stated measurement (900 power,
   3000 kg → 38.73 knots). The recovered shipped
   `v = [F/(0.007m)]^0.4` has a different exponent, so a fixed newtons-per-point
   conversion cannot make the curves agree. Around that historically measured
   example, it still places useful engine magnitudes in the low thousands of
   newtons. Our 1,400 N default is therefore plausible and independently
   re-derives the server's established 12 m/s reference speed; it remains
   WAREBORN tuning.

**One detail that should stop us building the wrong thing.** The wiki states
that **a sail's material has no effect on the thrust it provides** — players were
advised to build sails from the lightest wood available, because the only thing
material changed was the sail's weight. So when F5 gives engines a material and
quality model, **do not give sails one to match**: per-sail `Power` should vary
with the sail's *size or schematic*, if anything, and material should affect only
mass. Symmetric-looking systems were not symmetric here.

**The one dispute the decompile settles.** The wiki contradicts *itself* on the
best point of sail: its main text says dead downwind is fastest, its tips section
says *"you move faster at 90° to the wind than with the full wind"*, and no
player ever published a measured polar. **The recovered geometry answers it:**
sweeping the implemented model over all headings peaks at dead downwind and falls
to roughly half that on a beam reach. The main text is right and the tip is
wrong. This is the kind of question only the decompile can close, and it is why
WIKI stays the weakest tier.

**A caution on every speed number above.** The in-game airspeed indicator was
widely reported as **buggy through mid-2018** — reading non-zero at a standstill,
and disagreeing with observed overtakes. Player speed measurements from that
window are gauge readings, and the gauge was lying.

---

## 13. FUEL — how it worked, and how it works here

> **⚠ READ §13.11 FIRST.** Everything below about *where fuel is stored* and
> *where you refuel* was superseded on 2026-08-20: **the POWER GENERATOR is the
> fuel tank**, capacity 100 each, pooled across a hull, and refuelling is holding
> E on it — a prompt the shipped client itself labels "Refuel". §13.4's
> "there is no fuel tank prefab" searched for the wrong name; the prefab is
> `PowerGenerator01`. The component analysis (§13.1–§13.3), the gauge fix, the
> canister yield and the flight seam (§13.7) are unaffected and still current.

**Owner: `feat/ship-fuel`.** Written from the decompile
(`/home/ttanurhan/Games/WAReborn-decompiled`) and the shipped client asset
census, because before this section nobody on this project knew. Every claim
below carries a provenance label. The wiki is the **weakest** source here and is
used only where it is the sole survivor; where it disagrees with the decompile,
the decompile wins.

### 13.1 The answer, plainly: how fuelling worked in retail

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
TUNING and is labelled as such in §13.6.**

### 13.2 The three components, exactly

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

### 13.3 The gauge: what was wrong, and what fixes it

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

### 13.4 The two hard constraints this server has, that retail did not

> **⚠ SUPERSEDED by §13.11.** Constraint 1 below is FALSE, and Constraint 2's
> conclusion with it. The census does contain the tank; it is called
> `powergenerator01` (line 219), and its prefab bakes an `Activate` whose overlay
> asset reads "Refuel". Left in place because the shape of the mistake — searching
> for a thing by the name we call it rather than the name the assets use — is the
> reusable lesson.

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

### 13.5 The 1258 landmine — what fuel seeds, and what it could have woken

§12's audit found that `AtlasMultiplier` evaluates to **`0.0`** today (Bossa's
shutdown doomsday clock), so a properly-live `1258 AtlasSkyCoreState` would make
`TotalLift` zero, every ship permanently overloaded, and climbing would stop
working for everyone. The current inert `ShipLiftVisualizer` is what keeps
flight working. That is the same silent-inertness family as the fuel gauge, and
it has teeth — so this section states, explicitly, what fuel seeds and what it
could have woken.

**Fuel seeds exactly ONE new component: `1105 FuelGaugeState`, on the
`fuelGauge` PART entity.** Not on the hull, not on the sky core, not on any ship
root.

Enumerated the way the standing rule demands — which client visualisers could
this component's presence NEWLY satisfy, not just the one we are aiming at:

| component | every class in the decompile that `[Require]`s it | verdict |
|---|---|---|
| **1105** (what we seed) | `FuelGaugeVisualizer`, and **nothing else** — an exhaustive grep for `FuelGaugeStateReader`/`FuelGaugeState.Reader` across `acs/` returns one file | **safe.** One reader, and it is the one we want |
| 1258 `AtlasSkyCoreState` | `ShipLiftVisualizer` (`[Require] ShipLiftStateReader`, its only one) | **untouched.** Fuel neither seeds it nor changes its value |
| 1106 `FuelTankState` | `FuelVisualizer`, which `ShipPreprocessor.cs:77` adds to **every ship ROOT** | **deliberately NOT served.** See the warning below |

**The refuel door does not seed anything either.** Giving the sky core its
`Activate` prompt changes two *values* inside `1210 InteractiveState` — a
component that was already served on every ship part — and adds no component to
the core or the hull. `InteractiveObjectVisualizer` already `[Require]`d 1210
and was already enabled; nothing new wakes.

> **⚠ DO NOT serve `1106` on the hull as a "completeness" move.** It is the
> obvious next tidy-up — retail put a tank state on ships, we have a tank, the
> component exists. But `FuelVisualizer` is attached to every ship root and
> `[Require]`s 1106, so serving it would enable a visualiser that has been inert
> since this server started. In *this* case the consequence looks benign — its
> only method, `GetFuelPercent()`, has **zero callers in the entire decompile**
> — but "looks benign" is exactly what was thought about the fuel gauge and the
> containers, and §12's `AtlasMultiplier` finding is what happens when it is
> not. If a later phase wants 1106, re-run this enumeration first.

### 13.6 The numbers, and why

All WAREBORN TUNING unless marked. Every one is env-overridable, because none of
them is recoverable and the first live flight is the only real test.

| quantity | value | reasoning |
|---|---|---|
| one canister | **25 fuel** (8+8+9) | **WIKI/RECOVERED** — already in `FuelCanisterYield`, untouched |
| generator capacity | **100 fuel** — **RECOVERED**, §13.11 supersedes the row below | wiki + `FuelGaugeVisualizer.cs:56`'s own `SetFuelAmount(0f, 100f)` default. `WAREBORN_FUEL_CAPACITY` now means ONE GENERATOR |
| ~~ship capacity~~ | ~~**250 fuel**~~ (superseded) | ten canisters. Large enough that refuelling is an errand, small enough that one salvage trip fills you. `WAREBORN_FUEL_CAPACITY` |
| burn at full throttle | **0.25 fuel/s** | a full tank is 1000 s ≈ **16 minutes of continuous full throttle**; one canister ≈ 100 s. `WAREBORN_FUEL_BURN_RATE` |
| burn shape | **proportional to abs(throttle)** | retail's `consumption` and `throttle` are separate live floats and the client's own audio scales load by their product. Half throttle costs half. Idling costs nothing |
| empty ⇒ no thrust | **WAREBORN TUNING, not recovered** | see below |
| one refuel press | **everything that fits** | retail's per-press amount is unrecoverable. Moving the whole overlap in one hold beats making the player mash E |
| tank on introduction | **FULL** | see the risk note below |
| gauge push | **≤1 Hz, and only on a ≥1-unit change** | the client already delays the needle 2 s and lerps it; anything faster is invisible traffic |

**The thrust gate is a DESIGN DECISION, and §12 sharpens why.** The flight audit
proves retail's engine force is
`ShipThrustMultiplier × spin × (boost + Power) × forward`, applied at the
propeller, and that it **consumes nothing** — lift is a kilogram budget, not a
fuel draw, and neither the engine nor the core reads a fuel level anywhere in
the client. Fuel accounting lived entirely on the GSim, and the client carries no
`fuel == 0` branch at all (§13.1). So "empty means the engines stop" is a
reconstruction of the only behaviour that makes fuel a resource, **not** a
recovered retail rule, and it is labelled WAREBORN TUNING for that reason. It is
also the one part of this feature that can inconvenience a player mid-flight,
which is why it has its own kill switch (`WAREBORN_FUEL_GATES_THRUST=0`) rather
than sharing the subsystem's.

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

### 13.7 The seam with `feat/ship-flight` — what this branch expects from theirs

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
2. **What THEY asked us for, already built.** Phase F5 asks fuel for "a per-hull
   *is there burnable fuel, and at what rate* query — ours consumes it, theirs
   owns it. Agree the seam before either side writes it." Here is that seam,
   concretely, so nobody has to invent it:

   ```csharp
   // Is this hull burning anything at all? FALSE for a hull with no sky core -
   // no refuel door, so no fuel system, so never gate it.
   WorldsAdriftRebornGameServer.ShipFuel.Ledger.IsMetered(hullEntityId)

   // Has it run out? FALSE for an unmetered hull, by the same rule.
   WorldsAdriftRebornGameServer.ShipFuel.Ledger.IsDry(hullEntityId)

   // The level itself, for a proportional model: FuelReading { Capacity, Level,
   // Fraction (0..1, and 1.0 for an unmetered hull), IsDry }.
   WorldsAdriftRebornGameServer.ShipFuel.Ledger.Read(hullEntityId)
   ```

   **The contract that matters more than the signatures:** an *unmetered* hull
   must read as fully fuelled, never as empty. That is what stops a ship with no
   sky core being grounded by a feature it cannot participate in, and any
   consumer that treats "no entry" as "no fuel" breaks it.

   Fuel deliberately does **not** offer a `Consume(hull, joules)` — burning is
   throttle-driven and owned here, so that F5 can scale *power* without also
   owning *depletion*.

3. **What we would rather have back, from `feat/ship-flight`.** Two things, in
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

### 13.8 The phased plan

Same contract as §5 and §11.8: what it delivers · what a player can newly DO ·
dependencies · schema migration · networked state (soak gate) · main risk.

---

#### PHASE FU1 — The needle moves, and fuel is real

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
     commands, per §13.6.
  6. **Gauge push**: 1105 broadcast to every mounted `fuelGauge` on that hull,
     rate-limited per §13.6.
- **Player can newly do:** salvage fuel canisters, walk up to their ship's sky
  core, hold E, and **watch the fuel gauge needle climb**. Then fly, and watch
  it fall. Then run out, and coast to a stop.
- **Depends on:** nothing. `atlasSkyCore`, `fuelGauge` and the `"fuel"` item
  all already exist and already work.
- **Migration:** **no.** Fuel lives in memory for the session; a restart
  refills. Deliberate — see FU3.
- **SOAK: YES.** A per-ship level ticking continuously with a periodic 1105
  broadcast is exactly the new networked state the standing rule names.
- **CLIENT MOD: no.** Every piece is a component the stock client already
  reads, on a prefab it already resolves.
- **Main risk:** the sky-core prompt reads as the generic *Activate*, not
  "Refuel" — `InteractionEntry.description` is transmitted and **never rendered**
  by the shipped client (§13.1), so there is no way to label it. A player who
  does not read patch notes will not discover refuelling. Second risk: a ship
  with no sky core silently has no fuel system; that is the deliberate
  non-punitive rule, but it means two ships can behave differently for reasons
  the player cannot see.

---

#### PHASE FU2 — Fuel is visible without a gauge

- **Delivers:** a chat/toast line on refuel ("Refuelled: 250/250") and a low-fuel
  warning at 10%, both on the existing native toast path
  (`HarvestReward`'s 8060). Retail had neither, and retail also had a tank you
  could walk up to and a gauge on every ship; we have one instrument and an
  unlabelled prompt.
- **Player can newly do:** find out they are nearly dry without having crafted
  and mounted a fuel gauge.
- **Depends on:** FU1.
- **Migration:** no. **SOAK:** no — one-shot toasts on an existing path.
  **CLIENT MOD:** no.
- **Main risk:** toast spam if the threshold has no hysteresis.

---

#### PHASE FU3 — Fuel survives a restart

- **LOUD:** this is isolated into its own phase **on purpose**. It is an
  additive `double Fuel` property on `BuiltShipRecord` in **`world-state.json`**
  — plain JSON, game-server only, whose default `0` must be read as "legacy,
  fill it" and not as "empty". It is **NOT** a Postgres migration and does
  **NOT** require a login-server deploy. The same additive-default discipline
  `MountedPartRecord.LampOff` documents (stored inverted so the JSON default
  means the legacy state) applies here, and the legacy sentinel must be chosen
  before a line is written.
- **Delivers:** a tank level that persists across a server restart.
- **Depends on:** FU1.
- **Migration:** **JSON only, additive, no DB.** **SOAK:** no. **CLIENT MOD:** no.
- **Main risk:** reading a legacy `0` as an empty tank and grounding every ship
  in the world on the first restart after deploy. Use a nullable/sentinel, not a
  bare double.

---

#### PHASE FU4 — Engines matter

- **Delivers:** burn scaled by mounted engine count and by each engine's crafted
  `fuelEfficiency` stat (the retail location for it, §13.1); `1104
  FuelConsumerState` served on each engine so `attached` is honest; the
  `enginesLit` seam of §13.7 replacing the throttle clamp.
- **Player can newly do:** craft an economical engine and get more range out of
  the same canister — the reason `fuelEfficiency` exists.
- **Depends on:** **`feat/ship-flight` PHASE F5**, which is the same meeting
  point seen from their side ("engines become parts rather than a count"). Their
  F5 needs the fuel query §13.7 now specifies; this FU4 needs their `enginesLit`
  parameter. Neither is blocked on the other's *code*, only on the seam, and the
  seam is written down in both sections now.
- **Migration:** no. **SOAK:** yes — 1104 is new per-engine state.
- **Main risk:** it is not this branch's to sequence.

### 13.9 What only a live flight can settle

1. **Whether the needle actually moves.** Nothing headless renders a
   `GaugeRoller`. Craft a Fuel Gauge, mount it, refuel, and watch. The needle
   lags ~2 s by design (§13.3); it is **not** broken if it is late.
2. ~~**Whether the sky core shows an E prompt at all.**~~ **SETTLED IN A LIVE
   CLIENT, 2026-08-19, and the answer killed the door.** See §13.10.
3. ~~**What the prompt says.**~~ **SETTLED, and §13.1 was wrong.** It reads
   **"Activate Atlas Pulse"**. See §13.10.
4. **Whether a dry ship coasts or stops dead.** The clamp goes through the
   normal deceleration curve, so it should glide to a halt over several seconds.
   A hard stop would mean the clamp is landing somewhere it should not.
5. **Whether a dry ship holds altitude.** It must. Fuel never touched
   `ShipLiftState 1258` in retail and does not here. If a ship sinks, that is a
   flight bug this feature revealed, not a fuel behaviour.
6. **Whether a bunker actually feeds the tank.** New, and the whole refuel path
   now: put fuel in a trunk bolted to the ship, burn a canister's worth, and
   watch the gauge climb again. The tell is
   `[fuel] hull N drew M fuel from its bunker`.

---

### 13.10 THE DOOR WAS WRONG — settled in a live client, 2026-08-19

**Reported:** *"I'm trying to interact with sky core, it says 'Activate Atlas
Pulse', I press E, I can see the loading thing as I press it down and then
nothing happens."*

Three separate facts, and each one matters.

**1. The verb was free. The PROMPT was not, and §13.4's premise missed it.**
§13.4 concluded the sky core was "the only ship part whose Activate is baked,
unused and unclaimed". The *verb* is: the only client reaction to a returning
`Interact` is `InteractiveObjectVisualizer.OnInteractSent` →
`SendMessage("OnInteract")`, and the only three `OnInteract` receivers in the
entire decompile are `ToggleRoller`, `ResetSwitch` and `InteractiveDemo`. Our
1211 arrives and is handled; nothing intercepts it. **PROVED.**

But the words the player reads are a **baked client asset**.
`InteractiveObjectVisualizer.GetTutorialStep` maps `(verb == Activate)` +
`GetComponent<ShipCoreVisualizer>() != null` to `TutorialStep.MOUSE_OVER_CORE`,
whose overlay asset carries `Name: "Activate Atlas Pulse"`, `Hold: true`. No
server can change that string. **PROVED**, and it makes the refuel a control
that lied about what it did — which is exactly what `PartInteractionPolicy`
exists to forbid. §13.1's prediction ("the generic Activate glyph with no text")
was wrong because it reasoned about `InteractionEntry.description`, which
genuinely is never rendered, and missed that the client has its own label table.

**2. It names a REAL retail action, so the door was also occupied.**
`1306 ShipAtlasPulseState { activeTime: int, activationCooldown: int }` drives
`ShipAtlasPulseVisualizer`, which `ShipPreprocessor.cs:91` attaches to **every
hull**. It is not cosmetic: the class implements `IClimbGrapplePreventer`, and
`GrapplingHookNew.HasActivePreventer` and `PlayerMove.HasActiveClimbPreventer`
both refuse while `IsPulseActive`. **The Atlas Pulse was the ship's anti-boarding
defence** — while it is up, nobody can grapple onto or climb your hull, the hull
glows on `_OveralEmmissive`, a sphere sized from the ship's own `ShipPlan`
expands from the core, and Wwise plays `AtlasPulse`.

**3. "Nothing happens" had TWO causes, and one of them was not fuel's.**
- The hold bar itself is the tell. We serve `ActivateTimeToUse = 0f`, and
  `TimedInteractionController` draws **no bar at all** below 0.001 s. A visible
  filling bar therefore proves `InteractAgentObserver.cs:374`'s `flag2` was
  **true** — the client had decided the core was on a **non-friendly ship** and
  applied the `+10f` penalty at `:398`. That is a **ten-second hold**, and it was
  costing every mounted-part interaction on this server, not just the core. Same
  root cause as the container lock — see §11.10 — and fixed there.
- Even a completed refuel was near-invisible. A registered tank **starts full**
  (§13.6), so `Deposit` returns 0, `TryRefuel` logs `refuel refused: ... tank is
  full` to the server console and returns without sending the client a byte.

> **⚠ SUPERSEDED by §13.11.** The diagnosis in this subsection is correct and
> still the reason the sky core is served no verb. The FIX is not: the bunker
> drain has been deleted, because the power generator has a baked prompt that
> reads "Refuel" and is therefore an honest door. The consequence noted at the
> end of this subsection — a metered hull that cannot be refuelled — is
> structurally impossible now that metering and the refuel door are the same part.

**THE FIX: refuelling moved to the ship's own BUNKER.** Fuel put into any
container bolted to the hull is drawn into the tank as the tank makes room, on
the burn tick that already runs (`ShipFuelService.DrainBunkers`,
`Multiplayer/Ship/Fuel/ShipFuelBunkerPolicy.cs`). No new verb, no new prompt, no
new prefab, and nothing that can misdescribe itself because nothing new is
described — opening a ship container and moving an item into it both already
worked. The sky core is now served **no verb at all**, the same treatment the
personal reviver gets for the same reason.

A **wire rule** rides with it: the bunker only feeds a tank that is a full
canister (25) short. At 0.25 fuel/s an unthresholded drain would push a
container's `1081` at ~0.25 Hz for a whole flight, on an entity that rides a
moving ship — the traffic class §12/the multiplayer-safety rule warns about. It
cannot strand anybody: an empty tank has ten canisters of room.

**Consequence to hold onto:** a hull with a core and **no container** cannot be
refuelled. `WAREBORN_FUEL_GATES_THRUST` must therefore stay **off** until either
such a hull is excluded from metering or the low-fuel warning exists. It was
already off for the second reason.

**SHOULD THE ATLAS PULSE BE IMPLEMENTED? Yes — and it is now unblocked rather
than merely possible.** The client half is complete and already on every hull;
the server half is one serve branch for `1306` plus a cooldown ledger that is a
copy of `Horns` + one `DeferredActions.AfterKeyed` push of `activeTime = 0`
(`TimeEstimationSmoother.StepAndSmooth` never writes `smoothed`, so the server
**must** push the zero explicitly to end a pulse). Two blockers, both stated
rather than guessed:

- `1306` currently sits in `ComponentAbsencePolicy.KnownAbsentComponentIds`
  precisely so an unhandled id cannot drop the ship's interest batch. It must be
  **served**, not merely authored — and that file is owned by
  `fix/component-init` right now, so this needs coordinating, not editing.
- Pre-flight: `ShipPulseEffect.Awake` hard-requires `PulseParams` and leaves
  `_pulseFx` null when it is missing, which `Update()` then dereferences every
  frame. Confirm `atlasPulseParams` survived into the exported ShipFrame prefab
  first. Encouraging: the sibling `PersonalRespawnerPulseEffect` reads
  `enabled=1` in the prefab census on all three ShipFrames.

---

### 13.11 THE TANK WAS IN THE WRONG PLACE — settled from the shipped assets, 2026-08-20

**Everything above in §13.4, §13.5, §13.6 and §13.10 about *where fuel is stored*
and *where you refuel* is superseded by this subsection.** The component analysis
(§13.1–§13.3), the gauge fix, the canister yield and the flight seam (§13.7) all
stand unchanged.

#### What was wrong, and why the mistake was reasonable

§13.4 concluded fuel had to be per-HULL because "the 349-name client entity
prefab census contains `fuelgauge`, `fueldeposit`, `fuelextractor`,
`fueleggspawnerequip` and `egg` — and **no ship fuel tank**".

That search was for the words *fuel tank*. The prefab is called
**`PowerGenerator01`**, and it is **line 219 of the same census**. It has been in
`LoosePartCatalogue` (rows 335–336, two schematic keys over one prefab) since the
catalogue existed, craftable off the Engines knowledge branch, mountable on a
deck — and completely inert, with an empty `functional` array and a
`PartInteractionPolicy` note reading *"powerGenerator(01) — 'generator' as a ship
part is the sky core module; no component, no verb."*

That note was wrong, and the reason it survived is worth recording because it
will recur: **the decompile has no `PowerGeneratorPreprocessor`.** Every other
verb verdict in `PartInteractionPolicy` was derived from a preprocessor
(`ShipContainerPreprocessor.SetVerb(Inventory)`, `ShipCorePreprocessor.SetVerb
(Activate)`, …), so "no preprocessor" was read as "no verb". But a preprocessor
is an **export-time editor script**. What matters is what it left in the shipped
prefab, and nobody had opened the prefab.

#### What the shipped prefab actually contains — PROVED

A UnityPy census of `~/Games/WorldsAdrift/UnityClient@Windows_Data`, reading the
raw `MonoBehaviour` blobs (the assets carry no type trees, so the serialized
fields are parsed by offset):

| prefab | `InteractiveObjectVisualizer.Verb` | `TutorialHelper._interactionStep` |
|---|---|---|
| `Helm01_unityclient` | **3 = Man** | — |
| `ContainerSmall_unityclient` | **4 = Inventory** | — |
| `Sail01_unityclient` | **1 = Activate** | — |
| `CoreMain_unityclient` | **1 = Activate** | — |
| **`PowerGenerator01_unityclient`** | **1 = Activate** | **17 = `MOUSE_OVER_GENERATOR`** |

The first four are the ground truth `PartInteractionPolicy` already held from the
decompile, and **all four match** — that is what makes the fifth row
trustworthy rather than a guess about byte offsets.

Now follow `InteractiveObjectVisualizer.GetTutorialStep`. Its `Activate` arm
tries `_sail`, `_respawner`, `_lamp`, `_horn`, `GetComponent<ShipCoreVisualizer>()`
in that order, and the generator has **none** of them, so it falls through to
`_tutorialHelper.InteractionStep` = `MOUSE_OVER_GENERATOR`. That step's overlay
asset is `STANDARD_MOUSE_OVER_GENERATOR`
(`docs/research/loop/data/tutorial-content.json`), and it carries exactly one
control:

```
{ Type: ONE_BUTTON, Anchor: MIDDLE, Name: "Refuel", Hold: true, InputButtons: ["Interact"] }
```

**The shipped client has always had a prompt on the power generator that reads
"Refuel".** `TutorialStep.MOUSE_OVER_GENERATOR` was noted in §13-era research as
"defined but never referenced anywhere else in the codebase" — true of the C#,
and misleading, because it is referenced from a **serialized field on the prefab**.

#### Which WIKI claims survived

| claim | verdict |
|---|---|
| The Power Generator IS the fuel tank | **SURVIVES**, and is now the strongest-evidenced claim in the subsystem. The prefab bakes an Activate whose label is "Refuel" |
| A standard generator holds **100** units | **SURVIVES**, and gains a second source: `FuelGaugeVisualizer.cs:56` initialises the needle with `SetFuelAmount(0f, 100f)` — the only `100f` near fuel in the decompile, and the capacity the instrument assumes before a server speaks to it. Promoted from WIKI to RECOVERED |
| Multiple generators **pool** their capacity automatically | **SURVIVES in effect, not in mechanism.** Retail's ship root aggregated `AccumulatedData.field5_fuel_tanks` (a `Map<EntityId, FuelData>`) and showed the gauge the sum, which is pooling. But it is **NOT** `1106.subtanks`: nothing in the shipped client reads that field (zero non-gencode hits across `acs/` and `ecs/`), so it is a server-side-only int and reproducing it would be inventing a number with no observable consequence. We pool by **summation over mounted generators** and say so |
| Refuel by **dragging** fuel from inventory onto the generator | **HALF FAILS.** The *interact* half is real and is now implemented. The **drag** half is not reachable: the only client path that moves an item to another entity is `InventoryModificationBehaviour.RequestCrossInventoryMoveItem`, whose destination must carry `InventoryState` and open an `InWorldInventoryVisualiser` — added only by the three container preprocessors. `PowerGenerator01` has no `InWorldInventoryVisualiser` and bakes `Activate`, not `Inventory`. `FuelTankState` (1106) additionally has an **empty `Commands` and empty `Events` block**, so there is no message a client could send it. The gesture is *hold Interact*, and the server decides what moves |
| Fuel pods hang from island hooks, salvaged with the gauntlet | **ALREADY TRUE HERE** (§13.1), unchanged |
| A canister can be **hit several times** before dropping | **ALREADY TRUE HERE**, and the premise that it is not is wrong: `FuelCanisterRegistry.Hit` counts shots and `FuelCanisterYield.Schedule` is `{8, 8, 9}` over **three** shots, with `Depleted` only on the third. The "yields more if caught over land or a ship" half is unmodelled — pods are not physics objects here |
| Pod locations respawn when an **understorm** resets the island | **SURVIVES, and is now RECOVERED rather than WIKI.** Still out of scope for the fuel work, but no longer a dead end: §14.6.3 proves the mechanism (the server raises `SpawnResources{Egg}` on 1010 and the CLIENT re-samples its own island mesh for the new positions), and this repo already implements that handshake. Understorms are planned in §14.10 as S1/S3. Canisters still reset only via `FuelCanisterRegistry.ResetAll` today |

#### What was built

- **The tank is per GENERATOR**, keyed on its entity id
  (`Multiplayer/Ship/Fuel/ShipFuelLedger.cs`). A hull's capacity and level are
  the **sum** over the generators bolted to it, so two generators are twice the
  range. Burn is per SHIP, spread across the pool in mount order, and a hull is
  dry only when **every** generator is.
- **Fuel travels with the generator.** Lifting one off takes its contents; bolting
  it to another ship brings them along. That is what "the generator is the tank"
  implies, so it is pinned by a test rather than left to fall out.
- **Capacity 100 per generator**, RECOVERED (above). `WAREBORN_FUEL_CAPACITY`
  still works but **its meaning changed**: it is now one generator's capacity, not
  a ship's. Anyone with it set in production should re-read it.
- **Refuel is `Activate` on a mounted generator** → `ShipFuelService.TryRefuel`,
  dispatched from `PartInteractionService` alongside the sail, lamp and horn. It
  moves every unit of `"fuel"` the player carries that fits, pool-first so a
  nearly-full ship cannot eat a stack, with an exact `Withdraw` rollback if the
  inventory drawdown then fails.
- **The bunker drain is DELETED** (`ShipFuelBunkerPolicy` and its tests). It only
  ever existed because §13.10 found no honest prompt; there is one now. Removing
  it also removes a per-flight walk of every container on every burning hull and
  its `1081` pushes — the traffic class the multiplayer-safety rule names.
- **The sky core is served no verb**, unchanged from §13.10 and for the same
  reason. Its `Activate` is now free for the Atlas Pulse (1306) whenever that is
  picked up.

#### The safety rule is now STRONGER, not just different

§13.6 keyed metering on the sky core so that "a ship that cannot be refuelled can
never be stranded", and §13.10 then discovered a hole in it: a hull with a core
and no container was metered and unrefuellable. That hole is **structurally
closed**, not patched — metering and the refuel door are now **the same part**. A
hull is metered because a generator is bolted to it, and that generator is
exactly the thing you hold E on. There is no configuration in which a ship is
metered and cannot be refuelled.

**No existing ship can become unflyable, and here is the proof rather than the
assurance.** Metering strictly *shrinks*: a hull is metered today iff it carries a
mounted `atlasSkyCore`, and tomorrow iff it carries a mounted power generator.
Nobody has built a generator, because until now it was an inert prop with no
component and no verb — so **the metered set becomes empty on deploy** and every
ship in the world reverts to `FuelReading.Unmetered`: full static tank, no burn,
no gate. Unmetered *is* the pre-fuel behaviour. A ship can only ever *acquire* a
fuel system by a player deliberately crafting and bolting on a generator, at
which point it starts full and can be refuelled at the same part. This is pinned
by `ShipFuelWiringTests.AHullWithNoGeneratorHasNoFuelSystemAndReadsFull`, which
holds both halves — the `IsGenerator` gate on the mount seam and the
`FuelReading.Unmetered` return on the gauge read — because a mutation run walked
past both before it existed.

**`WAREBORN_FUEL_GATES_THRUST`:** §13.10's first reason for keeping it off (a
metered hull that cannot be refuelled) is gone, and its second (no low-fuel
warning) is much weakened, because the prompt now says "Refuel" in the client's
own words. Flipping it on is still a live-configuration judgement and is left to
the maintainer; this branch changes no default.

#### Nothing new is served, and here is the enumeration

Per the standing rule, before seeding anything:

| component | every class in the decompile that `[Require]`s it | verdict |
|---|---|---|
| **1105** `FuelGaugeState` | `FuelGaugeVisualizer`, and nothing else | **served, unchanged.** Still the only fuel component this server serves |
| **1106** `FuelTankState` | `FuelVisualizer`, and nothing else | **still NOT served, and the generator does not change that.** `ShipPreprocessor.cs:77` attaches `FuelVisualizer` to ship **ROOTS** only — confirmed independently by a UnityPy scan, which finds it on `ShipFrame`, `ShipFrame01` and `ShipFrame02` and on **no part prefab**. So on a generator 1106 would satisfy no reader at all, and on the hull it would wake a visualiser inert since this server started, whose one method `GetFuelPercent()` has zero callers. It buys nothing in either place |
| **1258** `AtlasSkyCoreState` | `ShipLiftVisualizer` | **untouched.** This work neither seeds it nor changes its value. The `AtlasMultiplier = 0.0` cliff is not approached |
| 1210 `InteractiveState` | `InteractiveObjectVisualizer` | already served on every mounted part; the generator's row changes two *values* inside it (verb + availability) and adds no component |

#### What only a live client can settle

1. **That the prompt appears and reads "Refuel".** The asset says so; whether the
   generator's collider is reachable where a player would put it is not
   something a headless server can answer.
2. **That holding E completes.** §13.10 found a `+10 s` hold penalty applied to
   parts on a ship the client thinks is unfriendly; that root cause was fixed with
   the container lock (§11.10), but this is the first Activate part added since.
3. **That the needle moves after a refuel.** Nothing headless renders a
   `GaugeRoller`, and it lags ~2 s by design (§13.3).
4. **Whether 100 per generator is the right size in play.** It is recovered, so it
   should be left alone unless it is actually miserable; `WAREBORN_FUEL_CAPACITY`
   is the knob if it is.

---

## 14. STORMS — the recovered cycle, and the go/no-go

*Written 2026-08-20 on `feat/storms`. This section SUPERSEDES §5's PHASE 7
("Storms, lightning and the Blight") and corrects §5 PHASE 6's claim that wind
walls depend on weather cells. Every non-obvious claim is labelled
**PROVED** (read in the decompile or this repo's code), **RECOVERED** (a retail
constant or field recovered from shipped data), **WIKI** (community source,
weakest class, undated), or **WAREBORN TUNING** (ours, invented).*

### 14.0 THE VERDICT — GO, and Phase 7 was blocked on the wrong thing

**Yes, this can be built, and most of it needs no weather lattice at all.**

PHASE 7 as previously written conflated **three unrelated systems** under the
word "storm" and inherited the blocker of the hardest one. Separated:

| system | component | blocked on the 1139 lattice? | verdict |
|---|---|---|---|
| **Understorm** — the island lightning event that resets resources | **1254 `IslandLightningTimerState`** | **NO** | **REACHABLE NOW. We already serve 1254.** |
| **Weather walls** — wind/storm/sand rifts | **1204 `WallSegmentState`** | **NO** | **REACHABLE.** One `[Require]`, geometry already imported |
| **Blight** — the debris/server-load storm that eats ships | 1269 `RadialStormState` + 8065 `Blueprint` | **NO** | **PROBABLY REACHABLE WITH NO CLIENT MOD** — see 14.3.3, corrected. Its blocker was a FALSE ZERO |

The maintainer's memory — *"storms … that's what would respawn or refresh nodes
on an island"* — is **CORRECT, corroborated four ways**, and the mechanism is
recovered end to end. It is a gameplay-loop feature, not weather decoration.

**The single strongest piece of evidence is client-side, not wiki.** The
understorm's ambient rumble loop in the shipped client is literally named
**`Play_IslandRespawn_Start` / `Play_IslandRespawn_Stop`**
(`acs/IslandLightningTimerVisualizer.cs:129`) — **PROVED**. Bossa's own audio
event for the understorm calls it *island respawn*. The understorm **is** the
resource refresh, named as such in the binary.

**We currently have NO respawn at all for metal deposits, metal nodes, fuel
canisters, loot chests or atlas shards** (PROVED — a targeted `respawn|regrow|
cooldown|refresh` sweep of `NodeRegistry.cs`, `MetalHarvest.cs`,
`FuelCanister.cs`, `LootContainerLedger.cs`, `DatabankLedger.cs`,
`AtlasShardRegistry.cs` returns zero hits). Only trees regrow, on a per-tree
5-minute timer, and `TreeHarvest.cs:277-298` already says in prose that the
shape is wrong and names `DueRespawns` as the seam an understorm should take
over. This is a real, live gap with a real recovered mechanism behind it.

---

### 14.1 THE TAXONOMY — three storms, not one

The wiki's `Weather` page has exactly three top-level sections and they are
three different systems (**WIKI**,
`docs/research/archive/worlds-adrift-wiki/pages/Weather.wikitext:3,24,32`):

1. **Weather walls** (`:3-22`) — *permanent geometry*. Static biome boundaries.
   You fly *through* them. Wind Wall, Storm Wall, Sandwall.
2. **Understorms** (`:24-30`) — *transient, from below, per island*. Lightning
   strikes the island's underside. Reset resources, damage shipyards, bury and
   surface loose objects. Under a minute.
3. **The Blight** (`:32-38`) — *transient, radial, roaming*. Triggered by debris
   accumulation (later: by server time dilation). Disintegrates loose objects
   and eventually ships. A couple of minutes.

A fourth exists only in patch notes: **impassable storms at the world edge**
(**WIKI**, `Beta_0.1.1.1.wikitext:17,37`). `WorldEdgePushback` is separate and
already understood (§12.6).

**"Understorm" and "storm" are not synonyms.** Bare "storm" in the wiki usually
means a *storm wall*. Retail's own patch notes distinguish them: `Update_31`
line 86 tunes "**lightning storms**" per biome and line 90 tunes
"**understorms**" globally, four lines apart (**WIKI**,
`Update_31.wikitext:86,90`).

---

### 14.2 THE UNDERSTORM — the full cycle, recovered

#### 14.2.1 What it is

> "These storms occur with varying frequency all across the map **beneath the
> islands**. It appears that they are most common with islands at **lower
> altitudes**. They usually last for **less than a minute** but affect various
> things on the island in the process."
> — **WIKI**, `Weather.wikitext:26`

In-fiction cause: lightning from the death clouds below electrifies the atlas
stone inside an island, which is what makes islands float at all (**WIKI**,
`Weather.wikitext:29`).

#### 14.2.2 The component — one, and we already serve it

**1254 `IslandLightningTimerState`**, namespace **`Bossa.Travellers.Loot`** —
note the namespace; the understorm lives in the *loot* schema, not the weather
schema (**PROVED**,
`gencode/Bossa.Travellers.Loot/IslandLightningTimerStateData.cs`):

| field | type | meaning |
|---|---|---|
| `estimatedMilliTillNextLightning` | `int` | ms until the storm starts — **the telegraph** |
| `estimatedMilliTillLightningEnd` | `int` | ms until it ends — **the storm switch** |
| `nextLightningTimestamp` | `long` | absolute start |
| `lightningEndTimestamp` | `long` | absolute end |
| `isLightningActive` | `bool` | **DO NOT SET TRUE — see 14.8.2** |
| `generation` | `int` | storm cycle counter |
| `entitiesToInformOfStormStart` | `List<EntityId>` | server-side fan-out list; **zero client readers** |

We seed it today at `ComponentsSerializer.cs:1780-1790` with
`estimatedMilliTillNextLightning = 50*1000`, `estimatedMilliTillLightningEnd = 0`,
`isLightningActive = false`. It is **not** in `ComponentAbsencePolicy`.

#### 14.2.3 The client half — one `[Require]`, and the mechanism of the old comment is now proved

`acs/IslandLightningTimerVisualizer.cs` (**PROVED**, read in full):

- **`[Require]` set: exactly one — `IslandLightningTimerStateReader`** (`:209`).
  No `BlightLocalComponent`. No 1139. No 1269. No authority grant. This is the
  cheapest storm surface in the game.
- **`IsLightningActive => base.enabled && _state.EstimatedMilliTillLightningEnd > 0`**
  (`:226`). **This is the storm switch, and it is the exact field our seed pins
  to `0` with the comment `"must be 0 or you will set the island into a storm"`.**
  That comment was empirical; the mechanism behind it is now established.
- **The telegraph, and it is already wired.** From `:161-196`: when
  `IsLightningActive || EstimatedTimeUntilLightningStarts < 30f`, and the camera
  is within `sqrDistanceToBounds < 90000` (**300 m** of the island), the client
  adds an `AmbientCameraShake` whose magnitude ramps `InverseLerp(30, 0, t)` and
  starts the `Play_IslandRespawn_Start` audio loop. **A player within 300 m of
  an island gets a rising rumble and camera shake over the final 30 seconds,
  for free, with no client change.** Our current seed of 50 s keeps it silent.
- **The strike, and it strikes upward.** During the storm, every
  `Lerp(_minTimeBetweenLightningSeconds, _maxTimeBetweenLightningSeconds, roll)`
  seconds (`_max` serialized default `1f`, `:216`), the client picks a random
  point on its own island surface via `IslandSurfaceData.FindPlace` and draws a
  bolt from `WorldBoundsDataVisualizer.MinHeight` **up to** that point, ±3 m
  jitter (`:140-150`). Bottom-to-surface — exactly the wiki's "struck from
  below". Plus `LightningStrikeSfxController.OnLightningStrike` and
  `LightningPathCreator.CheckCameraShake`.
- **The VFX ships.** `Resources.Load<GameObject>("LightningStrike")` (`:36`);
  `LightningStrike` appears **23×** in
  `UnityClient@Windows_Data/resources.assets` (**PROVED**, `grep -ac`, binary-safe).

**So the entire understorm presentation — warning rumble, camera shake, audio,
upward lightning bolts, and their end — is driven by two integers on a
component this server already sends.** Nothing else is needed to make an
understorm visible.

#### 14.2.4 The cycle, assembled

| stage | value | provenance |
|---|---|---|
| **cadence** | every **1.5–2 h** per island | **WIKI**, `Islands.wikitext:8` |
| **cadence, late retail** | "Understorms now happen **twice as often**" (Update 31, the last content patch) → ~45–60 min | **WIKI**, `Update_31.wikitext:90` |
| this repo's recorded constant | `TreeHarvest.UnderstormCadence = 105 min` | **PROVED**, `TreeHarvest.cs:298`, pinned by `TreeRespawnTests.cs:347` |
| **spatial bias** | commoner on **low-altitude** islands | **WIKI**, `Weather.wikitext:26` |
| **warning** | rising rumble + camera shake in the last **30 s**, within **300 m** | **PROVED**, `IslandLightningTimerVisualizer.cs:161-167` |
| **duration** | "less than a minute" | **WIKI**, `Weather.wikitext:26` |
| **strike rate** | ~1 bolt/s at the shipped `_maxTimeBetweenLightningSeconds = 1f` | **PROVED**, `IslandLightningTimerVisualizer.cs:216` |
| **area** | the island. Not a radius — the visualiser is *on the island entity* and samples *its own* surface | **PROVED**, `:239-240` |
| **movement** | **none.** An understorm does not travel; it happens to one island | **PROVED** (no position in 1254) + **WIKI** (silent) |
| **end** | `estimatedMilliTillLightningEnd` reaches 0 | **PROVED**, `:226` |

**Understorms do not move.** That is a genuine finding and it makes the feature
far cheaper than "a storm that forms and travels".

---

### 14.3 THE BLIGHT — recovered in full, and blocked on a client mod

Included because it is the other half of "the full cycle of storm", and because
the recovered tuning is worth keeping even though we cannot ship it yet.

#### 14.3.1 What it is

> "These storms occur when an island accumulates **excessive amounts of
> debris**; such as ship parts, wreckage and fallen resources. … a **large
> column of dust** engulfing the island from above or below. While within the
> storm, **player visibility will be drastically reduced**, and any loose debris
> or objects will be **gradually disintegrated by orange electrical
> discharges**." — **WIKI**, `Weather.wikitext:34`

**By Update 30 it had stopped being weather and become a server-load valve**
(**WIKI**, `Update_30.wikitext:12,16,18`):

> "Blight will now turn on if the server hits **75% time dilation for 30
> seconds** and disable when it gets to **85% time dilation for 30 seconds**"
> … "Initial Blight position is **no longer based on weather cell positions**
> but on a **K-Means cluster analysis** of the priorities and positions of all
> of the entities on the FSIM" … "It has a maximum speed that varies based on
> time dilation. **So the worse the FSIM is performing the more aggressively
> the Blight will chase ships!**"

That is a garbage collector with a dust column painted on it. Worth knowing
before anyone builds it as weather.

#### 14.3.2 The tuning, RECOVERED verbatim from `acs/BlightConfig.cs`

Every one of these is **RECOVERED**, not invented:

| field | value | line |
|---|---|---|
| `MaxRadius` | **500 m** | `:10` |
| `ActivationRate` / `DeactivationRate` | `1/60` weight per second (ramps 0→1 over 60 s) | `:13,16` |
| `DestructionDuration` | **10 s** (7.5 s fade to black + 2.5 s dissolve, colliders off halfway) | `:25`, **WIKI** `Update_30.wikitext:10` |
| `RecentlyCheckedOutDuration` | 120 s immunity after becoming physical | `:28` |
| `RecentlyCreatedDuration` | **3600 s** immunity for docked entities | `:31` |
| `MaxSpeedAtNoTimeDilation` / `AtFullTimeDilation` | **5 → 30 m/s** | `:37,40` |
| `StartupGracePeriod` | 30 s | `:43` |
| `ActivateTimeDilation` / `Deactivate` | 0.75 / 0.98 | `:46,49` |
| `RetargetEmptyBlightsDuration` | 30 s | `:55` |
| `MaxRetargetDistance` | 1000 m, else it deactivates | `:61` |
| `OwnedImportanceMultiplier` | 2× | `:64` |
| `Top` / `Bottom` | **+1000 m / −500 m** | `:68,71` |

#### 14.3.3 ⚠ CORRECTED — the Blight's blocker was a FALSE ZERO

**This subsection originally said the Blight was blocked on a client mod. That
was wrong, and it was wrong for exactly the reason this project keeps naming:
a search that found nothing, in a tree that could not have contained the answer.**

`/home/ttanurhan/Games/WAReborn-decompiled/` contains `acs/`, `ecs/`, `gencode/`
and `sdk-decomp/`. It does **not** contain **`WASystems.dll`** or
**`SpatialTranslator.dll`**, both of which are present in the shipped client at
`UnityClient@Windows_Data/Managed/` (**PROVED**, `ls`). Those two assemblies hold
`BlightLocalComponent`, `RadiusLocalComponent`'s writer,
`ApplyBlueprintLocalComponentsS`, `SpatialRuntimeWrapperS`, `RadialStormStateC`,
`WeatherCellGenesisS`, `CantorPairUtils` and `RecomputeWeatherCellStatesS`.

**Every prior weather/Blight search of the decompile tree — including the one
that produced PHASE 7's blocker, and this section's first draft — returned a
false zero on those symbols.** Re-run anything that matters against the DLLs
(`ilspycmd -t <Type> <dll>`, or `grep -a` on the binary).

##### What is actually true

The `[Require]`/filter analysis stands: `BlightViewSystem`'s filter is
`_radialStorms.Has ∧ _blights.Has ∧ _remappedPositions.Has ∧ _radii.Has`
(**PROVED**, `acs/BlightViewSystem.cs:59`), and no C# in `acs/` calls
`AddComponent<BlightLocalComponent>`. What was missed is that **the attacher is
data, and the data ships.**

**A TextAsset named `Blight` is inside the shipped
`UnityClient@Windows_Data/resources.assets` at byte offset `567382447`**
(**PROVED**, dumped verbatim — the earlier "no `Blight` blueprint ships" claim
came from a `find` for `*blueprint*` filenames, which a TextAsset inside an
`.assets` file will never match):

```json
{
    "Components": {
        "BlightLocalComponent, WASystems": { },
        "BlightPlayerImportanceDestroyed, WASystems": { "Value": 0 },
        "Bossa.Travellers.Weather.RadialStormStateC, SpatialTranslator": {
            "Weight": "0", "ComponentWriteAccess": "physics" },
        "Improbable.Corelib.Entity.PrefabC, SpatialTranslator": { "Name": "Blight" },
        "Improbable.Entity.Physical.TagsDataC, SpatialTranslator": {
            "Tags": [ "LongDistanceCheckout" ] }
    },
    "InheritBlueprints": ["RateLimitedTransform"],
    "EntityReadAccess": ["physics","visual"]
}
```

**`"visual"` is the UnityClient's own attribute** (`acs/Improbable.Unity.Core.Acls/
CommonAttributeSets.cs:9`). The Blight entity was readable by players by design.

And the client re-applies it. `SpatialTranslator.Systems.ApplyBlueprintLocalComponentsS`
filters on `_blueprints.AddedThisFrame`, and for each new
**`8065 Blueprint = { string identifier }`** calls
`GetBlueprint(identifier).ApplyBlueprint(entityIndex, ExclusionTags)` with
`ExclusionTags = { "Spatial" }` — so `RadialStormStateC` (tagged `Spatial`) is
skipped and arrives over the wire, while the **untagged**
`BlightLocalComponent` is attached **locally on the client**. It is
unconditionally prepended to the subsystem list in
`SpatialRuntimeWrapperS.OnInitialize()`, so it runs whatever `ecs_config.json`
says. `FlagComponentStore.ReplaceComponentData` → `ReplaceComponent` →
`Has.SetTrue` (**PROVED**, `ecs/BossaECS.Core.Component/FlagComponentStore.cs:64-98`).

##### The consequence: a server-driven Blight, with no client change

**We already serve 8065.** `ComponentsSerializer.cs:200` hands every entity
`new Blueprint.Data(new BlueprintData("Player"))`. Changing that string on one
entity is the whole attach mechanism.

The chain, each link `[Require]`-checked and each system confirmed present in
the client's seven-system config:

```
server: new entity + 8065 Blueprint{"Blight"} + 190602 TransformState + 1269 RadialStormState{weight}
   -> ApplyBlueprintLocalComponentsS   (prepended, always runs) -> BlightLocalComponent
   -> RemapTransformsCompositeSystem   (in config)              -> RemappedTransformPositionC
   -> BlightUpdateRadiusSystem         (in config, filter = _blights.Has AND
                                        _radialStorms.ReplacedWeightThisFrame)
                                                                -> RadiusLocalComponent = weight * 500 m
   -> BlightViewSystem                 (in config)              -> dust column, screen overlay,
                                                                   particles, audio, OSD message
```

Note `BlightUpdateRadiusSystem` is driven by **`ReplacedWeightThisFrame`**, i.e.
by our 1269 weight *updates*. A static weight renders nothing. The radius is
**derived on the client and never replicated** — 500 m at full weight
(`BlightConfig.MaxRadius`).

**And the player-facing text exists, hard-coded, in the shipped client**
(**PROVED**, `acs/BlightViewSystem.cs:160-167`, `OSDMessage.SendMessage`):

> `"You are entering a Blight Storm, your ship and everything on it is at risk of destruction!"`
> `"You are leaving a Blight Storm, your ship is safe... for now..."`

Quote those exactly if they are ever referenced. They fire on the rising edge of
`TelegraphVfxWeight` crossing 1 — i.e. **you are already inside**. There is no
advance warning, no marker, no countdown.

##### What is still unknown, and it is not nothing

1. ~~Whether a `"Blight"` entity survives our AddEntity naming gate.~~
   **RESOLVED — it does.** `blight` is **line 17** of the 349-name
   `client-entity-prefabs.txt`, and `CanResolve` lower-cases before matching
   (`Multiplayer/Ship/ClientEntityPrefabs.cs:79-83`). **PROVED.** (`weathercell`
   is line 338 of the same file.) This is the *third* time this census has held
   an answer an agent designed around the absence of — the power generator was
   line 219.
2. **1269 is in `KnownAbsentComponentIds`** — removal plus a deliberate update to
   `ComponentAbsencePolicyTests.cs:71`.
3. **Destruction stays ours.** The client never deletes anything; the whole
   `DestroyEntitiesWithinBlight` half is server-side, and would be a policy we
   write. That is a feature, not a gap.
4. **This is a moving, streamed entity.** SOAK, emphatically.

**Nothing above has been tested against a live client.** It is a chain of
`[Require]` and filter reads plus one shipped JSON. It is a strong lead, not a
shipped fact.

##### One more false zero worth chasing, not chased here

The shipped **`WeatherCell`** blueprint (same `resources.assets`, offset
≈ `567957692`) grants `EntityReadAccess: ["social","physics"]` — **`"visual"` is
absent**. **INFERRED**, and only from the shipped blueprint file rather than the
live snapshot ACL: retail's UnityClient may never have checked out weather cells
at all, in which case client-side `GetWeatherAt` returned the `(1,0,-2)` fallback
**everywhere, in retail too**, and the wind a player felt came entirely from
storm walls and the world-edge ramps. If that holds it changes §12's wind story
substantially and would mean our "becalmed constant" is closer to retail than we
thought. **Not established. Worth its own pass.**

---

### 14.4 WEATHER WALLS — reachable, and NOT blocked on the lattice

**§5 PHASE 6 item 7 says wind walls are "nearly free once cells exist". They do
not need cells at all.**

`WallSegmentVisualizer` has **exactly one `[Require]`: `WallSegmentStateReader`**
(1204). On enable it sets `transform.forward` from `state.Orientation` and calls
`WeatherWalls.Register(this)` (**PROVED**, `acs/WallSegmentVisualizer.cs:9-22`).
`WeatherWalls` is a pure registry over registered segments — `GetIntensityAt`,
`GetWallWindAt`, `IsInsideStorm`, `IsInsideAnyWalls` — and **never calls
`GlobalWeather.GetWeatherAt`** (**PROVED**, `acs/WeatherWalls.cs`, 239 lines).

`1204 WallSegmentState = { int wallType, int wallId, Vector3d orientation,
float length }`, and 44 typed segments are already imported and drawn on the
admin map. Distribution counted from `docs/research/world-data/wamap-islands.json`:
**Wind Rift 20, Storm Rift 11, Sand Storm 12, World End 1, Typhon 0, Ice Storm 0**.

**Correction to `MapWallPalette.cs:78-80`:** it says the legend gap "is how
Typhon and Ice Storm came to be on the map"; on this data they have **zero
segments** and cannot be on the map. Neither name appears anywhere in the
425-page wiki either — they are MapFile enum entries with no surviving gameplay
description.

Walls are **out of scope for this task** but should be lifted out of PHASE 6 in
a future edit: they are a separate, cheaper, independent phase.

> **✅ BUILT 2026-08-20, branch `feat/wallvis` — phase (A), VISUALS ONLY.**
> All 44 walls are served as `WallSegment` entities carrying `190602` (the wall's
> midpoint) and `1204` (`wallType`, `wallId`, unit orientation, **HALF**-length),
> behind `WAREBORN_WALLS` — default OFF, and off registers nothing at all, so it
> is byte-identical on the wire.
>
> **One entity per wall, not thousands.** `WallData.Add` merges every segment
> sharing a `wallId` into their axial extent, so N collinear segments produce a
> bit-identical distance field to one spanning the same extent
> (`findings-storm-walls.md` §6). Retail's subdivision was interest management
> for a checkout radius we do not have.
>
> **The ambient-bolt cost is now RECOVERED rather than feared** —
> `findings-storm-walls.md` §11b, read off the shipped `level0`: 53.4 km of storm
> wall gives ~0.9 frustum tests per frame and a hard cap of 2 concurrent
> emitters, and the per-frame cap would need >600 km to bind. `WAREBORN_WALL_TYPES=0,3,5`
> remains as a lever, unneeded.
>
> **NOT built, deliberately: `1229`, any force, any damage.** The three wall force
> paths live in `ShipPreprocessor`'s `UnityWorker` branch and are not on our hulls,
> so this applies **zero newtons** and cannot perturb flight or the atlas
> arithmetic. `1229`'s 50 retail values are unrecoverable and the client
> `LogError`s per missing key; a wiring test goes red if it ever appears.
>
> Code: `Multiplayer/Walls/` (all decisions, unit-tested), `Game/WallSegmentWire.cs`,
> and the `1204` + widened `8065` branches in `ComponentsSerializer`.

---

#### 14.4.1 The rest of the lightning family — what each actually needs

Enumerated so nobody re-derives it. `[Require]` sets read directly (**PROVED**),
and "lattice?" is whether the class routes through `GlobalWeather.GetWeatherAt`:

| class | component(s) | `[Require]` count | needs 1139? |
|---|---|---|---|
| `LightningGeneratorVisualizer` | 1222 `LightningGeneratorState = { float rateOfSpawn }` | **1** (reader) | no |
| `LightningAttractorVisualizer` | 1223 reader, 1224 **writer**, 1222 **writer** | 3, **two are writers** → needs client authority over 1222 + 1224 | no |
| `LightningStrikableVisualizer` | 1225 | **1** (reader). Plays one SFX on `HitByLightning` | no |
| `GlobalWeatherDataVisualizer` | 1229 `GlobalWallDataState = { Map<string,float> floatValues }` | **1** (reader) | no |
| `WallSegmentVisualizer` | 1204 | **1** (reader) | no |
| `SandStormAffecteeBehaviour` | 1256 **writer** | `[WorkerType(UnityWorker)]` — **never runs on a player client** | n/a, dead |
| **`StormDebris`** | — | — | **YES** (`:82`) |
| **`WeatherTextureGenerator`** | — | — | **YES** (`:200`) |

So the only two storm classes that genuinely need the weather lattice are the
two purely **cosmetic** ones — the debris flying inside a wall, and the wind
texture. **Every gameplay-bearing storm component is lattice-free.**

`1226 PocketOfLightningWallDataState` and `1227 PocketOfLightningState` have
**zero client consumers** outside gencode (**PROVED**) — like 4346, they were
server-side only.

### 14.5 EFFECTS ON SHIPS AND PLAYERS

#### 14.5.1 Understorm

| target | effect | provenance |
|---|---|---|
| **Shipyards** | damaged **every strike**, destroyed if unrepaired | **WIKI**, `Shipyard.wikitext:49`; balance restored to "correct, higher value" in `Beta_0.1.12.1.wikitext:33` |
| the component behind it | **4346 `TakeDamageFromIslandLightningState = { bool isInitialized, int damagePerStorm, EntityId islandEntityId }`**. **Zero client consumers** → it was server-side only. Note **per STORM, not per strike** | **PROVED** + **RECOVERED** field names |
| **Island turrets** | **fully repaired** by an understorm | **WIKI**, `Update_27.wikitext:26`, `Update_28.wikitext:25` |
| **Loose parts, decking, wrecks** | sink into the island; enough strikes and they emerge from the underside | **WIKI**, `Weather.wikitext:26`; introduced `Alpha_0.0.6.wikitext:21-22` |
| **Chests/containers** | "animate in when understorm lightning brings them out of the ground" | **WIKI**, `Beta_0.1.12.1.wikitext:38` |
| **Empty Makeshift Storage** | destroyed on island reset | **WIKI**, `Makeshift_Storage.wikitext:17` |
| **Ships (hulls)** | **no documented damage.** Parked ships lose *loose* parts; the hull is not said to be hurt | **WIKI**, absence across 25 understorm mentions in 16 pages |
| **Players** | nothing documented, **except**: reviving *inside* the understorm layer (below the islands) means "you will immediately take a bunch of damage and die" | **WIKI**, `Beta_0.1.11.1.wikitext:12` |

**No forced ship movement, no lift loss, no vision loss is documented for
understorms.** Those belong to walls and to the Blight.

#### 14.5.2 Blight

Prefers abandoned/disconnected parts, then player ships; docked ships are **not**
immune and auto-undock when their shipyard is eaten; harvested resources are
disintegrated as they drop; visibility drastically reduced with a directional
screen overlay (**WIKI**, `Weather.wikitext:34-38`, `Beta_0.2.1.1.wikitext:48`,
`Update_30.wikitext:8`).

#### 14.5.3 Walls (for completeness)

Wind wall damages **sails only**; storm wall's **lightning damages parts** and
gusts "can even turn you completely around"; sandwall does **damage over time to
all parts** (**WIKI**, `Weather.wikitext:8,12,22`). Player lore: **bar pipes**
were used as lightning rods — "if you have, say, 20 bar pipes on your ship, it
is less likely the lightning will strike a valuable part" (**WIKI**,
`Bar_Pipes.wikitext:4,7`). That is a second, independent reason to build the bar
pipe part named in §0.0.

`1225 LightningStrikableState`'s entire client behaviour is: on the
`HitByLightning` event, play `"Play_Lightning_Strike_Impact"` (**PROVED**,
`acs/LightningStrikableVisualizer.cs:22-25`). All damage was server-side. One
`[Require]`.

`SandStormAffecteeBehaviour` is `[WorkerType(WorkerPlatform.UnityWorker)]` —
Bossa's FSIM only, **never runs on a player's client** (**PROVED**,
`acs/SandStormAffecteeBehaviour.cs:7`). **1256 is dead to us.** Remove it from
any future scope list.

---

### 14.6 RESOURCE REFRESH — the mechanism, proved twice

#### 14.6.1 The claim, from four independent wiki pages

> "Resources on islands will **reset every 1.5 to 2 hours**, during what is
> known as an **'understorm'**. These storms strike the island from below and
> **replace all the metal ore nodes as well as the scrap piles**. Trees will
> regrow at their own rate but **logs that are on the ground will be removed**
> when an understorm hits." — **WIKI**, `Islands.wikitext:8`

> "**All chests will respawn during the understorm, though not always in the
> same place.**" — **WIKI**, `Islands.wikitext:11`

> "**not all spawnpoints produce a chest every reset** from an
> [[Weather#Understorms]]" — **WIKI**, `Chests.wikitext:4`

> "**Each island reset (caused by Understorms), the locations of the fuel pods
> change**, unlike trees which are placed in the island creator."
> — **WIKI**, `Resources.wikitext:15`

#### 14.6.2 Re-rolled or restored? — per resource, and the answer differs

| resource | understorm does | placement |
|---|---|---|
| **fuel pods** | replaced | **RE-ROLLED** — explicit, "the *locations* change" (**WIKI**) |
| **chests** | respawn | **RE-ROLLED** from the creator's authored spawnpoint set, with a fixed per-island count and not every point used (**WIKI**) |
| **metal ore nodes / scrap piles** | "replaced" | **AMBIGUOUS in the wiki** — it says *replace*, never *relocate*, and never *restore*. See 14.6.3 for the decompile's answer |
| **trees** | **not reset**; they regrow on their own rate, positions author-fixed | fixed (**WIKI**) |
| **logs on the ground** | **removed** | — (**WIKI**) |

#### 14.6.3 The decompile settles the ore question: it was RE-ROLLED, and the CLIENT chose where

**1010 `IslandResourceSpawnerState`** (**PROVED**, field names **RECOVERED**
verbatim from `gencode/Bossa.Travellers.Islands/IslandResourceSpawnerStateData.cs:28`):

```
int metalRocksRequiredToRespawn, int initialMetalRockDeposits,
float metalDepositDensity, float minMetalRockDeposits, float metalOnSurfaceProb,
Map<string,int> metalDepositQuantities, Map<string,int> metalDepositQualities,
int eggsSpawned, List<EntityId> spawnedMetalDeposits
```

Two things fall out immediately:

1. **`metalRocksRequiredToRespawn` is a THRESHOLD, not a timer.** Retail's ore
   respawn was gated on *how many rocks had been mined*, and the understorm was
   the moment it was allowed to fire. **INFERRED** from the field name and type,
   but it is the only respawn-shaped field in the schema and there is nothing
   else it can mean.
2. **`eggsSpawned` sits in the same component.** "Egg" is the fuel-canister
   prefab (`IslandProxyVisualizer.ResourceNames.Egg`, `:17`). **One component
   drives both ore and fuel-pod respawn** — which is exactly what the wiki says
   the understorm did to both.

**The placement path, PROVED end to end** (`acs/IslandProxyVisualizer.cs`, read
in full — its only two `[Require]`s are the 1010 *reader* and the 1011
*writer*):

1. Server raises `SpawnResources { int number, IslandResourceType resourceType }`
   on 1010 (`:142`).
2. Client accumulates `_rocksToSpawn` / `_eggsToSpawn` and, every `_interval`
   seconds, generates `_resourceBatchSize` placements by **sampling its own
   island's LOD0 mesh** — `myIsland.GenerateMetalDepositSpawnRequest()` /
   `GenerateFuelDepositSpawnRequest()`, with a normal filter
   `dot(up, n) > 0.4` and a physics clearance check (`:150-247`).
3. Client replies on **1011 `IslandResourceSpawnerClientState`** with
   `TriggerSpawnResourcesReply(List<SpawnResourceRequest>)` — each carrying a
   world transform and a variant string (`:231`).
4. Server creates the entities.

**So retail's respawn RE-ROLLED placement by construction: the client re-sampled
the surface each time.** That resolves 14.6.2's ambiguity for ore and confirms
it for fuel pods. **PROVED.**

#### 14.6.4 …and this server already implements that handshake

`Multiplayer/IslandResourceHandshake.cs` (199 lines),
`IslandResourceLedger.cs`, `IslandResourceFallback.cs`,
`Game/Gathering/IslandResourceService.cs` (516 lines),
`Game/Components/Update/Handlers/IslandResourceSpawnerClientState_Handler.cs`,
plus the 1010/1011 seeds at `ComponentsSerializer.cs:1838-1891`, plus three test
files. `IslandResourceService.OnIslandInterest` serves 1010+1011, **grants the
peer authority over 1011**, raises `SpawnResources`, retries on a schedule, and
spawns what comes back through a clamped, deduped, AABB-guarded ledger.

**The re-roll channel is already built, tested and wired.** A storm-driven
re-roll is a second caller of `SendRequest`, not a new system.

Two caveats before anyone leans on it:
- It requests **`IslandResourceType.Metal` only** today. Eggs are a one-line
  extension of `SendRequest` plus a spawner.
- Its coordinate guard is **`IslandBounds.Haven()`**, hardcoded
  (`IslandResourceService.cs:111`), so on the release world every reply from a
  non-Haven island is refused. That is why `WAREBORN_METAL_COUNT` reads as
  vestigial in `HANDOVER.md`. **Per-island bounds is a prerequisite for using
  this path on tier-1.**

---

### 14.7 WHAT THIS SERVER ALREADY HAS

This is the reason the verdict is GO rather than "research first".

| piece | where | state |
|---|---|---|
| **1254 served on islands** | `ComponentsSerializer.cs:1780` | live, storm field pinned to 0 |
| **the retail cadence** | `TreeHarvest.UnderstormCadence = 105 min` | recorded, tested (`TreeRespawnTests.cs:347`) |
| **the reset itself, already called an understorm** | `WorldsAdriftRebornGameServer.cs:1697-1722` `ResetHarvestResources()` — its own doc comment reads *"Authenticated operator understorm"* | live, on the admin path |
| the four ledgers it drives | `TreeHarvest.ResetAll` `:752`, `NodeRegistry.ResetAll` `:203`, `MetalHarvest.ResetAll` `:197`, `FuelCanisterRegistry.ResetAll` `:224` | live |
| **the wire half of a reset** | `PushTreeSectionMask` `:810`, `BroadcastNodeReset` `:1742`, `BroadcastFuelCanisterReset` `:1724` | live, per-peer, checkout-gated |
| **depletion is a 1000 m sink, not a delete** | `MetalNodes.DepletedSinkMetres = 1000.0` | a reset is a transform push; **no entity churn** |
| **per-island grouping** | `ResourceInterestService._resourceIslands : Dictionary<long, IslandId>` `:91` | exists; **private**, needs an accessor |
| **a timer queue and a poll loop** | `DeferredActions` (`After`/`AfterKeyed`/`Cancel`); loop `WorldsAdriftRebornGameServer.cs:4610`, ≥20 Hz | live |
| **the re-roll channel** | 1010/1011 handshake, §14.6.4 | live, Haven-bounded |
| **a targeted-reset result slot** | `WorldsAdriftServer/Admin/WorldAdminResult.cs:54` already handles `reset-resources` **with a target** | already there |

**No schema migration is needed, and this is not a judgement call.** Every
depletion fact — tree `SectionMask`/`RespawnDueAt`, node `IsDestroyed`/
`ShotPoints`, deposit `Hits`/`Depleted`, canister `Shots`/`Depleted` — lives in
a plain `Dictionary<long,…>` inside a registry, is rebuilt from the catalogue at
boot, and is lost on restart. `SchemaScripts.cs` has no resource table;
`WorldStateSnapshot` holds only deployables, ships, mounted and loose parts.
**PROVED.**

---

### 14.8 HAZARDS — read all four before writing code

#### 14.8.1 The 1225 / 1235 absence gate

`1225 LightningStrikableState` and `1235 DetachFromParentWhenUnderHealthThresholdState`
are both in `ComponentAbsencePolicy.KnownAbsentComponentIds`
(`:224,227`, set at `:367-396`), and
`ComponentAbsencePolicyTests.cs:71` asserts the exact eight-id set
`{1139, 1269, 1225, 1235, 1306, 1259, 1304, 4323}`. **Any lightning-damage work
must remove an id from that set and update that test**, deliberately and with a
stated reason — the file's own convention is that each removal carries a comment
saying why absence was not safe.

#### 14.8.2 ⚠ THE ISLAND-DROP HAZARD — new, and it is the atlas cliff's shape exactly

`IslandLocalTransformBehaviour.HandleLightningActiveUpdated(bool active)`
(**PROVED**, `acs/Bossa.Travellers.Visualisers.Islands/IslandLocalTransformBehaviour.cs:46-52`):

```csharp
if (active)
    TransformStateWriter.Update.LocalPosition(
        GetEndOfWorldPosition().ToFixedPointVector3()).FinishAndSend();
```

`GetEndOfWorldPosition()` is **End-of-the-World doomsday code** — it lerps the
island's Y toward **−250 m … −1500 m** off `EndOfTheWorldConfig` dates (`:54-85`).

So **`isLightningActive = true` can teleport an island into the depths.**

It cannot today, for a reason that is an *absence*: the method returns the
island's current position early unless **all three** of
`IslandFabricState.OriginalPosition`, `.EndOfWorldDurationMultiplier` and
`.EndOfWorldOutroOffset` have values (`:56-59`), and our 1042 seed leaves all
three as empty `Option`s (`ComponentsSerializer.cs:1833-1835`). It is also
gated behind a `TransformStateWriter` — client authority over the island's
transform, which we do not grant.

**A THIRD ABSENCE, found during S1 and stronger than both** (**PROVED**
2026-08-20, UnityPy MonoScript sweep of all 255 `*@island_unityclient`
bundles): **`IslandLocalTransformBehaviour` is baked onto ZERO of them.** The
island prefab carries `StaticGlobalTransformBehaviour` /
`StaticLocalTransformBehaviour` instead — 17 MonoScripts per bundle, the same 17
on every island, and the drop behaviour is not among them. So on our islands the
drop code is not merely un-enabled; it is not present. (The same sweep is what
closed §14.11.1 — see there for the method.)

**This is the same shape as the atlas cliff and must be recorded the same way:
the safety comes from three absences, and completing 1042's Options, granting
island transform authority, or shipping a bundle that does carry the behaviour
would arm it. Three absences are still three absences.** S1 therefore keeps the
rule absolutely: `IslandStormUpdate` has no bool field at all, the wire never
calls the setter, and two tests — one reflective, one source-reading — go red if
either changes.

**THE RULE, and it costs nothing:** drive the storm **entirely** through
`estimatedMilliTillLightningEnd` and `estimatedMilliTillNextLightning`.
**Never write `isLightningActive = true`.** The visualiser that actually renders
the storm reads the *int*, not the bool (`IslandLightningTimerVisualizer.cs:226`),
so the bool buys nothing and risks everything.

#### 14.8.3 The silent `[Require]` rule

Enumerate what 1254 could *newly* satisfy, not just the one visualiser aimed at.
Only two classes `[Require]` 1254 (**PROVED**, whole-`acs/` sweep):
`IslandLightningTimerVisualizer` (1 require — 1254 alone) and
`IslandLocalTransformBehaviour` (4 requires — `TransformStateWriter` + 1254 +
1042 + 1041). Since we already serve 1254, **this feature adds no component and
therefore satisfies no new requirement**, which is the main reason it is safe.
The 1042/1041 combination is exactly what 14.8.2 warns about; leave it alone.

#### 14.8.4 The lattice prohibition still stands

Nothing in this section touches 1139 or 1269. They stay in
`ComponentAbsencePolicy` for the measured reason (31,144 client errors in 158 s).
A **partial** lattice remains worse than none, because `GlobalWeather.GetWeatherAt`
interpolates four cells and the no-cell fallback is already a clean uniform wind
`(1,0,-2)`, pressure `0.5`. **Do not "complete" anything here.**

---

### 14.9 THE PREREQUISITE CHAIN

```
S1  timed island understorm: reset + presentation   ── needs NOTHING new
     ├── 1254 already served
     ├── ResetHarvestResources() already exists
     └── no migration, no new component, no client mod

S2  per-island scoping                              ── needs S1
     └── an accessor on ResourceInterestService._resourceIslands

S3  re-rolled placement (ore + fuel pods)           ── needs S2
     ├── the 1010/1011 handshake (BUILT)
     └── per-island IslandBounds (NOT built - Haven is hardcoded)

S4  understorm damage to structures (shipyards)     ── needs S1
     └── a server-side structure damage model (DOES NOT EXIST)

S5  lightning strikes a ship                        ── needs S4
     ├── remove 1225 from ComponentAbsencePolicy + update its test
     └── a ship-part damage model (DOES NOT EXIST; 1235 also absent)

S6  weather walls                                   ── needs NOTHING
     └── independent of everything above; belongs in its own phase

S7  the Blight                                      ── NO CLIENT MOD (14.3.3, corrected)
     ├── serve 8065 Blueprint{"Blight"} on a new entity  (we already serve 8065)
     ├── serve 1269 + PUSH weight updates (a static weight renders nothing)
     │     └── remove 1269 from ComponentAbsencePolicy + update its test
     ├── "Blight" is line 17 of client-entity-prefabs.txt - it RESOLVES
     └── a server-side entity-destruction policy (the client deletes nothing)
```

**Weather (PHASE 6 / the 1139 lattice) is a prerequisite for NONE of S1–S7.**
That is the headline correction to PHASE 7.

---

### 14.10 THE PHASED PLAN

#### S1 — The understorm, server-side, presentation + reset — **BUILT, `feat/understorm-s1`, not deployed, not yet seen by a human**

Server-only. No migration. No new component. No client mod. No patcher release.
All six items below shipped, plus a seventh the plan did not anticipate. What
the plan got wrong once the code was in front of it:

- **The plan's item 3 undercounted the countdown pushes, and the reason is a
  client bug.** "A low-rate countdown refresh" is not optional decoration — it
  is the only thing that makes the warning exist, and each push must move the
  value by **more than 7 s** or the client discards it. See §14.11.5. A
  once-per-storm push would have shipped an invisible feature that passed every
  test in the plan.
- **The plan's per-island schedule (item 2) and its world-wide reset (item 4)
  contradict each other in a multi-island world.** `ResetHarvestResources()` is
  global, so firing it at *each* island's storm end resets the whole world once
  per island per cadence — eleven of twelve of those while the island in
  question is calm. S1 therefore keeps the jittered per-island **presentation**
  and fires **one** reset per generation, at the *last* island's storm end.
  S2 replaces that with a per-island reset and the contradiction goes away.
- **A seam the plan missed entirely: the SEED.** Updates only reach peers that
  already hold the component, so a player logging in mid-storm was served the
  static seed — clear sky — and heard nothing until it ended. The 1254 seed is
  now answered from the same schedule the pushes come from.
- **§14.8.2 gained a third absence** (the drop behaviour is on 0 of 255
  bundles); **§14.11.1 and §14.11.2 are closed**; **S3 gained an unrecorded
  prerequisite** (the metal handshake is off in production). All recorded above.

Everything else in the plan survived contact unchanged.

1. **`IslandStormService`** ticked on the main loop beside `TickTreeHarvest()`
   (`WorldsAdriftRebornGameServer.cs:4667`), shaped like `TreeHarvest` — an
   injected `IClock`, `TimeSpan` deadlines, a `DueStorms()` returning a change
   list. **Never count main-loop turns.**
2. **`IslandStormPolicy`** (pure, in `.Multiplayer`, fully unit-tested): the
   per-island schedule. Proposed knobs, all **WAREBORN TUNING** except the
   cadence:
   - `WAREBORN_STORM_CADENCE_SECONDS`, default **6300** (105 min,
     `TreeHarvest.UnderstormCadence`, **RECOVERED**)
   - `WAREBORN_STORM_JITTER_FRACTION`, default `0.2` — so islands do not all
     fire together, honouring "varying frequency" (**WIKI**)
   - `WAREBORN_STORM_DURATION_SECONDS`, default **45** ("less than a minute",
     **WIKI**)
   - `WAREBORN_STORMS`, default **0** (off) for the first deploy
3. **Presentation.** Each tick, push 1254 to peers with that island checked out:
   `estimatedMilliTillNextLightning = ms to start` (so the last 30 s telegraph),
   `estimatedMilliTillLightningEnd = ms remaining` (0 outside a storm), and
   `generation++` per cycle. Use the `PushTreeSectionMask` pattern verbatim —
   dereference the stored ref, `ApplyTo(stored)`, send one field, **never**
   route through `RelayToOtherPlayers`, **never** send `Data.ToUpdate()`.
   **`isLightningActive` stays `false` for ever** (14.8.2).
4. **The reset, at storm END** (the wiki's objects "emerge from the ground"
   *during* the storm; resetting at the end is the honest simplification and is
   **WAREBORN TUNING**). Reuse `ResetHarvestResources()`'s body.
5. **Trees ride the storm.** Per `TreeHarvest.cs:290-296`'s own instruction,
   `DueRespawns` becomes "reset every stand" called by the storm. Keep
   `WAREBORN_TREE_RESPAWN_SECONDS` working so an operator can revert.
   **`ResetAll` already skips felled logs** (`TreeHarvest.cs:761`), which
   matches the wiki ("logs on the ground will be removed") more closely than it
   matches "restore" — leave that behaviour and say so.
6. **Placement is RESTORED, not re-rolled, in S1** — and this must be stated as
   a **known divergence from retail** (§14.6.3 proves retail re-rolled), not
   quietly shipped. S3 closes it.

**Rate:** one 1254 update per checked-out island per state change — start,
end, and a low-rate countdown refresh. This is not a relayed per-frame
component. A soak is still required (see 14.11).

**Mutation tests required** (hard rule 9 — this repo has twice shipped a green
suite over an unplugged feature). At minimum, break each of these one at a time
and confirm exactly the intended test goes red: delete the service tick; no-op
the 1254 push; no-op the reset call; write `isLightningActive = true`; drop the
`generation` bump; make the countdown never cross 30 s; let the reset fire at
storm *start* instead of end; regrow a felled log.

#### S2 — Per-island scope — **BUILT, `feat/understorm-s2`, not deployed**
Storms are per island (**PROVED** — the visualiser samples *its own* surface).
The plan's two named seams were both right and both used: a public accessor over
`ResourceInterestService._resourceIslands` (`IslandOf` / `ResourceIslands`) and a
per-island variant of `ResetHarvestResources()`
(`ResetHarvestResourcesOn(IslandId)`). Each of the four ledgers gained a
`ResetAll(Func<long,bool> include)` overload; the no-argument form still means the
whole world and is still what the operator's `reset-resources all` runs.
`IslandStormPolicy.WorldResetAt` / `DueWorldResetGeneration` became `ResetAt` /
`DueResetGeneration` over *that island's* phase offset, and the "last island"
special case is gone.

**MEASURED, headless, at production's exact configuration** (tier1 = 47 islands,
cadence 900 s, jitter 0.2, duration 45 s): the worst gap between an island's own
storm START and its own reset is **45.05 s** — the storm's own length plus one
20 Hz loop turn. S1 measured **212 s** on the same configuration. 36 of the 47
resets now land *before the last island has even started storming*; under S1 that
number was 0 by construction.

Three things the plan got wrong or did not say:

- **`WorldAdminResult.cs:54` is NOT a targeted result slot for this.** The
  `TargetEntityId` property exists on the type, but line 54 is the validator
  clause that *rejects* a target on `reset-resources`, and an island is not an
  entity id. More to the point the storm-driven reset never touches that file at
  all — it is the login-server↔game-server operator-command bridge, and
  `AdminWorldCommandPolicy` only parses `reset-resources all`. Nothing in S2
  needed it and nothing in S2 changed it. An island-targeted *operator* command
  would be separate, small work.
- **The reset must not sit behind the 1254 push's early exits.** Resources are
  server-side state; an island whose `AddEntityOp` has not run still owes its
  trees, and skipping the call would also leave that island's
  `LastResetGeneration` unseeded, so its first real reset would replay every
  generation it slept through.
- **`_resourceIslands` is EMPTY when spatial interest is off**, because the
  constructor returns before populating it. Production reads
  `WAREBORN_INTEREST_RADIUS_M=120` (**PROVED**, read live 2026-08-20), so the map
  is populated there — but a per-island reset that trusted it unconditionally
  would silently restore nothing on an interest-off server, with every test
  green. `IslandOwningResource` falls back to the same
  `IslandResourceInterestPolicy.ClosestIsland` the map itself is built from.

**One mutation escaped on the first attempt** (hard rule 9, which predicted
exactly this). The scope decision was written inline in the game server, which
has no test project, so it was covered only by source-reading assertions on
`ResetAll(include)` at the call sites. Replacing the whole declaration with
`Func<long,bool>? include = null;` reinstated the world-wide reset — the exact S1
defect — left every one of those strings intact, and the suite passed 4215/0.
The decision now lives in `Multiplayer.Islands.IslandResourceScope.Include`,
where it is unit-tested, and one wiring test reads the single line that hands the
island over.

#### S3 — Re-rolled placement — **BUILT, `feat/storm-s3`, not deployed**

The original plan was: re-request `SpawnResources` for `Metal` **and** `Egg` at
storm end via the existing 1010/1011 handshake, after clearing the island's
ledger. **That route is blocked, and doubly so** — see the box below, which the
S3 work confirmed rather than removed. So S3 re-rolls **the path production
actually runs** instead, and leaves the handshake route untouched for whenever
its two blockers are cleared.

**What shipped.** Placement is now re-rolled per island at that island's own
storm end, with no client mod, no schema migration, no flag flip and no new
component:

- **`HavenSurface.DepositPool()`** — the SAME generator over the SAME surface
  and the SAME `DepositConfig` as the boot layout, run out to a saturating
  target instead of stopping at 40. Haven yields **107 seats** (MEASURED; 1
  anchor + 106 generated).
- **`Islands.IslandResourceReroll.SeatsFor`** — the pure, unit-tested decision:
  which seats are occupied at generation *g*, seeded FNV-1a over
  (island, generation), shuffled by SplitMix64. No RNG, no clock.
- **`MetalDeposits.RerolledNode`** — the whole per-deposit decision in one call.
- **`NodeRegistry.Reseat`** — the first and only writer of a placed node's
  position (see the §4 warning in the S1 live findings).

**Two properties make this safe, and both are asserted:**

1. **Prefix stability.** The generator is a greedy pass over a fixed hash order,
   so a larger target can only APPEND. The pool's first 40 seats are
   byte-identical to today's layout (MEASURED 39/39 plus the pinned anchor), so
   **generation 0 IS the current production world** and nothing changes until
   the first storm.
2. **Every pair in the pool is already ≥22 m apart**, because the pool was
   thinned by that same pass. So *any* subset is a valid layout, the re-roll can
   never produce a rock carpet, and — the point — **there is one placement
   policy, not two that can disagree.** That was S2's lesson applied one layer
   down.

**Deposit identity is carried across; only position moves** (key, metal type,
quality, 1255 variant). That matches the wiki, which says the *locations*
changed, and it keeps "index 0 is always iron" and every resolvable variant id
intact — an unresolvable variant leaves the entity invisible.

**deposit-0 is PINNED** — the hand-measured tutorial rock 8.9 m from spawn.
A new player's first mining lesson must not become a search. **WAREBORN TUNING.**

**LIVE, end to end** (headless soak, cadence 60 s, `WAREBORN_DEPOSIT_COUNT=40`):
`understorm re-roll on haven: moved 39 deposit(s) into generation 1's layout`,
then **38** at generation 2, with `the-trades-challenge` correctly untouched.

**Persistence is coherent without a migration.** Resource positions are not
persisted (PROVED — no resource table in `SchemaScripts`, no resource record in
`WorldStateSnapshot`) and neither is the generation counter, so a restart
returns the world to generation 0 — the boot layout — at the same moment it
returns every mined node to intact. Layout and harvest state reset together;
nothing can survive a restart half re-rolled.

**Scope limit, stated not hidden:** Haven's own static `deposit-N` field only.
`MetalDeposits.HavenIndexOf` returns null for release-world and
Trades-Challenge deposits, which are placed from their own catalogues and have
no seat pool. Extending the re-roll to the release world needs a per-island seat
pool — the same shape of prerequisite the handshake route has. **Trees, fuel
canisters and chests are also not re-rolled yet**; the wiki says trees were
author-fixed anyway, but fuel pods and chests were explicitly re-rolled and are
the natural next increment.

**One mutation escaped, exactly as hard rule 9 predicts, and it was S2's hole
again.** The re-roll was first a loop in the game server that asked for the seat
LIST and indexed it; changing `NodeAtSeat(index, seats[index])` to
`NodeAtSeat(index, index)` moved not one rock, printed no log line, and left all
4252 tests green, because the untestable assembly was guarded only by string
matches on `SeatsFor(` and `NodeAtSeat(` — both of which still appeared. Fixed
structurally: the arithmetic moved into `MetalDeposits.RerolledNode`, and a
`DoesNotContain` keeps `SeatsFor` out of the game server so the hole cannot
reopen.

> ⚠ **S3'S TWO PREREQUISITES ARE BOTH STILL UNMET — they were not worked around,
> they were routed around.** The 1010/1011 handshake remains the more faithful
> mechanism (retail's client re-sampled its own mesh), and it is still available:
>
> 1. **The flags are off.** Re-read live 2026-08-20: `WAREBORN_METAL_HANDSHAKE=0`
>    and `WAREBORN_SPAWN_METAL=0`. **Why they were turned off is still
>    unrecorded** and should be found before anyone flips them.
> 2. **Per-island `IslandBounds`.** The coordinate guard is `IslandBounds.Haven()`
>    HARDCODED at `Game/Gathering/IslandResourceService.cs:111`, so on the
>    release world every reply from a non-Haven island is refused.
>
> They are also **mutually exclusive with what S3 shipped**: turning the
> handshake on makes `WAREBORN_SPAWN_DEPOSIT=1` a no-op
> (`WorldsAdriftRebornGameServer.cs:3312-3314`) and the static field — the thing
> S3 re-rolls — disappears entirely. So this is an either/or, not a stack, and
> flipping those flags would turn S3's re-roll off. It is a deploy decision for
> the maintainer.
>
> (Same read, for context: `WAREBORN_DEPOSIT_COUNT=40`, not the dangerous default
> of **1** — and that default is worth remembering, because with one deposit the
> only rock in the world is the pinned tutorial one and the re-roll correctly
> does nothing.)

Chest re-roll follows the same shape once loot placement is per-island.

> ⚠ **S3 HAS A SECOND, PREVIOUSLY UNRECORDED PREREQUISITE.** The 1010/1011
> handshake S3 rides is **switched off in production**: read live 2026-08-20,
> `WAREBORN_METAL_HANDSHAKE=0` and `WAREBORN_SPAWN_METAL=0`. §14.12 item 5 left
> this as "worth one read-only check"; the check has now been done and the
> answer is the unfavourable one. So S3 needs **per-island `IslandBounds`
> AND an operator decision to turn the handshake back on**, and whatever reason
> it was turned off is itself unrecorded and should be found before flipping it.
> (For context on the same read: `WAREBORN_DEPOSIT_COUNT=40`, not the dangerous
> default of 1, and `WAREBORN_BUILD=ee86213` — one commit behind main, and that
> commit is docs-only, so production code is current.)

#### S4/S5 — Damage
There is **no** server-side damage model for structures, ship parts or players
(**PROVED**: no `DamageService`/`ApplyDamage`/`TakeDamage` anywhere; 1235, 1225,
4323 all known-absent; `SpawnPlan.cs:87` — *"this server writes no HealthState
so there is no fall damage"*). Understorm shipyard damage is a genuinely new
subsystem and should be sized on its own. Its recovered semantic is
`4346.damagePerStorm` — **per storm, not per strike**.

Do **not** stack these onto S1. A storm that damages ship parts also interacts
with §12's lift arithmetic (a damaged core upgrade stops contributing lift), and
that must be reasoned about deliberately, not inherited.

#### S6 — Weather walls
Independent. Lift out of PHASE 6. One component, geometry already imported.

#### S7 — The Blight
**Corrected: probably needs NO client mod.** The blocker was a false zero over
two assemblies missing from the decompile tree (§14.3.3). The attach path is a
shipped JSON blueprint plus `8065 Blueprint`, which this server already serves.
`"Blight"` **resolves** — line 17 of `client-entity-prefabs.txt` (**PROVED**).
So it is one new streamed entity, a 1269 weight ramp, and a server-side
destruction policy — and the recovered
`BlightConfig` numbers in 14.3.2 mean almost nothing has to be invented.
It stays after S1–S3 because it is a moving streamed entity and wants its own
soak, not because anything blocks it.

---

### 14.11 WHAT ONLY A LIVE CLIENT CAN SETTLE

> **Updated 2026-08-20 during S1.** Items 1 and 2 are **CLOSED** — both were
> settled headlessly with **UnityPy type-tree reads** of the shipped island
> bundles. *The next agent should not redo this.* The bundles are compressed, so
> `grep` structurally cannot see their contents — which is exactly why these two
> questions stayed open through several passes. The bundles **do** ship type
> trees, so `UnityPy.load(bundle)` → `obj.read_typetree()` reads serialized
> MonoBehaviour fields directly. Bundles live at
> `/home/ttanurhan/Games/WorldsAdrift/Assets/unity/*@island_unityclient` (255 of
> them). A fourth item, **5**, was added by the same sweep.

1. ~~That `estimatedMilliTillLightningEnd > 0` actually renders a storm on our
   islands.~~ **PROVED.** `IslandLightningTimerVisualizer` is baked onto the
   island prefab in **255 of 255** `*@island_unityclient` bundles. **Zero
   bundles lack it**, Haven's `1431299145@island_unityclient` included. So
   pushing 1254 reaches a live visualiser on every island we serve. Its
   companions are present too: `IslandSurfaceData` is 255/255, which is what
   `FindPlace` samples to place each bolt.
2. ~~That we cannot read `_maxTimeBetweenLightningSeconds` headless.~~
   **RECOVERED:** `_minTimeBetweenLightningSeconds = 0.0`,
   `_maxTimeBetweenLightningSeconds = 1.0`, identical across every island
   sampled. Retail's shipped strike cadence is therefore a uniform roll in
   **[0, 1] s** — a bolt roughly every 0.5 s, about **90 strikes over a 45 s
   storm**. That is retail's own value, not a guess, and the server cannot
   change it (it is on the prefab; changing it would be a client mod).
   **Whether ~2 bolts/s is *pleasant* is still open** — that is item 4, a
   playtest question — but it is no longer an unknown quantity.
3. **That an island does not move** when a storm runs — the direct check for
   14.8.2. Watch the island's Y. **Still live-only**, though see the third
   absence recorded in 14.8.2: the drop behaviour is not on the prefab at all.
4. **Whether a ~45 s storm every 105 min feels right**, and whether ~90 bolts
   inside it reads as dramatic or as strobing. A playtest question.
5. **NEW, and it is the one that would actually have sunk S1.** That the
   telegraph ramps at all. The client's countdown **does not tick down on its
   own**: `TimeEstimationSmoother.StepAndSmooth()` computes a decayed value and
   **returns it without ever storing it** (`smoothed` is written in exactly one
   place, `OnUpdatedValue`, and only when `warp` is true), and its sole caller
   discards the return value. **PROVED**, read in the decompile. So
   `EstimatedTimeUntilLightningStarts` is a **staircase** that moves only when
   the server pushes a value differing from the held one by **more than 7 s**
   (`Mathf.Abs(num - smoothed) > 7f`). S1 is built around this — the countdown
   is re-pushed on an interval floored above 7 s — but **whether the resulting
   staircase reads as a smooth ramp on screen** (the client's own
   `TimeLerp(_curMag, target, dt, 0.25f)` should smooth it) is a live-client
   question.

**A request for the maintainer, if S1 ships:** stand on Haven within 300 m of
the island with `WAREBORN_STORM_CADENCE_SECONDS=180` and watch for (a) a rumble
ramping in over the last 30 s, (b) bolts striking upward into the island,
(c) the island staying exactly where it is, (d) mined nodes coming back when it
ends.

---

### 14.12 WHAT I COULD NOT ESTABLISH

1. **Understorm radius/movement.** Nothing in 25 wiki mentions across 16 pages,
   and 1254 carries no position. I believe there is nothing to find:
   understorms are per-island, not roaming. Stated as a belief.
2. **Whether ore node *positions* were re-rolled**, from the wiki alone — it
   says "replace", never "relocate". §14.6.3 answers it from the decompile
   instead; the wiki is silent, not contradictory.
3. **The exact trigger.** `metalRocksRequiredToRespawn` says depletion-threshold;
   the wiki says a 1.5–2 h clock. Most likely both — a clock that only fires
   when enough has been mined. **INFERRED**, not proved.
4. **`Typhon` and `Ice Storm`.** Enum names with zero map segments and zero wiki
   text. Nothing survives.
5. **Live production env.** `ssh` to the box was blocked in this session, so
   whether `WAREBORN_METAL_HANDSHAKE` is set could not be read off
   `systemctl show wareborn-game -p Environment`. The code default is ON; the
   Haven-hardcoded AABB (§14.6.4) is the likelier reason it is inert on tier-1.
   **Worth one read-only check before S3.**
6. **Whether retail's own client ever saw weather cells.** The shipped
   `WeatherCell` blueprint does not grant `"visual"` read access. **INFERRED**
   from the blueprint file only; the live snapshot ACL was authored by a tool
   that does not ship. If true it changes §12's wind story.
7. **Stale citations fixed in passing.** §2's row for weather cites
   `ComponentsSerializer.cs:1659-1674` and `:1675-1690`, and
   `ComponentAbsencePolicy.cs:120,146` / `:265-291`. The real locations today are
   **1764-1779**, **1780-1790**, **:151, :177** and **:367-396**.
## 15. SECURITY HARDENING — deferred to end of project, by decision

**Status: ACCEPTED AND DEFERRED, 2026-08-20.** The maintainer's call, recorded
verbatim so a future reader does not re-litigate it: *"document the login and
token rotation stuff in roadmap, that's something we're gonna do at the end, we
know that it was only HTTP from the beginning."*

That framing is correct and it is the reason this is a phase and not an
incident. This server has never had transport security on the account path.
Everything below is a *second* copy of secrets that were already travelling in
the clear. Nothing here newly exposes a password that was previously protected.

**What deferral does NOT excuse, stated so the decision stays informed.** A
plaintext password on the wire exists for one round trip between two endpoints.
A plaintext password in the journal is **durable, on disk, replicated into every
backup, and readable by every operator tool** — and the realistic leak is not an
attacker on the VPS, it is a `journalctl` snippet pasted into a support thread.
The two risks are the same secret and genuinely different exposures. Deferring
is defensible; deferring *because the wire is already plaintext* is the one
argument that does not hold, so it should not be the reason recorded.

**Ordering constraint that makes "at the end" the right call anyway:** the fix
and the remediation are coupled. Gating the log stops *new* capture; it does
nothing about what is already banked. The remediation is a session-token
rotation, which **logs every player out**. Doing that mid-development costs a
disruption per occurrence; doing it once, at the same moment TLS lands on the
account path, costs one. **So the correct sequence is: TLS → gate the log →
rotate once → done.** Rotating before TLS means rotating twice.

### 15.1 The log finding — PROVED, verified directly 2026-08-20

`WorldsAdriftServer/Handlers/RequestRouterHandler.cs:20` calls
`DataParser.ParseIncomingData(buffer, offset, size)` from the raw TCP receive
callback, so the buffer is **unparsed HTTP wire text**, unconditionally, for
every inbound request.

`WorldsAdriftServer/Handlers/DataParser.cs:93` takes its structured branch only
when the bytes contain the literal `{"Id":`. A `POST /login`, `/register` or
`/authenticate` body does not. Everything else reaches `:129`:

```csharp
else //Display raw data if not handled by custom handler
{
    for (int ByteIndex = 0; ByteIndex < size; ++ByteIndex)
        Console.Write((char)buffer[ByteIndex]);
}
```

No env gate, no `#if DEBUG`, no redaction. Into stdout — and therefore journald,
and therefore disk — go plaintext passwords, the `Security:` bearer header
(`Persistence/Accounts.cs:31`) and `Cookie: wa_player=<token>`
(`Handlers/Authentication/LoginHandler.cs:168`). Neither unit sets
`StandardOutput=`.

**This was already written down in this repo and not actioned:**
`docs/research/accounts/findings-signup-page.md:22-24` — *"every sign-up POST
writes the player's plaintext password into the system log. Gate it before
serving any web traffic."* That note predates the finding above. The failure was
not detection; it was that a written-down finding had no owner.

**MEASURED 2026-08-20, and the journal is NOT clean.** Count-only probe against
production, values never printed:

| probe (exact pattern) | count |
|---|---|
| `"password"`/`"passwd"` **JSON key** occurrences — i.e. credential-bearing bodies | **31** |
| lines merely *mentioning* `password` (broader, includes prose/headers) | 331 |
| lines carrying a `wa_player=` session cookie | **10,513** |
| lines carrying a `Security:` bearer header | 691 |
| `POST /login`, `/register`, `/authenticate` | 521 |
| `wareborn-login` journal size | **137 MB**, ~1,369,900 lines |
| span | **2026-08-08 00:08 → 2026-08-20 09:52** — 12 days |
| all journals on the box | **3.9 GB** |

Two probes with different patterns are listed rather than one headline number,
because they disagree for a legitimate reason and the disagreement is
informative: 521 auth POSTs produced only 31 `"password"`-keyed bodies, since
`/authenticate` carries a *token* rather than a password. **31 is the number
that matters for password exposure; ~10.5k is the number that matters for
session exposure.** Do not quote the 331 as "331 passwords" — it is a
substring count over prose and headers too.

*(Method note: the first two probe attempts returned empty and the reason was
mine, not the server's — `J=$(journalctl …)` assigns 137 MB into a shell
variable. Stream it; do not capture it.)*

**What the numbers mean, separately, because they decay differently:**

- **The 11,058 token lines largely self-heal.** Player sessions expire after
  7 days and admin sessions after 12 h sliding (`AccountPolicy.cs`,
  `PlayerSessions.Issue:56-66`). The journal spans 12 days, so most captured
  tokens are already dead; only the trailing ~7 days are live. Rotation kills
  that remainder in one action.
- **The 31 passwords do not decay at all.** They are valid until each player
  changes one, and the realistic harm is not this server — it is **password
  reuse against the player's email or other accounts.** No rotation on our side
  fixes that; only telling the affected players does. That is the part of this
  finding with a duty attached, and it is why the phase should not slip
  indefinitely even though it is correctly *ordered* last.

**Consequence for §15.2: step 4 is NOT optional.** The precondition it was gated
on has been measured and is non-zero. Additionally, `journalctl --vacuum-time`
must cover **archived** journals and any off-box backups, or the gate plus the
rotation still leaves the captured copies in place.

### 15.2 The phase, when it runs

1. **TLS on the account path first** — it is the precondition that makes one
   rotation sufficient.
2. **Gate `DataParser.cs:129-134`** behind an env flag defaulting to off.
   Six-line deletion. TRIVIAL.
3. **Measure the journal**, count-only, before deciding step 4.
4. **Rotate every session token** if step 3 is non-zero. Logs everyone out —
   announce it. Then treat existing journals and their backups as compromised
   and vacuum them.
5. **Delete the admin verifier print**, `Admin/AdminConfig.cs:112-115`, which
   logs `username + ":" + storedHash` — that hash *is* the complete server-side
   verifier. The adjacent comment claims the password is "never in source or
   logs"; the next line contradicts it.
6. **Rotate the DB credential** out of the systemd environment into a root-only
   `EnvironmentFile=`/`LoadCredential=`, and add `User=` so neither service runs
   as root. Open since HANDOVER §10. Postgres is loopback-only
   (`docs/hosting.md:14,17-18`), so this needs a foothold to exploit — the
   realistic escape is again pasted operator output.
7. **Stop logging account identifiers** on failed sign-ins
   (`SteamAuthenticationHandler.cs:90` captures typos and third-party addresses).

### 15.3 NOT deferred — cheap, unrelated to the credential story

These are in this section only because they surfaced in the same audit. They do
not depend on TLS, do not log anyone out, and should be picked up with any
ordinary change:

- **The four missing `Owns()` gates** (audit §5, corrected). One line each,
  copied from `TransformState_Handler.cs:71`. The crafting one lets a client
  craft out of another player's inventory. **Best effort-to-risk ratio in the
  whole audit.**
- **Path containment in `PatchEngine.LocalPathFor`**
  (`tools/patcher/WAPatch/PatchEngine.cs:222-227`) — no `StartsWith(installDir)`,
  no `..` rejection, no rooted-path rejection, and a rooted `destPath` makes
  `Path.Combine` discard `installDir` entirely. The sha256 gate is real and
  correctly checked pre-write on the in-memory buffer (`:164-171`), and TLS is
  properly enforced, so a network MITM is not viable — this is about blast
  radius if the manifest host is ever compromised. Add the assertion and tests.
- **Cap the `ReferenceDataRequestState_Handler.cs:39` loop at 1** — currently an
  uncapped client-supplied list, four GZip compressions and three full catalogue
  serialisations per element. One packet stalls the server.
- **Hoist the `GameStats.Read` call** behind the null check at
  `Handlers/PublicMap/PublicMapHandler.cs:121` — it is an argument, so it
  evaluates unconditionally and re-parses the whole stats file for every
  anonymous request.

### 15.4 The structural one, which is not a quick fix

**Character identity is a client-supplied string.** `CharacterIdentity.UidFrom`
(`Inventory/CharacterIdentity.cs:54`) validates only `Guid.TryParse` (`:73`), and
the uid **leaks on the wire** — `ComponentsSerializer.cs:576`, `:2546`, `:2626`
serve owner uids to any peer that checks out a shipyard or hull. So: walk past a
ship, read the owner's uid off components you are legitimately served, relog
publishing that uid, and inherit their inventory, progression, ship and crew
seat.

The code is defensively written and the author understood the hazard — it
explicitly rejects the upstream placeholder at `:48-53`, and the handler comment
at `PlayerPropertiesState_Handler.cs:74` names the root cause: *"no packet on the
ENet wire carries an account."* The architecture gives it nothing better to check
against. **Real fix:** `WorldsAdriftServer` issues a signed session token that the
game server validates on connect and binds to the peer; the 1088 uid is then only
ever *compared* to that binding, never *sets* it. That is the same work as step 1
above, which is a further argument for doing the account path properly, once.
**Cheap interim:** stop serving raw character uids at the three sites, which
severs the discovery half.

Full ranked detail, including the interest-set escape that makes several of these
reachable, is in `docs/plans/architecture-audit.md`. **Read its §5 with the
2026-08-20 correction** — the original text understated the ownership-gate
finding.
