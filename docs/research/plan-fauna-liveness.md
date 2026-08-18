# Making the island fauna feel alive — cost budget and plan

Planning document, 2026-08-18. Written on `feat/fauna-schools` after merging `main`
at `0a8c1e8`. NOTHING IN HERE IS IMPLEMENTED. The request was costs first, then a
plan, then a choice.

The complaint being answered, verbatim: *"the fauna — you spawn one group of each,
but that's so static and not real. how do we make this feel more natural, more
alive?"*

What the world serves today: exactly one manta school and one jellyfish shoal per
island, at fixed sizes (`4 + (tier-1)` mantas, `6 + 2(tier-1)` jellies), one
perimeter orbit and one day/night vertical cycle. 460 creatures over 46 tier-1
islands. Every island is the same island.

---

## 1. The cost budget, measured

### 1.1 Server compute is not the constraint, and the code says why

Measured on live production with fauna on and 460 creatures live:

| | |
|---|---|
| game server CPU | **1.00%** of one core (10 s sample) |
| game server RSS | **135 MB** |
| fauna telemetry | **3.2 KB** of a 54.2 KB stats file |

That is not a coincidence of the current population — it is structural, and it is
worth stating because it is the one budget that will not move however far this plan
goes:

- `IslandFaunaService.TickPoses` returns immediately when no peer holds a creature
  (`AnyPeerHoldsFauna`), so an empty server's fauna cost is one integer compare per
  loop turn.
- when peers *are* connected, `_registry.DuePoses(_held.Contains)` evaluates only the
  creatures somebody is actually holding. The ceiling is therefore
  `peers x per-peer budget` poses per 250 ms — **not** world population. Today that is
  24 closed-form evaluations per peer per 250 ms.
- the only per-turn work that scales with the WORLD is `Reconcile`, which is
  `O(peers x islands)` every 500 ms: 46 islands x 2/s per peer = 92 envelope distance
  tests a second.
- a creature costs one `FaunaPlacement` dictionary entry. 460 of them is on the order
  of 100 KB against a 135 MB process.

**Conclusion: world population is essentially free.** `WAREBORN_ISLAND_FAUNA_MAX`
(default 4000, full catalogue demand 3,866) bounds only how much wildlife *exists*.
It could be raised by an order of magnitude without moving any number above. Do not
design around it.

### 1.2 Per-peer wire — the number that matters

| | value | source |
|---|---|---|
| pose cadence | 250 ms (4 Hz) | `IslandFaunaRegistry.DefaultPoseInterval` |
| per-peer creature cap | 24 | `IslandFaunaInterestPolicy.DefaultPerPeerCreatures` |
| **worst case to one peer** | **96 transform updates/s** | 24 x 4 |
| payload | position 3 x `int64` (24 B) + `Quaternion32` (4 B) + float stamp (4 B) = **32 B**, plus CoreSdk protobuf framing, entity id, component id and ENet header | `FixedPointPosition`, `ShipPartTransform.BuildParentlessWakeUpdate` |
| **estimated wire** | **60-100 B per update -> 6-10 kB/s** at the ceiling | ESTIMATE, see below |
| reliability | 190602 is UNRELIABLE and SUPERSEDING | `MirrorSendPolicy.RelayReliabilityFor` |

The byte figure is an ESTIMATE, not a measurement: the payload goes through the
CoreSdk's generated binary serializer and then a protobuf op, so the framing overhead
is not derivable by reading C#. It can be measured exactly with the `[relay-stats]`
byte counters or an ENet capture, and that measurement is cheap. What is *not* an
estimate is the update RATE, and rate is what the standing multiplayer-safety rule is
about.

**The anchor that makes 96/s legible:** the avatar relay runs at 20 Hz
(`RelayEmitter`, `WAREBORN_RELAY_HZ` clamped 15-30). So the fauna ceiling is the same
update rate as roughly five nearby players' avatar traffic — significant, but not the
dominant sender, and unlike the avatar relay it is unreliable-and-superseding, so a
dropped fauna packet costs one frame of smoothness and never a desync.

### 1.3 The wire cost nobody costed: ARRIVAL

This is the sleeper constraint, and it binds before the pose rate does.

`IslandFaunaService.SendInterval` is 120 ms, and admitting one creature takes **two**
sends: an `AssetLoadRequest`, then one full cadence later an `AddEntity`. Admission is
whole-island. So a peer arriving at an island of `P` creatures waits

> **0.24 x P seconds** for its population to finish arriving

| island population | time for the whole population to stream in |
|---|---|
| 10 (today's tier 1) | 2.4 s |
| 19 (today's largest, tier 4) | 4.6 s |
| 24 (today's per-peer cap) | 5.8 s |
| 48 | 11.5 s |
| 60 | 14.4 s |

`MaxQueuedPerPeer` is 128. **This is the real ceiling on "one island's whole
population arrives together"**, and it is the reason a plan that simply multiplies
counts produces a school that trickles into view over a quarter of a minute rather
than a school that is there when you fly in. Any proposal that pushes an island past
roughly 24-30 creatures must either shorten `SendInterval` for fauna, batch the
asset-request step, or accept a visibly slow arrival.

### 1.4 Client frame cost — MEASURED, and the premise has changed

The brief said "frame cost demonstrably tracks entity count". That **was** true, and
as of 2026-08-18 it is **no longer** true, and the reason is already in this repo.

`tools/perf/README.md` (commit `2865af5`) proves the old per-entity cost was Wine
fsync sync-amplification, not per-entity work: Unity 5.6 spawned 27 job workers, every
job dispatch fired a 27-way futex wake, and *that* is what made frame time track
entity count (`ents=67 -> 52 fps`, `ents=98 -> 42 fps`). Confining the workers to one
CPU took the same scene from 51.4 to 120.9 fps, dropped process CPU from 807% to 137%,
and moved the limiter to the GPU (busy 45% -> 84%).

**So I re-measured the per-entity cost on the post-fix client**, from the mod's own
`[WAR][perf] beat` heartbeats in `~/Games/WorldsAdrift/BepInEx/LogOutput.log`. 25 clean
beats (`spikes=0`), entity count 129-177, frame time 9.21-10.43 ms:

| fit | slope | interpretation |
|---|---|---|
| frame ms ~ entities | **0.0107 +/- 0.0054 ms/entity** (95% CI 0.0001 .. 0.0214) | naive |
| frame ms ~ uptime, at CONSTANT ents=177 (n=13) | **0.00159 ms per second of uptime** | there is a real drift |
| frame ms ~ entities + uptime (n=25) | **entity: -0.0044 +/- 0.0050 ms** (95% CI -0.0143 .. +0.0055)<br>**uptime: +0.00095 +/- 0.00020 ms/s** (4.75 sigma) | the honest answer |

Entity count in that session rises with uptime, so the naive fit is confounded. Once
uptime is controlled for, **the per-entity coefficient is statistically zero**, and the
significant term is a session-length drift.

**The client budget, stated honestly:**

> Between 129 and 177 held entities, the marginal cost of one more entity is
> **below 0.0055 ms at 95% confidence** — under 5.5 microseconds. At that upper bound,
> **100 additional entities cost 0.55 ms**, which at a 9.8 ms frame is about
> **5 fps out of 102**. The measured point estimate is indistinguishable from zero.

Caveats, all of which matter:

- the measured range is **129-177 entities**. Extrapolating to 400 is not supported by
  this data, and the GPU was already 84% busy at ents=98 in the perf A/B, so the
  ceiling is a GPU ceiling and will be found by *what* is drawn, not how many entities
  exist.
- `ents` in the beat line is cumulative `AddEntity` ops, which equals held entities for
  a player who is not unloading anything. The session was largely parked.
- one session, one machine, 3440x1440, DXVK, worker pin ON. A player without
  `pin-unity-workers.sh` is still on the old curve, where the slope was ~0.15 ms/entity
  — **27x worse**. Anything shipped here should assume some players are unpinned.
- the regression cannot separate a creature entity from a tree entity. A manta is a
  skinned mesh with an Animator, four tail LOD sets and a material-expression
  behaviour; a rock is not. Per-entity cost is not uniform across entity kinds.

**The uptime drift is a separate finding and is worth someone's attention on its own:**
0.00095 ms per second of uptime is 3.4 ms per hour, which turns 102 fps into about
75 fps after an hour in world, at a constant entity count. That is not a fauna problem
and this plan does not address it, but it is a real, significant, measured regression
sitting in the same data.

### 1.5 The client cost that IS real, and is a bug: the manta exception storm

This is the largest single client-side fauna cost in the game right now, and it is not
a rendering cost.

Measured in `~/Games/WorldsAdrift/BepInEx/LogOutput.log`:

| | |
|---|---|
| `NullReferenceException` from `MantaRayVariantClient.UpdateMaterial` | **383,632** |
| all other `NullReferenceException`s in the whole log | **148** (0.04%) |
| frames covered by the beat heartbeats | 106,538 |
| **exceptions per frame** | **3.60** — and a tier-1 island carries exactly **4 mantas** |
| share of the 145 MB log file that is this one stack | **97%** (140.8 MB) |

So: **one thrown exception, with a stack trace and five lines synchronously written to
disk, per held manta per rendered frame.**

ROOT CAUSE, RECOVERED. `acs/Assets.Scripts.Visualisers.Creatures/MantaRayVariantClient.cs`
sets `MyVariantSettings` in exactly one place, `PickTail`, and `PickTail` is called from
exactly two places:

```csharp
private void OnGenderUpdated(GenderStateData.GenderType gender)  { PickTail(variantStateReader.BiomeType, gender); }
private void OnVariantTypeUpdate(BiomeType biomeType)            { PickTail(biomeType, genderStateReader.Gender); }
```

— the update callbacks for **1177 `GenderState`** and **4326 `MantaRayVariantState`**.
This server sends neither. So `MyVariantSettings` stays null, and every frame
`MantaRayMaterialExpressionClient.Update()` -> `UpdateDynamicTraits()` ->
`UpdateMaterial()` dereferences `GetMyVariantSettings()` and throws.

The consequence is not only the cost. It is that **the manta's per-biome tail and its
male/female body are never selected at all** — the client ships four tail meshes
(`TailArrow`, `TailBarbed`, `TailForked`, `TailStraight`, each `_LOD0..2`, with matching
packed textures) and every manta in the world is currently rendering the un-picked
default with a dead material block.

Jellyfish do not do this: `JellyFish` appears 4 times in the entire log. The jelly
pipeline (`BasicCreaturePreprocessor`) requires no gender, no variant and no aging, so
it is clean.

### 1.6 The soak gate is blind to this feature

`tools/relaybot/run-soak.sh` reports `VERDICT: FLAT` at 3,866 creatures and at 460, and
it also reports `fauna: 0 creature checkout(s)` in every run, even with `--centre`
placing bots inside a real island envelope. Optional island terrain is never checked
out to a bot, and `IslandFaunaService` gates `AddEntity` on `_terrainReady`, so a bot
is never shown a creature.

**The gate therefore proves only that a large fauna POPULATION does not perturb relay
staleness. It does not exercise the checkout path or the pose path at all.** The
per-peer rate bound is held by `IslandFaunaInterestPolicy`'s cap and its unit tests,
not by the soak.

Any proposal below that raises the per-peer creature count ships ungated until this is
fixed. That is why it is Phase 0.

### 1.7 The budget, in one table

| resource | today | headroom | binds at |
|---|---|---|---|
| world-wide population | 460 (cap 4000) | ~10x, free | memory / seed time only |
| per-peer creatures | 24 | wire says yes to 48+; **arrival says no** | 24-30 before arrival becomes visible |
| per-peer update rate | 96/s | ~5 nearby players' avatar relay | unmeasured; soak cannot see it |
| client frame cost | 10 creatures of 177 entities | **<0.0055 ms/entity (95%)**, ~5 fps per +100 entities | GPU, and only for *pinned* clients |
| client exceptions | **3.6/frame, 4/4 mantas** | this is a bug, not a budget | fix it before adding mantas |
| arrival time | 2.4 s per island | 0.24 s per creature | the 120 ms `SendInterval` |

**The single most important reading of this table: the constraint is not the wire and
not the frame. It is (a) a client bug that scales linearly with manta count, and
(b) the 120 ms arrival pipeline. Both are fixable, and neither is what anyone would
have guessed.**

---

## 2. What retail actually had, versus what we serve

Recovered 2026-08-18 by three independent sweeps of `/home/ttanurhan/Games/WAReborn-decompiled`,
the shipped `resources.assets`, and the extracted world data. Every claim below is
RECOVERED (read from a file) unless marked otherwise.

### 2.1 The species vocabulary is COMPLETE, and it is small

`gencode/Bossa.Travellers.Creatures/SpeciesType.cs` — the whole file:

```csharp
public enum SpeciesType { None, Tree, Beetle, MantaRay, Stalker }
```

`gencode/Bossa.Travellers.Creatures.Basic/BasicSpeciesType.cs` — the whole file:

```csharp
public enum BasicSpeciesType { None, JellyFishSeed, JellyFishFlower, JellyFishDesertA, JellyFishDesertB }
```

There is nothing else. No birds, no fish, no land mammals, no predators. The earlier
"known partial" lists were in fact complete. Retail's animal vocabulary is **three
renderable animals** — Beetle, MantaRay, JellyFish — plus `Tree` as a first-class
species (trees used the same habitat, population and egg machinery).

**`Stalker` has no assets whatsoever.** The string appears in exactly two files in the
48 MB decompile, both being the enum declaration; a case-insensitive grep of
`resources.assets`, `globalgamemanagers`, `sharedassets0/1.assets` and `level1` in the
retail client returns zero hits. It is a reserved wire value. Do not ship it.

### 2.2 What we are not spawning, and what the client can actually draw

Every one of these has a container key in `globalgamemanagers` and a backing GameObject
in `resources.assets`, i.e. the retail client can instantiate it today if the server
names it:

| prefab | status here | client support |
|---|---|---|
| `MantaRay` | **served** | full: `MantaRayAnimationClient`, `MantaRayVariantClient`, `MantaRayTail`, `MantaRayMaterialExpressionClient`, `RayAging`, collider states |
| `JellyFish` | **served** (generic) | `JellyFishAnimationClient`, `JellyFishMaterialExpressionClient`, `ContactFixedDamageClient` |
| `SeedPodJelly` | not served | as above — this is `JellyFishSeed` |
| `FlowerPodJelly` | not served | as above — this is `JellyFishFlower` |
| `DesertPod`, `DesertPodB` | not served | as above — these are `JellyFishDesertA/B` |
| `FlowerPodFireJelly` | not served | as above — a **fifth** jelly asset with **no enum member** |
| `Beetle` | **not served** | full: `BeetleAnimationClient`, `BeetleVariantClient`, `BeetleOfflineController`, `BeetleMaterialExpressionClient`, `BeetleAging`, collider states |
| `BeetleEgg`, `MantaRayEgg`, `MantaRay_Egg`, `Egg`, `EggHolder` | not served | `HatchVisualiser`, `MantaRayEggSetter` |
| `Flock` | not served | `FlockVisualiser`, `FlockClientVisualiser` — the attractor entity itself |
| `Patrol` | not served | `PatrolVisualiser`, `PatrolReader` |
| `BasicCreatureSpawner` | not served | the jelly spawner entity |
| `BigCall`, `DiscoWhale` | not served | ambience, not simulated creatures |

The beetle's shipped art is not a stub. `resources.assets` carries three heads matching
`BeetleVariantType { Grass, Moth, Crab }` — `Beetle_Head*`, `Moth_Head` /
`moth_head_BABY_LOD0..2` / `Beetle_MothHead_Elder`, `Crab_Head` / `crab_head_baby_LOD0..2`
— with aging masks for each, and an animation set that is richer than the manta's:
`beetle_idle_flying`, `beetle_flying_to_landing`, `beetle_landing_to_flying`,
`beetle_feeding_{into,loop,outof}`, `beetle_resting_onground`, `beetle_into_ram`,
`beetle_ram_loop`, `beetle_ram_impact`, `beetle_gethit_flinch`, `beetle_tailSwing`,
`beetle_being_eaten`, `beetle_death_pose`, and ten `beetle_poses_headlook_*`.

The manta likewise ships `MantaRay_flying`, `_flying_fast`, `_glide`, `_flinch`,
`_landing`, `_attached_to_ship`, `_flying_idle`, `_flying_turn_pose`, plus audio events
`Play_MantaRay_Wingflap_Fast` / `_WingFlaps_Slow` / `_Attacks`.

**None of that animation vocabulary is reachable today, because this server sends a
creature's transform and its species and nothing else.**

### 2.3 The variant selector is BIOME, not the variant enum

A trap worth writing down. `MantaRayVariantStateData` and `BeetleVariantStateData` each
carry *both* an `Option<...VariantType>` and a `BiomeType` — and the client subscribes
only to `BiomeTypeUpdated`, never to the variant type:

```csharp
variantStateReader.BiomeTypeUpdated += OnVariantTypeUpdate;   // MantaRayVariantClient
private void Awake() { OnBeetleVariantTypeUpdated(BiomeType.Biome1); }  // BeetleVariantClient
```

`MantaRayVariantType { ARROWTAIL, FORKTAIL }` and `BeetleVariantType { Grass, Moth, Crab }`
are vestigial. **The live selector is `BiomeType` (1-4) plus gender.** Send a variant
type without a biome type and the client renders Biome1 defaults — which is exactly
what would happen if this were implemented from the enum names rather than from the
callbacks.

The biome->tail table itself is `[SerializeField]` data inside the Unity prefab, so
*which* tail belongs to *which* biome is not in the decompile. It is extractable from
`~/Games/WorldsAdrift/Assets/unity/` if anyone wants it; it is not needed to fix the
storm, because any valid biome value populates `MyVariantSettings`.

### 2.4 Biome IS recoverable per island — and it turns out not to help

This was the headline of the population sweep, and the answer is a clean negative that
saves work.

`BiomeType { Biome1..Biome4 }` is assigned by nearest Voronoi centre over X/Z
(`acs/GlobalBiomeDataVisualizer.GetBiomeAt`, `acs/IslandSurfaceData.cs:171`), backed by
component 1253 `GlobalBiomeVoronoiCentresState`. **The 20 centres are already in this
repo**, in `docs/research/world-data/wamap-islands.json` under `"Biomes"` — Bossa's own
file, per `PROVENANCE.md`.

Running that lookup against all 254 catalogue islands:

- district agreement **254/254** — so `island.district -> biome` is a pure table join,
  no geometry needed;
- **biome == tier for 253/254**, the sole exception being *Holy Ruins* (district A4,
  catalogue tier 3, Voronoi Type 2);
- **all 46 tier-1 islands are BiomeType 1, Saborian, districts A2/A3/B2/B3.**

**Biome carries zero discriminating power inside tier 1.** It separates tiers, which we
already have. It is still worth recovering for one reason only: it is the value the
manta and beetle variant clients key their meshes on (2.3), so it is needed for the
storm fix even though it is useless as a population driver.

Related correction, free: `docs/admin-map-preview.html:817-822` labels Biome3 as
`terrain:'ice'`. The decompile says otherwise —
`acs/AmbienceSoundController.cs:113-123` maps `Biome3Island -> Play_AmbientMain_Jungle`
and `BiomeMusicState[Biome3] = "Forest_ALT"`, and `acs/MusicPlayer.cs:118-128` agrees.
Biome3 is jungle/forest, not ice.

### 2.5 Island SIZE is the driver that biome and tier are not — and it is RECOVERED

Measured from `release-runtime-catalog.json`'s own extracted AABBs.

Islands per tier: 46 / 50 / 82 / 76. Horizontal half-diagonal — the exact quantity
retail's patrol used (`new Vector2(BoundsExtents.x, BoundsExtents.z).magnitude + 10f`):

| tier | n | min | p25 | median | p75 | max |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 46 | 73.7 | 208.3 | **291.0** | 391.0 | 615.8 |
| 2 | 50 | 80.2 | 194.9 | 243.1 | 333.5 | 629.0 |
| 3 | 82 | 60.9 | 198.4 | 255.7 | 326.2 | 691.2 |
| 4 | 76 | 7.9 | 218.1 | 290.4 | 423.0 | 666.7 |

**Size does not track tier at all** — tier 1's median (291.0 m) is indistinguishable
from tier 4's (290.4 m). Size is an *orthogonal* axis, which is precisely what makes it
usable.

Within tier 1 alone: half-diagonal spans **8.4x** (73.7 m *Romerius Manor* to 615.8 m
*Saborian cave ruin*), XZ footprint spans **69x**, AABB volume spans **787x**. The
distribution is unimodal with a mode at 200-250 m and a long right tail.

And there is a second, independent reason this is the right driver: retail's own
habitat and flock geometry *were* the island's AABB extents.
`acs/Bossa.Travellers.Visualisers.Habitat/HabitatVisualiser.cs`:

```csharp
habitatFsimStateWriter.Update.HabitatHeight(isd.BoundsExtents.y).FinishAndSend();
```

`acs/Assets.Scripts.Visualisers.Creatures.Flock/FlockVisualiser.cs:187`:

```csharp
flockStateWriter.Update.OriginHabitatRadius(lateralIslandBounds.magnitude)
                       .OriginHabitatHeight(originIslandSurfaceData.BoundsExtents.y).FinishAndSend();
```

So scaling a population by island size uses a RECOVERED input in the same frame retail
used it. Only the proportionality constant is WAREBORN TUNING, and that is unavoidable.

### 2.6 Populations remain NOT RECOVERABLE, and this is now exhaustive

Re-confirmed rather than re-litigated. `PopulationManagementStateData` holds only
`Map<SpeciesType, long> timeSinceSpeciesPopulationCriticallyLow` — a timestamp map, no
threshold. `SpeciesInhabitantsRecord` is two unbounded `List<EntityId>`.
`BasicCreatureSpawnerState` carries an inhabitants map and a `hasDoneGenesisSpawn` flag.
`HabitatStateData` carries observed inhabitants, arrival times, incoming/outgoing flock
maps and a `biomeType` — an *observation*, never an *eligibility list* or a capacity.

And the decompile ships **no ecology data file at all**. A `find` for non-`.cs` files
across all 8,501 files returns five: three `.csproj`, plus `component-map.tsv` (a flat
id<->name index, no payloads) and `ecs_config.json` (880 bytes of view/weather system
plumbing). There is no JSON, CSV or asset dump of species tables, counts, densities or
biome eligibility anywhere.

Every count in this plan is therefore **WAREBORN TUNING**, exactly as
`IslandFaunaPolicy` already says.

### 2.7 What we serve today, against all of that

| retail had | we serve |
|---|---|
| 3 renderable animals + Tree | 2 (MantaRay, JellyFish) |
| 4 jelly species, 5 jelly prefabs | 1 generic `JellyFish` prefab |
| per-biome tails x 2 genders on mantas | none — and it throws 3.6 exceptions a frame |
| beetles with 3 heads and 20+ animation clips | none |
| eggs, hatching, egg holders | none |
| a flock as a real networked attractor entity (1199 + 1197) | geometry only; the client is never told a group is a group |
| habitats, patrol entities, spawners | none |
| health, age, gender, mortality, corpses, meat | none |
| 14 conducts (feeding, resting, mating, fleeing, attacking, patrolling...) | none |
| contact damage (jelly shock) | none |
| a flock that migrates between habitats | none |

Stage 3 and stage 4 of `findings-island-fauna.md`'s own implementation path are both
untouched. **The server owns each creature's transform and its species, and nothing
else.**

---

## 3. Proposals, costed

Every proposal is costed on six axes: what it does for the feel; RECOVERED vs WAREBORN
TUNING; wire cost; client cost; whether it keeps closed-form evaluation; and
implementation size and risk.

"Closed-form" means a pose stays a pure function of `(creature, envelope, elapsed
seconds)`. That property is not decoration: it is what lets the server evaluate only
what somebody is watching (1.1), what makes the world restart-reproducible, and what
lets the admin console's live fauna layer evaluate the same function in the browser —
guarded by `AdminFaunaParityTests`, which cuts the marked JS mirror out of the real
served page, runs it in node against the real published model, and asserts agreement to
a nanometre. **Any change to the motion must be mirrored in `WorldsAdriftServer/Web/AdminPage.cs`
and re-asserted by that test.** That is a real, recurring implementation tax and it is
priced into every movement proposal below.

Sizes are: **XS** under ~100 lines of production code plus tests, **S** ~100-300,
**M** ~300-700, **L** more.

---

### A. Kill the manta exception storm, and get per-biome tails and genders for free

Send **4326 `MantaRayVariantState`** (with the island's `BiomeType`) and
**1177 `GenderState`** at AddEntity.

**Feel.** Every manta in the world currently renders an un-picked default with a dead
material block, because `PickTail` never runs (1.5). Sending these two components makes
the client select one of four shipped tail meshes (`TailArrow`, `TailBarbed`,
`TailForked`, `TailStraight`) and a male or female body. A school of four stops being
four copies of one asset.

**Provenance.** **RECOVERED throughout.** The callbacks are quoted in 1.5. The biome
per island is a pure table join on `district` against Bossa's own Voronoi centres in
`docs/research/world-data/wamap-islands.json`, 254/254 agreement (2.4). Only the
male/female assignment rule is WAREBORN TUNING — retail's genders came from GSim
breeding, so any deterministic rule (e.g. alternate by member index) is ours.

**Wire.** Two extra components in the AddEntity seed, **sent once per checkout**. Zero
ongoing rate. It does not touch the 96 updates/s ceiling.

**Client.** **Strongly negative** — this is the only proposal here that makes the client
*faster*. It removes 3.6 thrown exceptions with stack traces per frame, and 97% of the
log file's disk writes.

**Closed-form.** Unaffected. This is static per-creature data, not motion. The admin map
does not evaluate it.

**Size.** **S.** Two `ComponentsSerializer` branches, a district->biome table, a gender
rule, tests. The biome table is a small data addition to the release catalogue loader.

**Risk.** **Low**, with one thing to verify first: whether `MantaRayVariantClient`'s
`_variantsSettings` array is populated in the shipped prefab. If the array is empty,
`PickTail` sets nothing and the storm survives; the fix would then need the prefab data
extracted from `~/Games/WorldsAdrift/Assets/unity/`. **Verify by sending the components
to one manta and watching the log stop.** That is a ten-minute experiment and it should
be done before anything else in this plan.

---

### B. Recover the day length: 1200 s becomes 600 s

**Feel.** The jelly day/night cycle is the only behaviour in the world that changes over
time, and at 1200 s a player sees at most one transition per visit. At 600 s they see
two, and the shoal's climb from the island's underside to its rim becomes something you
notice rather than something you would have to sit still for.

**Provenance.** **CLIENT-DEFAULT RECOVERED — better than invention, short of proof.**
`findings-island-fauna.md` currently says "THE MANTA SPEED AND THE DAY/NIGHT CYCLE
LENGTH ARE PURE INVENTION". Both claims can now be improved, and the improvement must be
stated carefully:

- `acs/Assets.Visualizers/WorldStateVisualizer.cs:16` compiles in
  `private float _timeRate = 144f`, and line 67 computes
  `DayNightCycle.Instance.timeForFullCycle = 86400f / _timeRate` — **600 s, a ten-minute
  day**. **The honest caveat:** line 38 immediately overwrites it with
  `_timeRate = _worldData.TimeRate`, so 600 s is *the value the retail client was built
  with and falls back to*, not proof of what the live `WorldData.timeRate` carried. That
  number was server data and is lost. But a compiled-in client default is a far stronger
  anchor than a number this project chose, and it should be adopted and labelled exactly
  this way: RECOVERED CLIENT DEFAULT, not recovered live value.
- the day *window* is fully recovered and already used here: `time in (0.2, 0.8)`
  (`JellyFishMovement.cs:135`).
- the manta's 8 m/s is corroborated too:
  `WanderingConductVisualiser.targetWanderVelocityMagnitude = 8f` — a plain hardcoded
  field, not `[SerializeField]` prefab data, so it genuinely survives. It is a creature's
  *wander* speed rather than a patrol speed, but it is the same number this server
  guessed, and "pure invention" is no longer the right label for it.

**Wire.** Zero. **Client.** Zero.

**Closed-form.** Fully preserved — it is one constant. It is published through
`FaunaMapConstants.DayNightCycleSeconds`, so the admin map follows automatically; the
parity test needs re-running, not rewriting.

**Size.** **XS.** One constant, plus updating the doc's own "pure invention" claim.

**Risk.** **Very low.** The only consideration is that the world clock this server keeps
is not tied to a `TimeKeeperState` on the wire, so this is "retail's day length" rather
than "the client's day agrees with the server's".

---

### C. Give the jellies their four species back

Serve `SeedPodJelly`, `FlowerPodJelly`, `DesertPod` and `DesertPodB` instead of one
generic `JellyFish`, chosen per island (and optionally per member).

**Feel.** A shoal today is 6-12 instances of one mesh. This is the cheapest visual
variety available anywhere in the feature: it costs nothing per creature and changes
what every shoal looks like.

**Provenance.** **RECOVERED that they exist and are renderable** — all four have
container keys in `globalgamemanagers` and backing GameObjects in `resources.assets`,
and `BasicSpeciesType` names them exactly. There is a fifth,
`FlowerPodFireJelly`, with a prefab but **no enum member** — a late addition or a cut
variant. **Which island got which is NOT RECOVERED** and never will be; the names are
suggestive (`DesertA/B` against Biome4) but nothing states it. So: the assets are
recovered, the assignment is WAREBORN TUNING, and this must be labelled that way — the
existing `IslandFaunaPolicy` comment explaining why all four were collapsed into one is
correct and would need rewriting, not deleting.

**Wire.** Zero — a different prefab name in the same AddEntity.

**Client.** Zero extra entities. Possibly a small extra memory cost from loading four
prefabs instead of one on an island that mixes them.

**Closed-form.** Unaffected.

**Size.** **XS-S.** A `PrefabNameFor(species, island, member)` and a deterministic
assignment. The `FaunaSpecies` enum probably wants four jelly members, or a separate
variant field on `FaunaCreature`.

**Risk.** **Low-medium.** Each pod prefab's `[Require]`d component set must be checked
the way A checks the manta's; if `DesertPod` requires something `JellyFish` does not, it
will throw. The jelly pipeline is much lighter than the manta's (no gender, no age, no
variant client), so this is unlikely — but "unlikely" is exactly the assumption that
produced the storm in 1.5.

---

### D. Let island SIZE decide how much lives there

Replace `PopulationFor(tier)` with a function of tier **and** the island's own envelope.

**Feel.** This is the direct answer to "every island is the same island". Today a tier-1
island carries exactly 10 creatures whether it is 74 m across or 616 m across. Under
this proposal a small rock carries a handful and a large one carries a crowd, and the
difference is visible from the air.

**Provenance.** **RECOVERED input, WAREBORN TUNING constant** — and the input is not
merely available, it is the *same quantity retail used*. The manta's orbit radius is
already the island's horizontal half-diagonal plus 10 m (`PatrolVisualiser.cs`), the
habitat's height is literally `isd.BoundsExtents.y` (`HabitatVisualiser.cs`), and the
flock's roaming radius is `lateralIslandBounds.magnitude`
(`FlockVisualiser.cs:187`). Retail sized its ecology by the island's own AABB. So does
this.

The measured spread inside tier 1 is **8.4x on half-diagonal, 69x on footprint** (2.5),
which is far more variation than tier gives — and unlike biome (which is constant across
all 46 tier-1 islands) it actually discriminates.

**Wire.** Unchanged *per creature*, but this is the proposal that stresses the per-peer
budget. **Whole-island admission means an island whose population exceeds
`WAREBORN_ISLAND_FAUNA_PEER_MAX` is admitted as nothing at all** — the invariant
`IslandFaunaPolicy` currently states as "PopulationFor's largest output (19) is
deliberately below the per-peer budget". Any size scaling must preserve that invariant
or raise the budget, and raising the budget is what Phase 0 gates.

**Client.** Proportional to the largest island's population, not the average. Under the
measured budget (1.4) even 40 creatures is under 0.25 ms on a pinned client; the real
objection is the arrival pipeline (1.3), where 40 creatures is a 9.6 s stream-in.

**Closed-form.** **Fully preserved.** Population becomes a pure function of catalogue
data, which is already how it works — it just reads one more field. The admin map's
`FaunaIslandPopulation` record *already* carries per-island `MantaRays`, `JellyFish`,
`Schools`, `MantaSchoolSize` and `JellyShoalSize`, so the roster can express this today
with no schema change.

**Size.** **S.** `PopulationFor` gains an envelope argument; the map projection already
has the fields.

**Risk.** **Medium**, entirely because of the budget interaction above. Mitigation: clamp
the per-island population to the per-peer budget and say so, so a big island is dense
rather than invisible.

---

### E. More than one group per island, at different phases and altitudes

Raise `SchoolsPerIsland` above 1 and give each school its own orbit radius and vertical
band rather than only its own phase.

**Feel.** A player currently sees one ring. Retail's islands had more than one thing
going on at different heights, and a second group at a different altitude is the
difference between "there is a school here" and "this place is inhabited".

**Provenance.** **The multi-group structure is RECOVERED, and more strongly than
expected.** `HabitatPatrolState` (4332) is
`Map<SpeciesType,float> speciesOrbitDegrees | Map<SpeciesType,Coordinates> speciesTargetCoordinates`
— **a separate, independently-advancing orbit phase per species on the same island**,
with the debug visualiser colouring Beetle yellow and MantaRay red. Retail explicitly
ran two patrols around one island at once. `HabitatState` likewise keys everything by
species and tracks plural `incomingFlocks`/`outgoingFlocks`.

What is NOT recovered is how many groups of *one* species an island had. That is
WAREBORN TUNING, like every other count.

**Wire.** Population multiplies by the school count; per-creature cost unchanged. Same
budget interaction as D.

**Client.** Proportional. Note that two schools at different altitudes are more likely to
be *simultaneously on screen* than one school is, so the GPU cost lands more of the time
even though the entity count is the same.

**Closed-form.** **Preserved, and most of it is already built.**
`IslandFaunaSchool.SchoolPhaseFraction` already spreads schools by the golden ratio and
is already published to the map. What is missing is per-school variation in *radius* and
*band* — today every school of a species shares one `MantaOrbitRadiusOf` and one
vertical band, so raising the count gives two schools chasing each other around the same
circle. That new variation must be mirrored in `AdminPage.cs`'s JS and re-asserted by
`AdminFaunaParityTests`.

**Size.** **S-M.** The count is a constant; the per-school geometry is the work, and the
JS mirror plus parity test is most of it.

**Risk.** **Low.** Nothing here can cause interest churn — a creature's position stopped
being an interest input when checkout moved to the island (1.2/`IslandFaunaInterestPolicy`),
which is precisely the structural property that makes this proposal safe.

---

### F. Break the perfect circle

Perturb the orbit — radius, altitude and along-lap speed — with a small sum of
incommensurate sinusoids keyed off the island id.

**Feel.** This is the loudest remaining "static" tell after the counts. Every manta
school in the world runs one geometrically perfect circle at one constant speed, forever,
and a player watching for thirty seconds can predict the next thirty.

**Provenance.** **This moves TOWARD retail, not away from it**, which is the opposite of
how it first looks. Retail's patrol did not run a circle at constant speed: the waypoint
advanced **only when a creature reached it** (`PatrolVisualiser.CreatureReachedPatrol`),
the creature chasing it was a PID-steered rigidbody inside a five-rule boid group, and
when the path to the waypoint was occluded the patroller **delegated to Wandering and
b-line scanned up and down in 10 m steps** until a clear path existed
(`PatrollingConductVisualiser`). The resulting path is irregular by construction. The
constant-speed circle is the WAREBORN simplification, and `IslandFaunaMovement` already
says so ("CONSTANT SPEED, NOT CONSTANT LAP TIME... Retail advanced the patrol target
when the creature REACHED it"). So: **the irregularity is RECOVERED in character; its
specific shape is WAREBORN TUNING.**

One correction to record while here. A separate reading of `PatrolVisualiser` describes
`sin(orbitDegrees * pi/180 * 0.25) * extents.y` as "a lazy helix with a period of four
orbits". That is what the expression would do *without the wrap* — but
`CreatureReachedPatrol` explicitly wraps `orbitDegrees` back into [0,360], so the sine's
argument never exceeds pi/2 and the band is [midpoint, top], exactly as
`findings-island-fauna.md`'s CORRECTION states and as `MantaVerticalOffsetRatioAt`
implements. The four-orbit helix reading is wrong, and it is wrong in a way that would
have re-introduced the below-the-island bug.

**Wire.** Zero. **Client.** Zero. This is the best cost-to-effect ratio in the plan
after A and B.

**Closed-form.** **Fully preserved** if built from sinusoids of elapsed time. It must be
`C^1` continuous — `IslandFaunaMovement` already enforces continuity with its own test,
for the recorded reason that on the wire a teleport is indistinguishable from a despawn.
Mirror in JS, re-assert parity.

**Size.** **S-M.** The maths is small; the JS mirror and parity test are the bulk.

**Risk.** **Low.** Watch two things: the perturbation must not push a creature inside the
island's rock (derive it as a fraction of the envelope, never absolute metres, as the
file already requires), and it must not raise the effective speed enough to make the
4 Hz pose stream visibly step.

---

### G. Add the beetle — the third species retail had

**Feel.** Almost certainly the single biggest "alive" win available, for the reason
stated in the brief: a new species costs nothing extra per creature. And the beetle is
qualitatively different from what we serve — mantas orbit *outside* the island and
jellies hang *under* it, so a player standing on an island currently has no wildlife
anywhere near them. A beetle is *on* the island.

**Provenance.** **RECOVERED that it exists and can be drawn.** `Beetle` has a container
key and a GameObject; `BeetleAnimationClient`, `BeetleVariantClient`,
`BeetleMaterialExpressionClient`, `BeetleAging`, `BeetleOfflineController` and
`BeetleAnimationControlledColliderStates` all survive; `resources.assets` carries three
heads matching `BeetleVariantType { Grass, Moth, Crab }` with per-age masks, and over
twenty animation clips. `BeetleOfflineController` even hardcodes a plausible rigidbody
(`mass 6, drag 0.8, angularDrag 0.8`) — the best surviving hint at real creature physics,
since the prefab values are lost.

Its *movement* is partly recovered too: `WanderingConductVisualiser` is fully readable —
8 m/s, Perlin-rotated heading, and a **leash** that steers back to the island centre
whenever the creature leaves the island's lateral bounds, checked every 2 s. And the
beetle patrolled: `HabitatPatrolState` keys orbit degrees by species with Beetle as a
first-class entry.

Counts, as always, are WAREBORN TUNING.

**Wire.** Same as any creature. Adding a beetle group to every island raises per-island
population, so it lands on the same budget interaction as D and E.

**Client.** Same per-entity cost as a manta — but a beetle wandering on the island is
in view far more of the time than a manta orbiting at half-diagonal + 10 m, so its GPU
cost is paid more often. That is the point of it, and it is also the honest cost.

**Closed-form.** **Achievable but it is the proposal where it costs the most.** A
faithful wander is Perlin noise over accumulated time with a leash — an integrator. A
closed-form stand-in (a bounded low-frequency drift inside the island's lateral bounds,
at a walkable altitude) is straightforward and keeps every property, but it is a
reconstruction of the *look* rather than of the rule, in exactly the way
`IslandFaunaSchool` is a reconstruction of the boid rules. That should be stated in the
code the way that file states it.

**Size.** **M.** New enum member and prefab mapping; a wander law plus its tests; 4325
`BeetleVariantState` and 1177 `GenderState` (see A — the beetle has the *same* variant
client shape, so it will throw the same storm if under-served); the map model, the JS
mirror and the parity test.

**Risk.** **Medium, and it is a known risk rather than an unknown one.**
`CreaturePreprocessor` installs a long `[Require]`d component list on every non-basic
creature — `CreatureHealthReaderClient`, `AgeVisualizer`, `GenderVisualiser`,
`ConductListener`, `MortalityClientReader`, `MeatSourceBehaviour`,
`DeteriorateVisualizerClient` and more. The manta already tells us what happens when one
of those is under-served. **A should be done first precisely so the beetle can be built
against a proven method for verifying component sufficiency.**

---

### H. Behaviour that changes over time

Drive `ConductPickerState` (1154) `activeConduct` from a slow, deterministic phase clock,
so a group visibly feeds, rests, drifts and regroups.

**Feel.** The deepest change available. A creature that does one thing forever is a
machine; a school that gathers, disperses, settles and moves on is a place. And retail's
animation vocabulary for this is already on disk: `beetle_feeding_{into,loop,outof}`,
`beetle_resting_onground`, `MantaRay_glide`, `_flying_fast`, `_flying_idle`.

**Provenance.** **The vocabulary is RECOVERED; the decision rule is LOST.**
`ConductType { None, Feeding, Wandering, Fighting, EggLaying, Resting, Mating, Flocking,
Attacking, Sharking, FleeingOffender, FleeingThreat, PursuingMate, Patrolling }` is the
complete enum, and every conduct's *execution* survives in the client with its constants
(the recovered table runs to about eighty literals). What does **not** survive is the
conduct **picker** — the GSim logic that chose which conduct was active, and every input
it used (hunger accumulation, tiredness curves, libido, stimulus weights). So a
time-driven picker is WAREBORN TUNING wearing a recovered vocabulary.

Two useful specifics for a reconstruction: retail's tiredness was accumulated *applied
force* reported every 4 s (`TirednessVisualiser._syncDelay = 4f`), and a resting creature
that gets knocked above 0.5 m/s abandons its spot and re-lands
(`RestingConductVisualiser`). Both are cheap to evoke.

**Wire.** Small but **new in kind**: a conduct change is one component update, event
driven, at phase boundaries — minutes apart, not a stream. It does not touch the pose
rate. It is, however, a new relayed sender, and the standing multiplayer-safety rule
applies to it on its own terms.

**Client.** Approximately zero, and arguably negative: an idling or resting creature
plays cheaper animation than a constantly-flying one.

**Closed-form.** **Preserved for the pose, with a caveat.** A conduct that is a pure
function of time keeps the motion closed-form — the jelly's day/night blend is already
exactly this shape. But the *component send* is an event, so the admin map would need the
phase schedule in the roster to draw it, and a creature's conduct would become a second
thing that must agree between server and browser.

**Size.** **M-L.** The picker is small; making the client actually *play* the conduct is
the work, and it may need more of the `[Require]`d closure than we currently send.

**Risk.** **Medium-high.** This is the first proposal where the client's own state
machines get driven rather than merely fed a transform, and `ConductListener`'s
delegation mechanism plus every conduct's stuck/obstacle hooks are code we would be
waking up without the server half that was written to feed it.

---

### I. Reaction to players and ships

**Feel.** The strongest possible answer to "not real" — a manta that circles your ship is
a different game from a manta that ignores it.

**Provenance.** **Astonishingly well recovered, and better than expected.** Retail had
*five* separate reaction channels, all readable:

- **Sharking** — a creature orbits a player **ship**, in a band from the ship's own
  bounds out to bounds + 30 m (`GOLDYLOCKS_WIDTH = 30f`), **matching the ship's
  velocity** and adding a tangential component, banked belly-toward the hull
  (`OverrideUpDirection(-vectorToTarget)`), line of sight re-checked every second.
  The whole three-zone rule is quoted verbatim in the recovery.
- **Proximity agro** — acquire a player inside **3 m**, hold until they leave **12 m**
  (`AgroPlayerTrigger`), feeding `AgroPlayerFSIMState.target` into the Fighting conduct.
- **Offenders** — whoever damages you, sorted by damage dealt, **with ally propagation**
  (`OffenderRecord.allyEntIds`, `OffendersState.fleeingAllies`): a herd alarm system.
- **Sound irritation** — `HearingSenseState.audibleSounds` and `IrritationState`
  accumulate per-source intensity and elect a `winningIrritantEntId`, which gates the
  Attacking conduct. Ships were meant to annoy wildlife by being loud.
- **Impact damage** — ramming a creature damages it above an impulse of 10, and
  explicitly does **not** anger it (`trigger_anger: false`).

Plus the one that already has both halves: **jelly contact shock**, a trigger event to
the server and a `ShockedEvent(entityId, duration)` back, which knocks the local player
out. The `duration` is server data and is lost.

**Wire.** This is where honesty matters. Reactions require the server to know where
players and ships are relative to creatures — it already does — but they also make the
pose stop being predictable, which means **the pose stream can no longer be dropped when
nothing is watching**, and a reacting creature would want a higher pose rate than 4 Hz to
read as responsive. Retail knew this: `FightingConductVisualizer` explicitly calls
`EnterVariableUpdateTransformState("highrate")` on activate and `"lowrate"` on
deactivate. Copying that idea is the right answer, and it is also precisely the kind of
rate escalation the standing multiplayer-safety rule exists to catch.

**Client.** Unchanged entity count; more visible motion.

**Closed-form.** **This is the one proposal that breaks it, and the cost is
architectural rather than computational.** A reacting creature has memory: a trigger
time, a trigger position, a target. Consequences, priced honestly:

1. the admin console's live fauna layer stops being able to draw reacting creatures at
   all, because the browser cannot evaluate a function of a player's history. Either
   reacting creatures are drawn from a periodic position sample (which the console
   deliberately rejected as bandwidth-for-teleports), or they are drawn as "reacting"
   without a position;
2. restart reproducibility is lost for any creature mid-reaction;
3. `AdminFaunaParityTests`' 1e-9 guarantee stops covering the whole population.

**A middle path that keeps most of it:** treat reaction as a *bounded, decaying
displacement added to the closed-form home trajectory*, parameterised by
`(trigger time, trigger position)`. The home path stays closed-form and evaluable; the
console can draw the home path and mark the creature as reacting; and only two extra
numbers per reacting creature ever need to exist. That is a real design and it is not
free, but it is far cheaper than abandoning the closed form.

**Size.** **L**, in any version. **Risk. High** — it is the only proposal that touches
the property everything else in this feature is built on.

---

### J. Let some islands be quiet, and some be empty

**Feel.** Uniformity reads as generated even when the numbers vary. If every island has
*something*, "how much wildlife is here" becomes a dial rather than a fact about the
place. A few empty islands make the busy ones mean something.

**Provenance.** **WAREBORN TUNING, with a recovered gesture toward it.**
`PopulationManagementState`'s only field is
`Map<SpeciesType,long> timeSinceSpeciesPopulationCriticallyLow` — retail explicitly
modelled a species population *going critically low* on a habitat, and `LibidoState`
carries a global `shouldCeaseBreeding` brake. Populations were not uniform and were not
guaranteed non-zero. The thresholds are lost.

**Wire.** Negative — fewer creatures. **Client.** Negative. **Closed-form.** Preserved.

**Size.** **XS**, folded into D. **Risk. Low**, with one caveat: an empty island must be
*legibly* empty rather than looking like a bug, and the admin map should show it as a
deliberate zero rather than as missing data.

---

### K. Phase 0 enablers (not features, but nothing above ships without them)

**K1. Make the soak gate see fauna.** Today `run-soak.sh` reports `fauna: 0 creature
checkout(s)` in every run because a bot never gets island terrain and
`IslandFaunaService` gates AddEntity on `_terrainReady` (1.6). Until a bot can be shown
a creature, **the deploy gate cannot measure any of this**, and every proposal that
raises per-peer counts ships on the strength of unit tests alone. Size **S-M**; risk low;
it is the standing multiplayer-safety rule's own requirement.

**K2. Measure the pose update's real byte size.** 60-100 B is an estimate (1.2). The
`[relay-stats]` counters can turn it into a number. Size **XS**.

**K3. Fix or budget the arrival pipeline.** 0.24 s per creature (1.3) is what actually
limits island population. Options: shorten `SendInterval` for fauna specifically, batch
the AssetLoadRequest step across a school, or accept it and cap populations near 24.
Size **S**; this decision gates how far D, E and G can go.

**K4. Re-measure the client per-entity cost with fauna specifically.** The regression in
1.4 cannot separate a creature from a tree. The mod's own `beat` line already prints
`fps` and `ents`; a session that parks on a busy island and a sparse one gives the
creature-specific slope with no new code and no synthetic input.

---

## 4. Ranking

Ranked by impact per unit of cost and risk. "Impact" is how much closer it gets to *this
place feels inhabited*.

| # | proposal | impact | wire | client | closed-form | size | risk |
|---|---|---|---|---|---|---|---|
| 1 | **A** — manta variant + gender | high (and fixes a bug) | none | **negative** | kept | S | low |
| 2 | **B** — day length 600 s | medium | none | none | kept | XS | very low |
| 3 | **C** — four jelly prefabs | high | none | none | kept | XS-S | low-med |
| 4 | **F** — break the perfect circle | high | none | none | kept | S-M | low |
| 5 | **D** + **J** — size-driven, sometimes empty | **highest** | budget interaction | proportional | kept | S | med |
| 6 | **E** — multiple groups, layered | high | budget interaction | proportional | kept | S-M | low |
| 7 | **G** — the beetle | **highest** | budget interaction | proportional | kept (reconstructed) | M | med |
| 8 | **H** — conduct over time | very high | new event sender | ~none | kept, with a caveat | M-L | med-high |
| 9 | **I** — reaction to players/ships | very high | **new rate class** | none | **broken** | L | high |

`K1`-`K4` are not ranked because they are prerequisites, not features.

**Note the shape of that table.** The four highest-impact proposals — C, F, D, E — are
all free on the wire and free on the client, and all keep the closed form. The reason
this feature feels static is not that it is out of budget. It is that the budget was
spent on one shape and never on variety.

---

## 5. Recommended phased plan

Each phase is independently shippable and independently valuable, so the choice can stop
at any phase boundary.

### Phase 0 — prove the ground (K1, K2, A's verification)

1. **Verify A's premise on one manta** before building anything: send 4326 and 1177 to a
   single creature and confirm the NRE storm stops. Ten minutes, and it decides whether
   A is an S or an asset-extraction job.
2. **K1**, make the soak gate see fauna. Nothing that raises per-peer counts should ship
   before this.
3. **K2**, measure the real pose byte size.

**Deliverable:** a gate that can measure this feature, and a known answer on A.

### Phase 1 — free wins (A, B, C)

Everything here is zero wire, zero-or-negative client, closed-form preserved, and every
one of them is a **recovery** rather than a tuning choice.

- **A** — mantas get per-biome tails and genders; the client stops throwing 3.6
  exceptions a frame and writing 140 MB of log.
- **B** — the day halves to its recovered 600 s.
- **C** — jellies become four species instead of one.

**What changes for a player:** the wildlife stops looking like two repeated assets, and
the world visibly changes state twice as often. **Nothing else in the plan needs to
happen for this to be worth doing**, and it is the phase with the best evidence behind
every line of it.

### Phase 2 — variety in the same budget (F, D, J, E)

Still zero marginal wire and client cost per creature; the only new pressure is on the
per-peer budget, and Phase 0 will have made that measurable.

- **F** first, because it is free and changes every island at once.
- **D + J** — populations scale with the island's own half-diagonal, clamped to the
  per-peer budget, and a deliberate minority of islands come out sparse or empty.
- **E** — more than one group per island, at genuinely different radii and altitudes,
  which is where "layers rather than a single ring" actually lands.

**Decision needed before D/E ship:** K3. If arrival stays at 0.24 s per creature, cap
per-island populations near 24; if the arrival pipeline is shortened or batched, they can
go higher. This is the real constraint and it should be chosen explicitly rather than
discovered.

### Phase 3 — a third species (G)

The beetle. Highest single "alive" return of anything left, and the first wildlife a
player standing on an island will see near them. Built against the component-sufficiency
method Phase 1 establishes, because it will hit exactly the same variant/gender trap the
manta did.

### Phase 4 — behaviour (H), and only then reaction (I)

**H** before **I**, deliberately. H buys most of the perceived aliveness — creatures that
feed, rest and regroup — while keeping the closed form, keeping the admin map, and
keeping restart reproducibility. **I** is the only proposal in this document that spends
the architecture, and it should be spent knowingly, after H has shown how much of "alive"
can be bought without it.

If **I** is wanted, build the middle path from proposal I: a bounded decaying
displacement on top of a closed-form home path, so the console keeps something to draw
and the parity test keeps most of its coverage. And copy retail's own answer to the rate
problem — `"highrate"` while reacting, `"lowrate"` otherwise — under the soak gate that
Phase 0 built.

---

## 6. What I could not determine

Stated plainly, because the temptation with this feature is always to fill gaps with
plausible numbers.

- **Every population count in retail. Still, and now exhaustively.** GSim owned it,
  GSim is not preserved, and the decompile ships **no ecology data file at all** — a
  `find` across all 8,501 files returns five non-`.cs` files: three `.csproj`,
  `component-map.tsv` (a bare id<->name index) and `ecs_config.json` (880 bytes of
  view/weather plumbing). `PopulationManagementState` holds a timestamp map with no
  threshold; `HabitatState.inhabitants` is an observed census, not a capacity;
  `BasicCreatureSpawnerState` has an inhabitants map and a boolean. Every count in this
  plan is WAREBORN TUNING and must stay labelled as such.
- **Which jelly species lived where.** The four names and four prefabs are recovered;
  the eligibility table is not. `DesertA/B` against Biome4 is a name-based inference and
  nothing more. And `FlowerPodFireJelly` has a prefab, a container key and **no enum
  member at all** — I could not determine whether it was a late addition driven by
  something other than `BasicSpeciesType`, or a cut variant whose asset survived.
- **The biome->tail and biome->head tables.** `MantaRayVariantClient._variantsSettings`
  and its beetle equivalent are `[SerializeField]` arrays living in the Unity prefab
  binaries, which the decompile does not contain. Four tail meshes and three heads exist
  in `resources.assets`; which belongs to which biome is extractable from
  `~/Games/WorldsAdrift/Assets/unity/` but was not extracted here. **Proposal A does not
  need it** — any valid biome value populates `MyVariantSettings` — but a faithful
  biome->tail mapping does.
- **Whether `_variantsSettings` is populated at all in the shipped prefab.** If it is
  empty, A does not fix the storm. This is the single highest-value unknown in the
  document and it is a ten-minute experiment (Phase 0).
- **The conduct picker.** Every conduct's *execution* survives with about eighty
  recovered literals; the GSim logic that chose the active conduct, and every input it
  consumed — hunger accumulation, tiredness curves, libido thresholds, stimulus weights,
  `StimuliState.iDValueMap`'s very key names — is gone.
- **Migration triggers and target selection.** `HabitatState.outgoingFlocks`,
  `incomingFlocks`, `flockRequestsPendingFlags` and `lastFlockArrivalTime` are recovered
  as a schema; what wrote them is not. Migration also has **no duration** — the flock
  attractor is rubber-banded to whichever member is catching up
  (`FlockVisualiser`), so travel time is emergent from flight speed. The 20 m raise loop
  is fully recovered and is a *two*-stage clearance test: open sky directly above (cast
  down from 9999 m, require zero hits) **and** a clear corridor for the first half of the
  journey.
- **Almost every per-prefab tuning value**, because they were `[SerializeField]`: every
  PID setting, `separationComfortRadius`, `turnBankingScale`, the jelly's three
  `AnimationCurve`s (which *are* the jelly's motion), age scales, mass ranges, striking
  distances, latch DPS, shock duration, `threatDetectionRadius`, salvage yields. The two
  offline demo controllers hardcode `mass 6, drag 0.8, angularDrag 0.8` for both manta
  and beetle, and that is the best surviving hint at real creature physics anywhere.
- **Retail's live day length.** 600 s is the client's compiled-in fallback
  (`_timeRate = 144f`), but `WorldStateVisualizer` overwrites it from
  `WorldData.timeRate`, which was server data and is gone. Proposal B adopts the client
  default knowingly; it is not proof of the live value.
- **The exact pose byte size on the wire.** Estimated at 60-100 B (1.2); measurable, not
  measured (K2).
- **The creature-specific client frame cost.** The regression in 1.4 bounds *any*
  entity at under 5.5 microseconds on a pinned client, but it cannot separate an animated
  skinned manta from a static tree, and it only covers 129-177 entities (K4).
- **What an unpinned client costs.** The measurement in 1.4 assumes
  `pin-unity-workers.sh`. Without it the slope was ~0.15 ms/entity — **27x worse** — and
  it is not known how many players run pinned.
- **Whether the retail client's flock readers would add anything visible.** No 1199
  `FlockState` and no 1197 `InhabitantState` are put on the wire, so the client is never
  told that a group of creatures is a group. `FlockClientVisualiser` exists and is
  untested against this server.
- **The uptime frame-time drift** found while measuring (1.4): +0.00095 ms per second of
  uptime at constant entity count, 4.75 sigma, which is roughly 102 fps decaying toward
  75 fps over an hour. Not a fauna problem, not investigated here, and someone should
  look at it.

### One correction to existing docs, found while researching

`docs/admin-map-preview.html:817-822` labels Biome3 as `terrain:'ice'`. The decompile
disagrees: `acs/AmbienceSoundController.cs:113-123` maps `Biome3Island` to
`Play_AmbientMain_Jungle` with `BiomeMusicState[Biome3] = "Forest_ALT"`, and
`acs/MusicPlayer.cs:118-128` agrees. Biome3 is jungle/forest. Small, but it is a claim
about the preserved world and it is wrong.
