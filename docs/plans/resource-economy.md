# PLAN — THE RESOURCE ECONOMY

*Branch `feat/resource-economy`, cut from `main` @ `2fc5846`.*

This plan answers three maintainer questions — *is a deposit a stone that yields
different metals?*, *can we add scrap and scrapping?*, and *can we bring in the
other resources properly?* — and sequences the work.

It builds on `docs/research/findings-resource-catalogue.md` (branch
`research/resource-audit`). **Section 0 corrects that document in three places**,
and those corrections change the ordering materially: two of the things the audit
calls missing are already built and one line away from working.

Provenance labels are the repo's usual four: **PROVED** (read out of the
decompile or shipped data), **RECOVERED** (reconstructed from a retail artefact),
**INFERRED**, **WAREBORN TUNING** (our decision, no retail basis).

## STATUS

| Phase | State |
|---|---|
| 1 — Quality reaches the item | **DONE**, committed |
| 2 — Per-island deposit metals | **DONE**, committed |
| 3 — Fibre and berries off the tree cut | **DONE**, committed |
| 4 — The loom | not started; scope grew, see the phase |
| 5 — Scrapping | not started |
| 6 — Cooking routing | not started |
| 7 — Creature lifecycle | not started; 7a-iii unsized pending investigation |

Phases 1–3 are the ones a player feels immediately and they were taken to
completion rather than three phases being left half-landed. Phase 4 stopped
before it started for a reason worth reading: investigating it turned a
one-component fix into a general defect affecting sixteen of eighteen
deployables, which is a different piece of work and overlaps another agent's.

---

## 0. CORRECTIONS TO THE RESOURCE AUDIT

### 0.1 The per-island metal table is NOT unused. It is loaded, labelled and wired.

The audit says `island_resources.json` is "referenced ONLY by a test and a
research doc — production never reads it". That is true of *that file*, and
misleading about the *data*, which was imported into the runtime catalogue long
ago:

- `WorldsAdriftRebornGameServer.Multiplayer/Islands/release-runtime-catalog.json`
  is an embedded resource carrying **all 254 islands**, each with an effective
  `metals` list (name + quality 1–10), the raw `pveMetals`/`pvpMetals` it was
  derived from, and a `metalSource` provenance string.
- Provenance is already enforced in code, not just documented:
  `Islands/IslandSurveyProfile.cs:27-45` defines
  `MetalTableSource { SurveyPve, SurveyPvp, InferredTier }`, and `:110-121`
  **throws** if a profile claims survey provenance it cannot support. Counts in
  the shipped catalogue: `survey-pve 38`, `survey-pvp 23`, `inferred-tier 193`.
- `Islands/ReleaseWorldCatalog.cs:146-157` already stamps every release-world
  deposit with its own metal and quality. The shipped catalogue holds **1930
  deposits across 15 metals at qualities 1–10**.

So the *deposit placement* layer already models exactly what the maintainer
describes. What is missing is one step further down the pipe — see 0.2.

**Where "everything is iron" is genuinely true:**

| Path | Metal | File |
|---|---|---|
| Haven's hand-placed deposits | `"iron"`, quality 6, for every node | `Multiplayer/MetalDeposits.cs:223,226` |
| Client-handshake deposits | `"iron"`, quality 5 | `Game/Gathering/DepositHandshakeSpawner.cs:42,45` |
| Release-world deposits | **already varied** | `Islands/ReleaseWorldCatalog.cs:150-152` |

and **the live server runs Haven only**: `deploy/wareborn-game-native.service`
sets exactly one variable, `WAREBORN_GAME_PORT=7779`. `WAREBORN_RELEASE_WORLD_DISTRICTS`
(`Islands/ReleaseWorldRolloutPolicy.cs:22`), `WAREBORN_ISLAND_FAUNA`
(`Islands/IslandFaunaPolicy.cs:145`) and `WAREBORN_SPAWN_DATABANK` are all unset.
So the audit's *player-facing* claim is right — today every deposit a player can
reach is iron — while its *code* claim is wrong.

### 0.2 The real, universal bug is one line, and it is worse than "quality is 0".

`Game/Gathering/WorldResourceActivation.cs:57-58,71-72` and
`Game/Gathering/DepositHandshakeSpawner.cs:161-163` all do this:

```csharp
HarvestReward.Register(node.MetalType, new YieldRule(node.MetalType, 1));
```

Two defects, not one:

1. **`node.Quality` is dropped**, so `YieldRule`'s `quality = 0` default wins
   (`Multiplayer/Gathering/YieldRule.cs:26`). Retail's scale is 1–10 and it is a
   *floor* in a crafting slot (`Multiplayer/Crafting/ShipBlueprintBuild.cs:34`
   `if (quality < required.Quality) return false;`), so quality 0 fails every
   slot that asks for anything, and the UI renders "Quality: 0".
2. **The yield table is keyed by the metal NAME, not by the node.** Even if the
   quality were passed, two iron nodes of different quality would overwrite each
   other's rule and the last one registered would decide what every iron node in
   the world pays out. This is why the fix is not "pass `node.Quality` in" — the
   *shape* has to change so quality travels with the hit, not with the name.

`Multiplayer/Inventory/InventoryPolicy.cs:446-450` already refuses to stack items
of differing quality, so the moment quality is real, quality-graded piles behave
correctly with no further work.

### 0.3 The scrap yield table is already shipped, in `itemData.json`, and already reaches the client.

The audit reports a "recovered 239-row salvage table" in
`docs/research/gathering/data/salvage_yields.tsv` as an external artefact. **134
of those rows are already live reference data**: every `scrapItem-*` row in
`WorldsAdriftRebornGameServer/Game/Items/Config/itemData.json` carries a
`rewards` object keyed by island tier —

```json
"rewards": { "3": { "a": 80, "q": 6, "item": "titanium" } }
```

`a` = amount, `q` = quality, `item` = the material. Tier keys observed:
`1, 1.1, 2, 2.1, 3, 3.1, 4, 4.1, 4.2`. Distinct yields: 21 ids — 15 metals, 5
woods, `fuel`. **Nothing in the server reads `rewards`** (a repo-wide grep finds
one unrelated comment). RECOVERED.

And the client-side trigger already exists and already reaches us:

- `acs/Travellers.UI.PlayerInventory/InventoryTooltipPopup.cs:112-118` — the
  **SALVAGE** action appears on any item whose id `StartsWith("scrapItem-")`.
  PROVED.
- It calls `Use()` → `acs/InventoryItemSlot.cs:395-401` →
  `acs/Bossa.Travellers.Behaviours/InventoryModificationBehaviour.cs:145-149`
  `TriggerTryToConsume(inventoryEntityId, itemId)` on **component 1082**. PROVED.
- We already receive it and already refuse it:
  `Game/Components/Update/Handlers/InventoryModificationState_Handler.cs:217`
  — `Note(update.tryToConsume.Count, "tryToConsume", "no consumable effects")`.

**So scrapping needs no new wire message, no new component, no client change and
no schema migration.** It is one handler branch plus a pure payout policy. This
is a large correction: the audit ranks loot containers fifth partly because the
scrap economy looked unrecoverable.

---

## 1. THE DEPOSIT MODEL — a direct answer

**The maintainer is right.** A metal deposit is a generic rock; which metal it
yields is data carried on the node, and it varies by island.

Evidence:

- The retail schema puts the metal on the rock, not in the prefab:
  `gencode/Bossa.Travellers.Materials/MetalRockStateData.cs:24` —
  `MetalRockStateData(Quaternion normalRotation, bool isDestroyed, EntityId islandId,`
  **`string metalTypeId, int quality`**`, List<EntityId> surfaceNuggets, int numberOfNuggetsToSpawn)`.
  PROVED.
- The deposit's own component carries **only a visual** variant:
  `MetalDepositStateData{ string variantId, EntityId coreId }`. `variantId` names
  a `MetalDepositVisuals` asset in the biome's PropLibrary — i.e. it selects
  *which rock you see*, and says nothing about what is inside. PROVED
  (`docs/research/gathering/findings-metal-deposits.md`).
- The per-island table is real and graded: 15 metals with stable ids 1–16 (id 6
  is absent and is probably one of `magnesium`/`palladium`/`platinum`), quality
  1–10 correlating hard with island tier — tier 1 → q1–4, tier 4 → q7–10.
  RECOVERED.

**Two refinements the maintainer should know, because they change the design:**

1. **One deposit yields ONE metal, not a mixture.** Variety is *between* nodes
   and *between* islands, not within a rock. An island's table is the menu each
   of its deposits is drawn from. RECOVERED (the shipped catalogue stamps one
   metal per deposit).
2. **Quality is per-node, and it is the part that is actually broken.** The metal
   name already varies correctly wherever the catalogue is used. Quality is
   dropped everywhere, on every source, including trees and fuel. Fixing quality
   is worth more than fixing metal variety, and it is Phase 1 for that reason.

One thing the maintainer's phrasing does *not* imply, and which we should not
build: there is **no ore-to-ingot smelting step**. Retail's beam paid the metal
directly. `iron` the deposit yields *is* `iron` the crafting input.

---

## 2. THE SEAM WITH THE CONTAINER WORKSTREAM

`feat/loot-containers` owns where scrap **comes from** (loot containers, chests,
ruin piles, craftable storage, and serving `1081 InventoryState` on non-player
entities). This plan owns what scrap **becomes**.

The dependency is explicit and mutual:

- **Phase 5 (scrap salvaging) is fully testable and shippable without them** —
  the payout is driven by an item already in the player's inventory. It is
  *reachable in play* only once something puts scrap there.
- Until then scrap can be granted by the existing admin/dev grant path for
  verification, and the maintainer can be handed a scrap item to salvage.
- **What I expect from their side:** a `scrapItem-*` item granted into a player
  inventory through `InventoryService.Grant`, with the **source island's tier**
  recorded in the item's `meta` under the key `"sourceTier"` (a string, `"1"`–`"4"`).
  `Grant` already takes a `meta` dictionary
  (`Game/Inventory/InventoryService.cs:98-103`), so this needs nothing new from
  either side. Phase 5 defines the key and defaults to tier 1 when it is absent,
  so their work and mine can land in either order without breaking.

Recommended sequencing: **their containers can land before or after Phase 5; the
order does not matter.** What must not happen is Phase 5 landing and the
maintainer concluding scrapping is broken because nothing hands out scrap — so
Phase 5 ships with a documented admin grant for verification.

Separately, and flagged for them: **check whether placed deployables generally
are missing a client-required activation component.** The loom's `1264` is a
known case (Phase 4 below); if it is a general pattern it affects their placed
containers too.

---

## 3. THE PHASES

Ordered by *what most unblocks the crafting loop per unit of risk*. Every phase
below is a **server-only** change unless it says otherwise. **No phase requires a
database schema migration** — Phase 5 is the only one that touches persisted
shape at all, and it does so through an existing free-form `meta` dictionary
rather than a new column.

---

### PHASE 1 — Quality reaches the item

**Delivers.** The quality declared on a harvest node arrives in the player's
inventory, for every source: deposits, nuggets, trees and fuel.

**A player can newly DO.** Craft things whose slots demand a quality floor. Today
`ShipBlueprintBuild.Matches` (`:34`) rejects every quality-graded slot because
all harvested material is quality 0. After this, an iron q6 node makes a Q6 slot
craftable, iron q4 and iron q6 sit as *separate stacks*, and the item tooltip
stops saying "Quality: 0".

**How.**
1. `Multiplayer/Gathering/YieldRule.cs` — validate quality: reject anything
   outside 0–10, where **0 means quality-exempt** and is the correct value for
   `fuel` (PROVED: `acs/ScannableData.cs:325` excludes fuel from the quality
   scale) and for wood registered without a node.
2. `Multiplayer/Gathering/HarvestYield.cs` — add an optional per-hit quality
   override to `Resolve`, so the quality travels with the *hit* rather than with
   the *source name*. This is what fixes the collision in 0.2 defect 2.
3. `Game/Gathering/HarvestReward.cs` — `Award(...)` takes an optional
   `int? quality`, passed through to `Resolve`.
4. The four live award sites pass the node's own quality:
   `WorldsAdriftRebornGameServer.cs:914` (nugget), `:1002` (deposit scrap piece),
   `:1564` (fuel — passes nothing; fuel stays quality-exempt), and the tree award
   at `:755` (wood has no per-node quality today; stays 0 until a wood quality
   source exists).

**Depends on.** Nothing.

**Schema migration.** No.

**Networked state / soak.** No new component, no new message, no rate change.
The 1081 push already happens on every grant. **No soak needed** by the standing
rule; I will still run it once at the end of the executed phases.

**What could go wrong.**
- *Stack fragmentation.* Quality-graded stacks mean a player mining a mixed
  island fills their grid faster. This is retail behaviour, not a regression, but
  it is a visible change and the maintainer should expect it.
- *Existing inventories.* Items already granted at quality 0 stay at quality 0.
  They are not migrated and must not be — rewriting a live player's items is
  exactly the class of change that destroyed progression before.
- *Test blindness.* The trap the tree work fell into. The test must fail if the
  quality stops being passed **at the live call site**, not only if the pure
  model regresses. That means a test over `WorldResourceActivation` / the award
  seam, not only over `HarvestYield`.

---

### PHASE 2 — Haven and handshake deposits stop being uniformly iron

**Delivers.** Deposits on Haven, and deposits spawned from the client's
surface-sample handshake, draw a metal and quality from their island's table
instead of the hardcoded `"iron"`.

**A player can newly DO.** Find more than one metal without leaving the starter
island; meet a quality above and below the single fixed value.

**How.**
- Release-world islands already work (0.1) — this phase is only
  `Multiplayer/MetalDeposits.cs:223,226` and
  `Game/Gathering/DepositHandshakeSpawner.cs:42,45`.
- For an island with a catalogue record, draw deterministically from
  `record.Survey.Metals` by node index — no RNG, so a restart reproduces the same
  layout, which the existing deposit code is careful about
  (`MetalDeposits.cs:180-186`).
- **Haven has no survey row** — it is Bossa-authored, not a Workshop island, and
  `IslandSurveyCatalog.ByIsland(HavenId)` is deliberately null. Its current
  iron-only choice is *documented and deliberate*
  (`MetalDeposits.cs:198-205`: "cycling arbitrary metals would manufacture lore
  and make the starter material needlessly scarce"). Proposed replacement, and it
  is **WAREBORN TUNING**, stated as such: use the **tier-1 cohort** — the same
  46-island cohort the existing `tools/world-import/metal_inference.py` generator
  used for 193 unsurveyed islands — weighted as observed:
  `Iron 36/46, Bronze 20/46, Lead 18/46, Tin 9/46, Epilar 8/46, Copper 4/46`,
  qualities 1–4. Iron stays overwhelmingly dominant, so the starter loop is not
  made scarce, and the method is the one already blessed for the other 193
  islands rather than a new invention.
- **This is a balance decision the maintainer should sign off**, because Haven's
  declared quality would drop from 6 to the tier-1 band 1–4. Alternative: keep
  Haven's quality at 6 and vary only the metal. I recommend asking rather than
  choosing.

**Depends on.** Phase 1 (otherwise the quality half is invisible).

**Schema migration.** No.

**Networked state / soak.** No.

**What could go wrong.**
- Haven's first crafting loop needs iron. If the weighting is applied naively a
  player could spawn next to three bronze nodes. Mitigation: index 0 (the
  proven-position node nearest spawn) stays iron unconditionally.
- Handshake deposits have no island record when the island is not in the
  catalogue; they must fall back to today's behaviour rather than throw.

---

### PHASE 3 — Plant fibre and berries: a second yield off the tree cut

**Delivers.** `HarvestYield` learns the shape `HarvestReward.cs:80-85` already
identifies as missing: **one source key resolves to several yield rules**. Trees
then pay wood *and* plant fibre *and* daccat berries from the cut we already
award.

**A player can newly DO.** Gather `plantFiber` — the input the entire Clothing
branch is dark for want of — and `daccatBerries`, without any new mechanic, new
entity or new wire message.

**How.**
1. `HarvestYield` stores `IReadOnlyList<YieldRule>` per key; `Resolve` returns a
   list of grants. `Register` keeps its single-rule overload so no existing call
   site changes.
2. `HarvestReward.Award` grants each resolved rule and toasts each separately —
   the client's `ReceiveSalvageFeedback` toast is per item type
   (`acs/FeedbackVisualizer.cs:41-43`), so several toasts is the retail shape.
3. Two new `itemData.json` rows. **Both ids are PROVED from shipped data. Grid
   sizes are RECOVERED from the icon filenames. Everything else — display text,
   stack size, rarity — is WAREBORN TUNING and says so in the row.**
   - **`plantFiber`** — PROVED, and stronger than the audit had it. It is not an
     icon-derived guess: it is a verbatim `itemCategory` in shipped quest data,
     `docs/research/loop/data/quest-conditions.json:74,82` and
     `docs/research/loop/data/quests.json:1933,2506`
     (`HaveItemByCategory{itemCategory:"plantFiber", requiredQuantities:[15]}`).
     Display name **"Plant Fiber"** / "Plant Fibers" is verbatim shipped text
     (`quests.json:1924,2137,2230,2235,2499`). Icon `materials/1x2_plantfiber`
     (`docs/research/valid-icons.txt:509`) → grid 1×2.
   - **`daccatBerries`** — PROVED
     (`acs/Travellers.UI.PlayerInventory/InventoryContents.cs:55`, plus
     `quest-conditions.json:161-262` using it as `itemIdToKeep` / `itemIdToLookFor`
     and the asset names `ConsumeItem-daccatBerries`, `ItemPresent-daccatBerries`).
     Icon `foods/2x2_berries` → grid 2×2.
   - **Note on `category`.** In retail both of these are **item categories**, not
     only ids — the collect-SFX table keys on `.category`
     (`InventoryContents.cs:551`) and the quest condition asks by `itemCategory`.
     So each row gets `category` equal to its own id. The recipe requirement is
     then written as the same string, which matches whether the client resolves it
     as a category or as a specific itemTypeId
     (`acs/InventoryItemManager.cs:117-120` accepts either).
4. Fix the first of the five lying recipes: `clothMakeshift`'s
   `craftingRequirements[0].name` from `"iron"` to `"plantFiber"`. Its label
   already reads "Plant Fibers", and `clothMakeshift` is PROVED as the real
   tutorial output (`quests.json:2124` `itemIdToLookFor: "clothMakeshift"`).
5. **Rate: UNKNOWN, with a proved anchor.** `RawMaterialSourceStateData.amount`
   was server-authored and did not survive; the client only displayed it. The one
   number that *is* shipped is the tutorial's simultaneous ask —
   **15 Plant Fibers alongside 20 Wood** (`quests.json:1924` and `:1946`), i.e. a
   designed ratio of **0.75 fibre per wood**. We award 1 wood per felled section,
   so the faithful rate is 3 fibre per 4 sections. Proposed: **1 fibre per
   section** and **1 berry per section** — WAREBORN TUNING, rounded up from the
   proved 0.75 because this project is generous where retail was thin, and
   because a fractional per-section rate would make small trees pay nothing.

**Berries and biome — the honest answer, and it is a "no".** The maintainer
expects several kinds of berry by biome. The evidence **contradicts** that:

- Exactly **one** berry identifier exists in the whole decompile
  (`InventoryContents.cs:55`), and it is used there as a **category**.
- Exactly **one raw berry icon** ships: `foods/2x2_berries`
  (`valid-icons.txt:351`). The other three berry icons — `1x2_berrysyrup`,
  `2x2_pickledberries`, `2x2_rumberrydesert` — are cooked products.
- The 22 biome-suffixed icons that *do* ship are **all creature materials**
  (meat, `Beetle_Biome{n}_Resource1`, `MantaRay_Biome{n}_Resource{n}`, neural
  cluster, conductive vessels). Berries were deliberately not in that family.

So this plan ships **one berry**. The biome axis is real and first-class, but the
evidence puts it on creatures (Phase 7b), not on plants. Per-biome berries would
be a WAREBORN TUNING invention and are not proposed.

**And the source is PROVED, not inferred.** Verbatim shipped quest text
(`docs/research/loop/data/quests.json:2762`):

> *"You're injured, to heal you'll need some food. **Daccat Berries** are the most
> basic form of food, and can be salvaged from **tree trunks and branches**."*

with the step description `"Salvage Berries"` at `:2767`, and the fibre step
`"…**Cloth** and **Wood**, both of which can be salvaged from **trees**"` at
`:1918` with the tutorial arrow pointed at a `Tree` entity
(`quests.json:1935-1937,1948-1950`). The audit had the tree→fibre/berry link as
INFERRED from the wiki; it is PROVED from Bossa's own quest data.

**Depends on.** Nothing, but sits *after* Phases 1–2 because it changes the
`HarvestYield` signature and it is cheaper to change once.

**File-boundary note.** `fix/log-grounding` owns `TreeFall.cs`, `TreeHarvest.cs`
and `FallingLogService.cs`. This phase touches none of them — the tree award call
lives at `WorldsAdriftRebornGameServer.cs:755` and the yield logic in
`Multiplayer/Gathering/`. The seam is the `WoodType`/`SectionsFelled` triple,
which is unchanged.

**Schema migration.** No.

**Networked state / soak.** No new component. It does raise the number of 8060
feedback events and 1081 pushes per tree cut by ~3×, on a player-initiated,
low-frequency action. Not a high-rate relay, but it is the one phase here that
increases message volume at all, so **run the soak on this phase**.

**What could go wrong.**
- *Multiple 1081 pushes per cut.* `InventoryService.Grant` pushes on every grant;
  three grants means three pushes. Batch them into one push per hit, or the panel
  churns.
- *Unknown itemTypeId is a client NRE.* Both new rows must be in `itemData.json`
  before either id is ever registered, and `InventoryService.Grant` already
  refuses unknown types — verify the row lands in the reference data the client
  actually receives on `1097 ReferenceDataState`.

---

### PHASE 4 — The loom stops being a dead prop

**This phase is bigger than the audit thought, and the reason is worth reading.**

The audit says the loom is inert because `1264` is never seeded. That is true and
incomplete on three counts, all now verified:

1. **The loom needs FOUR components, not one.** `LoomVisualizer` inherits
   `CraftingStationBehaviour`, whose `[Require]`s are 1004 and 1005
   (`acs/Bossa.Travellers.CraftingStation/CraftingStationBehaviour.cs:24-28`), on
   top of its own **1264** and **1210**. Improbable injects a behaviour only when
   *every* `[Require]` in the whole type hierarchy is satisfied, so 1264 alone
   changes nothing. The stove needs those four plus **1120**.
2. **Even fully seeded, the interaction dead-ends.**
   `Game/Components/Update/Handlers/InteractAgentState_Handler.cs:292-317` routes
   a `Craft` interact to `OpenShipyardConsole` / `OpenCraftingStationConsole`, and
   the latter consults the `PlacedCraftingStations` ledger
   (`Game/Placement/PlacementService.cs:334`), which only rows flagged
   `IsCraftingStation: true` join (`:555-558`). A loom is not in it, so the server
   logs "matched NEITHER ledger" and nothing opens.
3. **THIS IS A GENERAL BUG, NOT A LOOM BUG — and it is the single most valuable
   finding in this plan.** **16 of the 18 rows in
   `Multiplayer/Placement/Deployables.cs` seed only `190602 TransformState`
   (`TransformOnly`, `:135`) and nothing else.** Every one of them whose client
   behaviour is gated on a component is placeable, visible and silently dead:

   | deployable | prefab | components the client requires that we never seed |
   |---|---|---|
   | `makeshiftStorage`, `storageContainer`, `shippingContainer`, `cupboard`, `trunk`, `barrel`, `mountedBox` | 7 container rows | **1081 + 1210** (+1236) |
   | `loom` | `Loom01` | **1264 + 1210 + 1004 + 1005** |
   | `stove` | `Stove01` | **1264 + 1210 + 1004 + 1005 + 1120** |
   | `campFire` | `Campfire` | **1012 CampfireState** |
   | `lamp` | `Lamp01` | **1108 + 1236 + 1099** |
   | `personalReviver` | `KiokiRevivalChamberA` | **1094 RespawnPointState + 8066 ShipRootState** |
   | `territory_control_beacon` | `TerritoryControlBeacon` | **1210 + 1272 + 1273 + 1236** |

   The only two rows that are *not* inert — `shipyard` and `assemblyStation` — are
   exactly the two with a full seed set. That is the proof.

   **Two of those seven container rows are `feat/loot-containers`' craftable
   storage, and their missing components are exactly `1081 + 1210`.** Tell them:
   the storage container is not blocked on new machinery, it is blocked on the
   same seeding gap as the loom, and fixing it generally fixes both.

**Delivers.** A placed loom activates, is interactable, the Clothing tab routes,
and there is something to make in it.

**A player can newly DO.** Weave `plantFiber` into cloth at a loom they built.

**How — 4a, the seeding gap (cheap and mechanical).**
1. A `1264` branch in `Game/Components/ComponentsSerializer.cs` (~10 lines, copy
   the 1004 branch at `:1515-1531`). No new serialisation code is needed: the
   wire serialiser is the *client's own* vtable
   (`ComponentsSerializer.cs:3593`), the type ships in `Generated.Code.dll`, and
   `ComponentsSerializer.cs:21` already has `using Bossa.Travellers.Ship;` — the
   namespace `InventoryItemCraftingStationState` lives in. Idle default:
   `Data(no craftedBy, isReady:false, itemTaken:false, no schematicId, empty materialsUsed)`.
2. A full seed array for the loom row in `Deployables.cs`
   (`190602 + 1004 + 1005 + 1210 + 1264`).
3. Add `1264` to `IdsWithSerializerBranch` in
   `Multiplayer.Tests/Placement/DeployablesTests.cs:84` — **this list is
   load-bearing**, see the trap below.
4. Teach the 1210 branch (`ComponentsSerializer.cs:752-813`) to serve a `Craft`
   verb entry for a placed loom, or `InteractiveObjectVisualizer.OnEnable:67`
   gives it radius 0 and no prompt ever appears.

**How — 4b, routing and content (the real work).**
5. Join the loom to the `PlacedCraftingStations` ledger, or add a third route
   alongside shipyard/station in `InteractAgentState_Handler`.
6. **`Multiplayer/Crafting/StationCraftRouting.cs`** currently routes exactly two
   categories (`PersonalCategory`, `CraftingStationCategory`, `:56-71`), so the
   Clothing and Cooking recipe categories are unreachable *in principle*. Replace
   the boolean `isPersonalTarget` with the target's category, keeping the
   crash-safety invariant the file exists for (`:73-86`: a category mismatch
   NREs the client's `CraftingStationSchematicList.SelectSchematic`).
7. Add `cloth` to `itemData.json` (icon `materials/2x2_cloth`, RECOVERED) and the
   first `Clothing` schematics. Retail's only surviving output evidence is three
   grouping strings — `"Clothing - Chest"`, `"Clothing - Head"`,
   `"Clothing - Legs"` (`acs/SchematicIconUtil.cs:19-21`). Recipe contents are
   **WAREBORN TUNING**; we ship one garment per slot rather than inventing a
   catalogue.
8. Fix the loom recipe's slot-3 lie: `"Strings"` currently consumes `iron`.

**Depends on.** Phase 3 (no fibre → no cloth → nothing to weave).

**Schema migration.** No.

**Networked state / soak.** Extra components on a placed deployable, seeded once
per placement. Not a relay. No soak.

**What could go wrong.**
- **The all-or-nothing seed batch.** `PlacementService.BroadcastToPeer:617-633`
  sends every seed id in ONE `SendAddComponentOp` with
  `failOnComponentInitError: true`. An id with no serialiser branch drops the
  *whole* batch **including 190602** — so a half-done fix does not produce a
  half-working loom, it produces a **loom at the world origin**. This is why step
  3 is not bookkeeping.
- **Entity-gated serialiser branches.** 1108, 1236, 1099 and 1120 wrap their
  bodies in `if (LooseParts.Is(entityId))` / `DefFor(entityId) != null`. For a
  *placed* item the branch matches but leaves `obj == null`, `outcome` stays
  `NoClientVtable`, and the batch fails anyway. Relevant to the lamp and the
  stove, not to the loom.
- **The routing change is a crash-guard change.** `StationCraftRouting` exists
  because a mismatched category dereferences null on the client. Widening it
  wrongly reintroduces that crash. Tests must pin every (target, category) pair,
  including the refusals.
- **Unverified:** whether the shipped client build actually carries a vtable for
  1264. The server logs `component N has NO client vtable in this build`
  (`ComponentsSerializer.cs:3647`) if not. The decompiled `Generated.Code`
  carrying `[ComponentId(1264u)]` is strong evidence it does, but this is the one
  thing in Phase 4 only a live client can confirm.

---

### PHASE 5 — Scrapping: `scrapItem-*` becomes materials

**Delivers.** The SALVAGE button the client already draws on every scrap item
actually pays out, from the 134-row recovered reward table already shipped in
`itemData.json`.

**A player can newly DO.** Right-click any scrap item in their own inventory →
SALVAGE → receive the metal / wood / fuel that scrap was worth, at the recorded
quality.

**How.**
1. `Multiplayer/Inventory/` — a new pure `ScrapSalvagePolicy`: given an item type,
   its `rewards` table and a source tier, return the grant (item, amount,
   quality) or a refusal. Pure, no I/O, unit-tested.
2. `InventoryModificationState_Handler` — replace the `tryToConsume` refusal
   (`:217`) with a branch: if the item id resolves to a `scrapItem-*` type with a
   reward for the tier, consume the item and grant the payout. Everything else
   keeps refusing exactly as now, including the unconditional 1081 re-push that
   stops the panel sticking.
3. **Tier resolution**, in order: the item's `meta["sourceTier"]` if present
   (see §2 — this is what I ask of `feat/loot-containers`); else the tier of the
   island the player is standing on; else **1**. Fractional keys (`"1.1"`,
   `"4.2"`) exist in the data and appear to be sub-tier variants; resolve a
   request for tier *n* to the highest key whose integer part is *n*, so no row
   is unreachable. RECOVERED shape, WAREBORN TUNING resolution rule.
4. **The 21 distinct yields are metal, wood and fuel only.** The recovered table
   is a pre-Update-27 snapshot; cloth, leather, glass and pigment are *not* in
   it, and their real mapping is genuinely unrecoverable (§8 of the audit). The
   maintainer has authorised judgement here. Proposal, labelled **WAREBORN
   TUNING** in the data file itself: leave all 134 recovered rows exactly as
   recovered, and add cloth/leather/glass/pigment yields only to scrap rows whose
   *recovered display name* already describes cloth or hide — e.g. rope, sail,
   banner, garment. That keeps every recovered number untouched and makes the
   invented part inspectable in one diff.
5. `schematics` also gets the SALVAGE button
   (`InventoryTooltipPopup.cs:113`). Out of scope here — schematic items belong
   to the knowledge workstream.

**Depends on.** Nothing to build. **Depends on `feat/loot-containers` to be
reachable in normal play** — see §2.

**Schema migration.** **No.** The `sourceTier` key rides the existing free-form
`meta` dictionary that `InventoryService.Grant` already accepts and persists.
This is deliberate: a new column would force game server and login server to
deploy together, and a split deploy has already destroyed a character's
progression once.

**Networked state / soak.** No new component, no new message — `1082` inbound and
`1081` outbound already flow. Rate is one exchange per deliberate player click.
No soak needed.

**What could go wrong.**
- *Item duplication.* Consume-then-grant must be atomic against the client's
  `IsWaitingForServer` flag, and the client clears that flag only on a 1081
  update. If the grant fails (full inventory) the scrap must **not** be consumed.
  Refuse and re-push.
- *Two SALVAGE clicks in flight.* The client blocks its own UI while waiting, but
  the server must still be idempotent on an item id that is already gone.

---

### PHASE 6 — Cooking routes, and the food recipes stop lying

**Delivers.** The four `Cooking` schematics become reachable, and the remaining
four lying recipes consume what their labels claim.

**A player can newly DO.** Nothing yet — cooking needs meat and berries. Berries
arrive in Phase 3; meat needs Phase 7. This phase is deliberately small and
exists so that the moment meat lands, cooking works.

**How.** Falls out of Phase 4's routing change plus four `schematicData.json`
edits: `thuntomiteStew` (`iron`,`iron` → raw meat, berries), `thuntomiteSteak`,
`mantaSteak`, `moonshine`. Also seed `1264` on the stove
(`acs/StoveVisualizer.cs` is the only other `CraftingStationBehaviour`).

**Depends on.** Phase 4.

**Schema migration.** No. **Soak.** No.

**What could go wrong.** Pointing a recipe at an item id that does not exist yet
makes it *uncraftable* rather than *wrongly craftable* — which is the right
failure, but it means the four food recipes visibly regress from "craftable with
iron" to "not craftable" until Phase 7. Say so before shipping it.

---

### PHASE 7 — Creature lifecycle and biome-tiered creature materials

This is the phase the maintainer means by "full lifecycle stuff", and it is the
only one that needs genuinely new systems. It splits into four, because the first
two are shippable on their own and the last is blocked on content we do not have.

**The maintainer's own recollection of retail, which is the spec for this phase:**

> *"you could shoot them with guns you craft, or the cannons on your ship. They
> would die and fall to the ground or something, and you could then salvage them
> kinda like rocks and trees."*

That is four links — **damage → death → the corpse falls → salvage it with the
beam** — and it matches what the decompile independently proves. The salvage link
is the best-evidenced: `MeatSourceBehaviour` is on every creature prefab and is
salvageable exactly when `MortalityState.IsDead`, i.e. the same beam and the same
verb as a rock or a tree. It also re-confirms the negative: there was **no
butchering minigame**, `Butcher` appears once in the whole shipped build as an
orphan UI label, and jellyfish drop nothing. Do not invent a butchering mechanic.

**Reality check, because it reorders everything:** fauna is **OFF in production**
(`WAREBORN_ISLAND_FAUNA` unset in `deploy/wareborn-game-native.service`). Nothing
in Phase 7 is visible to a live player until fauna is turned on, which is an
operations decision and a soak question in its own right. That is why Phase 7 is
last despite unlocking the most material families.

**And the seam that stops this being four phases instead of one:** the
`(source, biome)` yield key designed in 7b is **one model with two producers** — a
metal node and a creature corpse resolve through the same table. That is worth
building once, deliberately, rather than growing a second creature-shaped yield
path beside the one Phases 1–3 just finished.

#### 7a — Mortality, without breaking the closed-form fauna model

**The design problem, stated honestly.** Our fauna are deliberately stateless:
`position = f(creature, island, envelope, elapsedSeconds)`, and the only mutable
per-creature field in the whole system is `NextPoseAt`, a send cursor
(`Islands/IslandFaunaRegistry.cs:138-144`). Population is closed-form too —
`IslandFaunaRhythm.ExpressedCount(elapsedSeconds)` expresses a *prefix* of a
fixed id list, and the design note is explicit that "a birth is an increase in
expression, never a new id" (`Islands/IslandFaunaAge.cs:288-292`). Naive
per-creature health destroys that property and its wire cost.

**Proposed shape: a kill ledger, not creature state.** The pose function and the
rhythm stay untouched. A separate, small ledger records only the creatures that
are *dead*: `entityId → (killedAtSeconds, unitsHarvested)`. Expression becomes
`rhythm-expressed AND NOT in the kill ledger`. A dead creature is removed from
peers through the *existing* channel-5 remove path
(`Game/IslandFaunaService.cs:857-871`), which is already throttled at 120 ms and
queue-capped. Entries leave the ledger when the rhythm's expressed prefix next
falls below that creature's rank — i.e. the population recovers naturally rather
than by a respawn timer. Memory is bounded by the number of *recently killed*
animals, not by the number of animals.

**Damage.** `WorldsAdriftRebornGameServer.cs:876` is already a four-way ledger
dispatch on a shot target id and falls through silently for a creature. A fifth
branch guarded on `Fauna.IsFauna` is the whole hook; the client already emits the
ShotEvent for any hit entity. Health can be a per-species constant compared
against accumulated shots, mirroring `MetalDeposits`' 2000 HP / 200 damage /
10-shot shape, with no new component.

**Corpse salvage — the exact contract, PROVED.** `MeatSourceBehaviour` is added
to **every** creature prefab on **every** client, unconditionally
(`acs/Assets.Scripts.PrefabExporting.Preprocessors/CreaturePreprocessor.cs:164`,
inside `case WorkerPlatform.UnityClient`). It is 29 lines and its whole gate is
one line: `IsSalvageable() => _mortalityStateReader.IsDead`
(`acs/Assets.Scripts.Visualisers.Creatures/MeatSourceBehaviour.cs:17`). No timer,
no tool check, no meat counter.

It `[Require]`s **two** components, so both must be seeded or the behaviour never
injects at all:

- **`1171 MortalityState`** = `{bool isDead, long timeOfDeath, string causeOfDeath,`
  `bool dieImmediately, string activeConductOnDeath}`
  (`gencode/Bossa.Travellers.Creatures/MortalityStateData.cs:7-15`, id at
  `MortalityState.cs:20`). No commands, no events — flipping `isDead` is the
  entire death signal.
- **`1099 SalvageAndRepairState`**, inherited from the `Salvageable` base
  (`acs/Salvageable.cs:10`), **with `originalMaterials` populated**. That list is
  the yield: `SlottedMaterial{index, RawMaterial, amount, …}` where
  `RawMaterial = {materialTypeId, quality, category, meta}`.

**The client contributes zero yield logic** — what a corpse gives is entirely
whatever the server wrote into `originalMaterials`. That is the same contract
every other harvestable in the game uses, so no new mechanism is needed.

**Do not build a butchering minigame.** `Butcher` appears exactly once in the
entire shipped build, as an orphan UI label. Jellyfish drop nothing
(`BasicCreaturePreprocessor.cs:80-83` adds no `Salvageable`). PROVED.

**Soak.** **Yes, mandatory.** This adds a new networked state transition on a
relayed entity class. Standing rule applies.

**Schema migration.** No — the kill ledger is in-memory and rebuilt from the
rhythm on restart, exactly as the rest of fauna is.

#### 7a-ii — The corpse falls. **REUSE the log grounding; do not build a second one.**

The maintainer's *"they would die and fall to the ground"* is the same problem
`fix/log-grounding` has just solved for a felled tree trunk coming to rest, and
that agent has baked a **ground-height profile for all 13,266 tree seats** — 8
bearings per seat, measured off the island meshes, so the server can answer "what
does the terrain do around this point" with **no runtime raycast**. Constants
`MIN_T = 8.0`, `DECK_BAND = 6.0`, unknown sentinel `-128`.

**A second grounding mechanism must not be written.** Two caveats inherited from
that work, both of which change the design rather than decorate it:

1. **The profile is baked AT TREE SEATS.** It answers "what is the ground like
   near a tree", not "at an arbitrary point on this island" — and a creature can
   die anywhere, including over water, over a cliff, or over a ship's deck. So
   the honest position is: **the existing data probably does not generalise, and
   corpse landing likely needs its own sampling.** Establish that before
   assuming either way. What is certainly reusable is the *method* and the
   `IslandSurfaceData` the profile was generated from, not necessarily the baked
   table.
2. **About one bearing in nine is UNKNOWN (`-128`).** Whatever corpses do, the
   unknown case must degrade to **resting on the surface**, never to **skipping
   grounding**. Skipping is exactly the floating-in-the-air / clipping-the-
   mountain bug the maintainer reported for logs, and reintroducing it for
   corpses would be the same bug with a new name.

`TreeFall.cs`, `TreeHarvest.cs` and `FallingLogService.cs` belong to that agent
and are not to be edited from here. **What this phase needs from them** is a
callable, entity-agnostic "ground this point" seam — given an island and a world
position, return a resting height and a surface normal, with the unknown case
resolved to the surface rather than to a null. If their module exposes that,
corpse landing is a caller. If it only exposes tree-seat lookups, say so and this
becomes its own small piece of work rather than a silent divergence.

**Open, and worth checking rather than assuming:** whether the corpse needs to
fall *on the server at all*. The client ragdolls a dying creature itself
(`CreaturePreprocessor` attaches the death visuals), so it is possible the server
only has to publish the death and a final resting transform, with the fall being
cosmetic. If so this sub-phase collapses to "pick a resting point", which is a
much smaller thing than a physics fall. Establish which before building either.

#### 7a-iii — The trigger. What is actually missing between firing and a corpse.

The maintainer confirms from the live client that **weapon schematics exist and
are visible in the crafting UI**, so the crafting side is not the gap. That moves
the question rather than removing it, and the accurate phrasing is not "weapons
do not exist" but: *does a crafted weapon fire, and does anything it fires deal
damage?*

Three things to establish, in order, before this sub-phase can be sized:

1. **The recipes.** Which weapon schematics we serve, and whether their inputs are
   honest. This plan has already found five recipes whose label and consumed
   material disagree; a weapon whose slot silently eats `iron` is the same bug
   class and is worth fixing in the same pass as Phase 6's.
2. **Firing.** What the client publishes when a weapon is used, and whether we
   have a handler for it. This is the seam most likely to be a stub.
3. **Damage reaching a target.** Whether *any* damage-to-an-entity path exists
   today. If a player-versus-object or ship-versus-ship path already carries
   damage, then creature damage is "give fauna a health component and subscribe",
   not "build a weapons system" — a completely different size of job.

**Ship cannons are a separate question and must be established separately.** The
maintainer named them apart from hand weapons, and they are ship *parts*, which
on this codebase is a different code path (mounted parts, `InteractVerb.Man`,
their own interaction policy). Do not assume one answer covers both.

Until 1–3 are answered this sub-phase has no size, and the plan should not
pretend otherwise.

#### 7b — Biome-tiered creature yields

**The biome axis is real, it is first-class, and we already have it.**

Retail's biome is `enum BiomeType { Biome1 = 1, Biome2, Biome3, Biome4 }`
(`gencode/Bossa.Travellers.Biomes/BiomeType.cs:1-9`) — 1-based, no zero. It is a
property of **world position**, not of an island record: 20 authored Voronoi
centres are broadcast in `1253 GlobalBiomeVoronoiCentresState`, and everything —
islands (`acs/IslandSurfaceData.cs:159,171`), players
(`acs/LocalPlayer.cs:406`), creatures — reads its biome by nearest-centre XZ
lookup (`acs/GlobalBiomeDataVisualizer.cs:56-84`). A creature then carries a copy
in its own `4325 BeetleVariantState` / `4326 MantaRayVariantState`, and
`biomeType` is the only field either variant client actually consumes
(`acs/…/MantaRayVariantClient.cs:149,169-190`).

**We already do this correctly.** `Islands/IslandBiome.cs:51-69` joins the survey
cell to `BiomeType 1..4`, verified against district 254/254, and it is already
served on 4326 (`Game/Components/ComponentsSerializer.cs:3476-3495`). Bossa's own
20-centre table survives at `docs/research/world-data/wamap-islands.json` under
`"Biomes"`. **So the server already knows every island's and every creature's
biome, and the yield key should be `(species, biome)` from the start** — exactly
as the maintainer asks, and cheap because the dimension already exists.

**The 22 biome-suffixed icons, enumerated** (`docs/research/valid-icons.txt`):

| family | icons | note |
|---|---|---|
| beetle raw meat | `foods/2x2_beetle_biome{2,3,4}_meatraw` | **no biome1** — generic `2x2_beetle_steak_raw` is the default art |
| manta raw meat | `foods/2x2_mantaray_biome{2,3,4}_meatraw` | **no biome1** — generic `2x2_manta_steak_raw` |
| neural cluster | `materials/1x1_biome{1,2,3,4}_neuralcluster` | complete |
| conductive vessels | `materials/2x1_biome{1,2,3,4}_conductivevessels` | complete |
| beetle resource | `materials/2x3_beetle_biome{1,2,3,4}_resource1` | index stays `resource1` |
| manta resource | `materials/3x2_mantaray_biome{1,2,3,4}_resource{1,2,3,4}` | index tracks the biome |

Note the two asymmetries: the meat families have **no biome1 icon**, and the
manta resource family increments its index with the biome while the beetle one
does not. So **the strings cannot be generated by one rule** — they have to be
tabulated verbatim.

**Names we can use, and names we cannot.** `beetleMeatRaw` and `mantaRayMeatRaw`
are PROVED (`acs/MaterialsEffectsData.cs:203-204`) — and note they are
**categories**, confirmed by the substring test
`category.Contains("MeatRaw") → "Organics"` at `InventoryContents.cs:556`. The
per-biome *item ids* are **UNKNOWN**: they exist only as icon filenames, and the
client never constructs an icon name — `acs/InventoryIconManager.cs:44,64`
prepend only the constant `"Icons/"` to a verbatim server string, and I checked
all 30 call sites. The **8 ids `Beetle_Biome{1..4}_Resource1` and
`MantaRay_Biome{1..4}_Resource{1..4}` likewise have unrecoverable display
names.** We ship them with a display name that literally reads UNKNOWN, or we do
not ship them. We do **not** invent retail names.

**Leather — the maintainer's assumption, tested and NOT supported.**

- `grep -rniE "leather" acs/ gencode/` → **zero matches.** Not one. Also zero for
  `leatherSalvage`, `pelt`; `hide`/`skin` hit only dev-console commands and
  `SkinnedMeshRenderer`.
- Two icons ship and nothing else: `materials/2x2_leather`,
  `materials/2x2_leathersalvage`. Leather appears in no quest, no knowledge tree,
  no prefab key, no materials table.
- **It is weakly contradicted by the manta family itself**: manta rays already
  have their own explicitly-named material family
  (`materials/3x2_mantaray_biome{n}_resource{n}`). If manta corpses dropped
  leather, that family would not need to exist.
- The naming pattern `leather` / `leathersalvage` exactly parallels `cloth` /
  `clothsalvage`, and cloth is proved to come from plant fibre with a
  *salvage-derived* second tier. INFERRED: `leathersalvage` is the scrap route.

**Verdict: "leather comes from manta rays" is UNSUPPORTED, and the better-
evidenced home for leather is Phase 5 (scrap), not Phase 7.** If the maintainer
wants it off corpses anyway that is a legitimate call, but it is **WAREBORN
TUNING** and must be labelled so.

**Soak.** Inherits 7a's. **Schema migration.** No.

#### 7c — Beetles / Thuntomites — BLOCKED, and this is a real dependency

`beetleMeatRaw`, `Chitin` and the four `Beetle_Biome{n}_Resource1` materials are
blocked on a creature we cannot spawn. `Islands/IslandFaunaPolicy.cs:14-27`
defines `enum FaunaSpecies { JellyFish, MantaRay }` and `PrefabNameFor` throws on
anything else (`:277-278`). The client prefab exists
(`Ship/client-entity-prefabs.txt:14-15` — `beetle`, `beetleegg`) and retail's own
flock enum had them (`Islands/IslandFaunaSchool.cs:60-62`), so this is
buildable — but it is a **new species with new ground movement**, not a yield
change. It is its own workstream and should not be folded into a resource phase.

---

## 4. WHAT THIS PLAN DELIBERATELY DOES NOT DO

- **No schema migration, in any phase.** See §3 preamble.
- **No client mod change.** Every phase above is server-side. The client already
  draws the SALVAGE button, the Clothing and Cooking tabs, the quality string and
  the salvage toast. If a later phase needs a client change it will be called out
  loudly, not done quietly.
- **No invented retail names.** The 8 creature-material display names stay
  UNKNOWN. The ~10 foraged foods (`Slug`, `Worm`, `Cloudworm`, `Greyshell`, three
  mushrooms, `conosSalt`, `seleneSugar`, `pumpkin`) have no component, prefab,
  verb or string connecting them to any world entity, so **how they were gathered
  cannot be established** and they are out of scope.
- **No `cobalt` / `aurium`.** These two are ours, not Bossa's — neither is in the
  shipped 18-icon metal atlas. They are used today by `MetalNodes.cs` Haven
  nuggets. Retail's three unshipped metals are `magnesium`, `palladium`,
  `platinum`. Renaming ours to those is a separate, cosmetic correction and is
  not bundled into a gameplay phase.
- **No atlas-category fix.** `atlasShard` carrying `category: "Metal"` instead of
  retail's distinct `"Atlas"` is real (`acs/InventoryContents.cs:54`,
  `acs/ComponentMaterial.cs`), but changing an item's category changes which
  crafting slots accept it, which touches live recipes. Separate change,
  deliberately not bundled.
- **No turning on databanks, fauna or the release world.** All three are
  operations changes to `deploy/wareborn-game-native.service` and belong in a
  deploy change the orchestrator makes, not in this branch.

---

## 5. ORDER, AND WHY

| # | Phase | Unblocks | Risk | Migration | Soak |
|---|---|---|---|---|---|
| 1 | Quality reaches the item | every quality-graded crafting slot | low | no | no |
| 2 | Per-island deposit metals | metal variety on the starter island | low | no | no |
| 3 | Fibre + berries off the tree | the entire Clothing branch | low | no | **yes** |
| 4 | Loom activates + Clothing routes | wearables | **medium** | no | no |
| 5 | Scrapping | 134 inert scrap rows | low | no | no |
| 6 | Cooking routes + recipe honesty | food, once meat exists | low | no | no |
| 7a | Creature mortality (kill ledger) | corpses | **high** | no | **yes** |
| 7a-ii | The corpse falls and rests | corpses you can reach | medium | no | inherits |
| 7a-iii | Firing and damage | a way to kill anything | **UNSIZED** | no | inherits |
| 7b | Biome-tiered creature yields | meat, chitin, clusters | medium | no | inherits |
| 7c | Beetles | beetle materials | **high** | no | inherits |

7a-iii is deliberately marked UNSIZED rather than given a guess. Weapon
schematics exist in the crafting UI, but whether a crafted weapon fires and
whether anything it fires deals damage are both unestablished, and the answer
changes the job by an order of magnitude: if a damage path to an entity already
exists, creature damage is a health component and a subscription; if it does not,
it is a weapons system. That question is out for investigation and the plan
should not pretend to a number before it comes back.

Phase 1 is first because it is the smallest change with the widest reach: it is
the difference between crafted stats meaning something and meaning nothing, it
costs no new state, and every later phase inherits it. Phase 5 is deliberately
*not* last despite depending on another workstream for its supply, because the
work itself has no dependency and the data is already shipped.
