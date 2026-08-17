# Populating metal on the 216 islands the survey never recorded

Tier 1 had metal on **four** of its 46 islands. World-wide, 354 deposits sat on 38 of
254 islands and the other 216 had terrain, databanks and nothing to mine. This
documents why that was a gap in a community survey rather than a fact about the game,
what replaced it, and exactly which parts of the result are evidence and which are
inference.

Result: **1930 deposits across all 254 islands** (328 in Tier 1), each carrying an
atlas shard, each resolving through the production harvest lookup. 38 islands keep
their own surveyed metals, 23 use the PvP table recorded for that same island, and
193 are labelled `inferred-tier` in the catalogue, in the runtime type system and in
the boot accounting.

---

## 1. Survey gap or real emptiness? — SURVEY GAP, proved

Five independent lines, strongest first.

### 1.1 The source is a player-submitted report system, and it says so itself — PROVED

`cardinal-guild/wasurveyor` is a Symfony application whose metal data comes from
**player-submitted survey reports** with an admin approval queue (`src/Entity/Report.php`,
`ReportMetal.php`, `IslandPVEMetal.php`, `src/Controller/Admin/ReportAdminController.php`).
It was never extracted from game data. Its API
(`src/Controller/ApiController.php::getIslandMarkersAction`) serialises `pveMetals` and
`pvpMetals` with **no filtering at all** — so `islands.json` is the complete final state
of that database, and an empty array means *no player ever filed a report*, not
*the island had no metal*.

The map's own UI settles the intent. For an empty list it renders the literal string
**"No metals data"** (`components/IslandPopup.vue:90`). The authors of the dataset read
their own empty arrays as *unsurveyed*.

- <https://github.com/cardinal-guild/wasurveyor>
- <https://github.com/cardinal-guild/wamap>

### 1.2 The survey visited all 254 islands and got metals from 38 — PROVED

| field | islands carrying it |
| --- | ---: |
| `surveyCreatedBy` / `surveyUpdatedBy` | **254 / 254** |
| `databanks` (exact count, 3-5) | **254 / 254** |
| `trees` | 74 |
| `pvpMetals` | 33 |
| `pveMetals` | **38** |

A survey that named a surveyor and an exact databank count for every island, and metals
for 15% of them, is not describing a world that is 85% barren. Metals were simply the
expensive thing to record: a databank count can be read off in one pass, a metal table
requires finding and scanning individual nodes.

### 1.3 There was no time — PROVED

Update 31 shipped **11 June 2019** with a brand-new map. The wamap repository records
*"Map disabled for update 31"* (2019-06-11) then *"Islands back enabled"* (2019-06-20),
and `ImportIslandsCommand` re-imported every island from `data/wamap.json` on 2019-06-20.
174 of the 254 islands carry a `createdAt` in June 2019. Worlds Adrift shut down
**26 July 2019**.

So the community had roughly **five weeks** to re-survey 254 islands on a map that had
just been replaced — and **44 of the 61 islands with any metal data were surveyed by one
person** ("Fearless Jake"). 38 PvE tables in five weeks is what a handful of volunteers
achieved before the servers went dark, and it is the only version that exists:
`static/islands.json` has **exactly one commit in its history**
(`bbaebec05c183929a402dc01750a6236e715a514`, 2019-09-10, *"Surveyor backend removed,
images and json files immortalized forever"*).

### 1.4 Retail's own schema has a floor, and the survey was reading its map — PROVED

The decompile has the per-island component the survey was describing.
`gencode/Bossa.Travellers.Islands/IslandResourceSpawnerStateReader.cs`, **component 1010**:

```
int   MetalRocksRequiredToRespawn
int   InitialMetalRockDeposits
float MetalDepositDensity
float MinMetalRockDeposits          <-- a FLOOR on deposits per island
float MetalOnSurfaceProb
Map<string,int> MetalDepositQuantities
Map<string,int> MetalDepositQualities   <-- exactly the survey's [{name, quality}]
```

Two things follow. First, `metalDepositQualities` is a per-island metal->quality map, and
the survey's `pveMetals: [{name, quality}]` is a readout of precisely that shape — so a
missing survey entry is a missing *reading*, not an empty map. Second, the schema carries
`minMetalRockDeposits`, a per-island **minimum**. A design in which 216 of 254 islands
spawn zero deposits does not need a minimum.

Corroborating: the Worlds Adrift wiki states *"each island will only produce one quality
variant of each metal"* (<https://worldsadrift.fandom.com/wiki/Metal>) — which is why the
retail map is `metal -> one quality`, and why the survey's table shape is right.

### 1.5 An independent survey of an earlier world found metal on 89% of islands — INFERRED (strong)

`jerodar/WAMap`'s `data/island_data.csv` is a separate community survey of a
**pre-Update-31** world revision: 304 islands, **271 with metal data (89%)**, against the
Cardinal survey's 24%. The 33 exceptions show no distinguishing pattern — they have
surveyors assigned, normal trees, and span all tiers, which is the signature of
incomplete surveying rather than of barren islands.

This is corroboration only, and it is **deliberately not imported** — see section 5.

### 1.6 The one thing that stays unproven

No primary source states outright "every island has metal". Update 31's notes come
closest, describing islands *without* spawn chambers as a deliberate exception made
*"to ensure that fresh t1 spawns are always on an island with plenty of resources"*
(<https://worldsadrift.fandom.com/wiki/Update_31>) — resources treated as the norm, not
the exception. **Labelled INFERRED.** It is the one assumption underneath this work, and
it is stated here rather than buried.

---

## 2. The rule, and what it is derived from

Implemented in `tools/world-import/metal_inference.py`. Run it directly to print the
whole derived rule — it is designed to be audited without reading any JSON.

### 2.1 A three-rung provenance ladder, cheapest evidence first

| rung | meaning | islands |
| --- | --- | ---: |
| `survey-pve` | the island's own recorded PvE table. Evidence. | **38** |
| `survey-pvp` | no PvE table, but the same physical island **was** read on the PvP shard | **23** |
| `inferred-tier` | neither table exists; composed from the tier cohort | **193** |

Rung 2 matters more than its size suggests. Shattered Mausoleum, our own seed island,
records **zero** PvE metals and **eleven** PvP metals. The same terrain cannot be barren
in one ruleset and carry eleven metals in the other; that island alone demonstrates the
gap. Using its PvP table is an observation of that island, one ruleset removed — much
stronger than inference, and it is labelled separately so it is never mistaken for a PvE
reading either.

The raw `pveMetals` and `pvpMetals` arrays are preserved **verbatim in the catalogue,
empty arrays included**. The effective table lives in a new `metals` field beside them.
The inference sits next to the evidence; it never overwrites it.

### 2.2 The palette: a metal->tier ladder, derived then independently confirmed

Every metal observation in the survey (405 across PvE and PvP) was grouped by the tier of
the island it was seen on. A metal is admissible at tier T if it was ever observed at or
below T — monotone by construction.

| first observed at | metals |
| --- | --- |
| tier 1 | Bronze, Copper, Epilar, Iron, Lead, Tin |
| tier 2 | + Gold, **Nickel**, Silver, Steel, Titanium, Tungsten |
| tier 3 | + Aluminium, Eternium, **Orthite** |

Is this real, or is tier 1 just thinly sampled? Aluminium, Orthite and Eternium are absent
from **all 39** tier-1 and tier-2 observations while accounting for **18.9%** of the 366
tier-3/4 observations. Expected under uniform draw: 7.4. Observed: 0.
Poisson P(0 | 7.4) is about **6e-4**.

Then the confirmation that makes this more than statistics. Bossa's Update 31 patch
notes — the notes for *this exact build* — name two retierings:

> *"**Orthite has been made a T3 metal**"* ... *"**Nickel has been made a T2 metal**"*
> — <https://worldsadrift.fandom.com/wiki/Update_31>

The ladder derived above, from player observations that never mention a tier, puts
**Orthite at 3 and Nickel at 2**. Two independent artefacts, exact agreement. This is
asserted as a test (`Derived_metal_tiers_agree_with_the_Update_31_patch_notes`) so a
future regeneration cannot quietly drift away from the only retail statement able to
check it.

### 2.3 Quality: tier-banded, and the patch notes say why

Observed quality (1-10) by island tier, across all 405 observations:

| tier | n | range | mean |
| ---: | ---: | --- | ---: |
| 1 | 11 | 1-4 | 2.82 |
| 2 | 28 | 2-6 | 4.43 |
| 3 | 86 | 2-8 | 6.17 |
| 4 | **280** | **7-10** | 8.67 |

Tier 4 is the load-bearing measurement: **280 observations, not one below quality 7.**
Update 31 states the cause — *"Metal quality is more in line with the biome they spawn
in"* — and the same notes give the sibling salvage system a hard tier->max-quality cap of
T1->7, T2->8, T3->9, T4->10, showing tier caps were an actual design device.

Inferred qualities are drawn from the empirical histogram of the island's own tier, so
they cannot leave the measured band. A test asserts that.

### 2.4 Table size

The median table size observed at that tier: **T1 -> 2, T2 -> 3, T3 -> 3, T4 -> 7.** Survey
tables are themselves partial samples, so a median under-claims rather than over-claims.

### 2.5 Determinism

Selection is keyed on the island's Steam workshop id through an explicit splitmix64
written out in the module — no `random` module, no Python-version dependence. The
catalogue regenerates byte-identically forever, and the self-check asserts it for all 254
islands.

---

## 3. How many nodes per island? — unchanged, and the old claim corrected

`ceil(LOD0 cells * 0.05)`, exactly as before. Only the `if metals` guard was removed.
**Which** metals and **how many** deposits were always independent questions; conflating
them is what left 216 islands barren, because an unsurveyed island had no metal *name* to
stamp on a deposit and so got no deposits either.

### A correction to the record

`findings-tier-one-world.md` and `HANDOVER.md` describe 0.05/cell as *"the recovered
retail figure"*. **That overstates it.** A full sweep of the decompile finds the field
names (`metalDepositDensity`, `minMetalRockDeposits`, `initialMetalRockDeposits`) and
confirms the island reports its LOD0 mesh count up to the spawner
(`IslandProxyVisualizer.cs`, `IslandMeshCount(myIsland.lod0Meshes.Length)`), but **the
formula combining them is not in the decompile** — it lived in the authoritative managed
worker, which was Scala and is lost. What is recovered is the *shape*: a density
multiplied by the island's own mesh-cell count, with a floor. The value **0.05 is ours**.
Those two documents are corrected in this branch.

The sibling system does survive in full and confirms the shape —
`acs/LootablePerAreaDataVisualizer.cs` computes databanks and loot containers from
surface area with min/max clamps and an exponential lerp. We do not need it for databanks
(the survey gives exact counts for all 254 islands) but it shows the design was
per-island, area-driven and floored.

### Placement is stricter than retail, deliberately

Retail placed metal by picking a random LOD0 vertex, accepting any normal with
`Dot(up, normal) > 0.4`, and testing a 2 m clearance sphere — **with no minimum spacing
between deposits at all** (only databanks and lootables got a 20 m rule). This generator
requires an upward normal of 0.90 and **35 m** spacing. That is a conservative choice,
not a reconstruction, and it is why the per-peer cost in section 4 stays low.

---

## 4. Load cost

### Totals

| | before | after |
| --- | ---: | ---: |
| Deposits (world) | 354 | **1930** |
| Atlas shards (world) | 354 | **1930** |
| Databanks (world) | 1233 | 1233 *(unchanged)* |
| Deposits (Tier 1) | 46 | **328** |
| Tier-1 release entities | 353 | **917** |
| Whole-world release entities | 2195 | **5347** |

Deposits per island: min 1, median 6, p90 16, max 37.

### Measured headless boot — `WAREBORN_RELEASE_WORLD_DISTRICTS=tier1`

Against a throwaway data directory on UDP 17831, production interest env:

```
[release-world] LOCAL TEST enabled: selectors='tier1', terrains=47, regions=5.
[world-directory] classified 1045 registrations: global=1, region=1044
                  (haven-region=127, release-a2-region=210, release-a3-region=179,
                   release-b2-region=256, release-b3-region=272), ship=0
[domain-host] local-single-process islands=47 ships=0 owned=997 globals=0
              unowned=0 duplicates=0
[terrain-interest] ON: 4000 m load / 4400 m unload hysteresis; resource checkout is
                   terrain-gated

activated: 328 deposit - 328 atlas shard - 215 databank - 81 tree - 24 fuel canister
```

481 -> 1045 classified registrations. **Zero unowned, zero duplicates, and no warning
beyond the four standard `WAREBORN_DB is not set` persistence notices.** Every deposit
activated through `WorldResourceActivation`, which is the harvest path — not decoration.

### Per-peer cost at the 120 m resource radius — the number that matters

The registry total is process-wide; what a peer pays is what falls inside its bubble.
Measured over all 254 islands, the **worst** 120 m neighbourhood anywhere in the world
contains **8 deposits** (pumpwerk-ruins), and the median island's worst case is **3**.

That is 8 deposits + 8 shards = **16 entities** at the absolute worst point in the world.
The 35 m minimum spacing is doing this work: a 6-deposit island spreads them over the
whole surface. **The interest radius is not widened and does not need to be.**

### Connect-plan risk — unchanged, and still guarded

The recorded crash came from too many entities entering the immutable connect plan.
`ReleaseWorldConnectCostTests` asserts the property that prevents it and still passes:
every release terrain root is `AfterPlayer`/`IsManaged` and every deposit, shard and
databank is a gateable streamed-resource key, so an out-of-range step is fast-forwarded
in one turn without sending anything. The nearest tier-1 island is 9.33 km from the Haven
spawn against a 4 km terrain radius, so **a fresh Haven connect still streams nothing from
the Wilderness** — the test asserting the streamed set stays under 40 entities is
unchanged and green.

The 4 km terrain radius and 120 m resource radius were not touched, and concurrent spawn
producers were not re-enabled.

---

## 5. Rejected: importing `jerodar/WAMap`

It is a genuine second survey at 89% coverage and it would lift raw coverage from 61/254
to about 108/254. It is **not** imported, for a reason specific to metals rather than the
existing layout-mixing rule in `PROVENANCE.md`:

- It predates Update 31. It has **12 metals, not 15** — no Epilar, no Eternium, no Orthite.
- Update 31 **explicitly rewrote metal balance**: *"Metal rarity distribution changed...
  Quality is also no longer dependent on rarity"*, *"metal quality is more in line with
  the biome they spawn in"*, plus the Orthite and Nickel retierings.
- Its qualities demonstrably belong to the old balance: it records Aluminium Q1 and
  Tungsten Q1 on tier-4 islands. In the release build, **280 tier-4 observations never go
  below Q7**.

Importing it would write quality values into the release world that the release build
provably never produced — real data from the wrong game. Its value here is as
corroboration (section 1.5), and this section exists so the next person to find it does
not have to re-derive the reason.

---

## 6. What still needs a live client

Nothing below was proved with a real Unity client.

1. **That 1930 deposits render and mine in-game.** The server side is asserted — every
   deposit resolves through `MetalDeposits.ByKey`, carries a variant id and a quality in
   1-10, and activates on boot — but a player has mined only the pre-existing ones.
2. **That an inferred deposit is visually indistinguishable from a surveyed one.** It
   goes through the same registration, the same seeding and the same yield curve, so
   there is no mechanism for it to differ; that is an argument, not an observation.
3. **Density at 328 deposits across 46 tier-1 islands.** Whether 6 deposits on a median
   island *feels* right is a playtest question, not a data question. `WAREBORN_ATLAS_RATE`
   thins shards; deposit count needs a regeneration with a different constant.
4. **Deposit respawn.** Retail replaced all metal nodes every 1.5-2 hours at understorm
   (<https://worldsadrift.fandom.com/wiki/Islands>). Nothing here implements that; a mined
   island stays mined. `MetalRocksRequiredToRespawn` in component 1010 is the retail hook
   and is unimplemented.

## 7. Sources

- Decompile: `acs/IslandProxyVisualizer.cs`, `acs/IslandSurfaceData.cs`,
  `acs/LootablePerAreaDataVisualizer.cs`,
  `gencode/Bossa.Travellers.Islands/IslandResourceSpawnerStateReader.cs` (1010),
  `gencode/Bossa.Travellers.Materials/MetalRockStateReader.cs` (1032).
- <https://github.com/cardinal-guild/wamap> — `static/islands.json`, `static/metaltypes.json`
  (the canonical 15 metals and their `type_id`s), `components/IslandPopup.vue`.
- <https://github.com/cardinal-guild/wasurveyor> — `src/Entity/Report.php`,
  `src/Controller/ApiController.php`, `src/Controller/Admin/ReportAdminController.php`.
- <https://worldsadrift.fandom.com/wiki/Update_31> — the release build's patch notes.
- <https://worldsadrift.fandom.com/wiki/Metal>, `/Mining`, `/Biome`, `/Islands`, `/Salvage`.
- <https://github.com/jerodar/WAMap> — `data/island_data.csv`. Corroboration only; see
  section 5.
