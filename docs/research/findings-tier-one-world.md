# Tier 1 (Wilderness): the complete A2/A3/B2/B3 region

Status: the terrain, deposit and databank population for the whole Tier-1
Wilderness is **already implemented and proved headless**. This document records
what was measured, the one gap that was closed, and the three things that are
explicitly deferred with their cost.

## 1. The resource answer

**Deposits and databanks already work on release-world islands.** Enabling Tier 1
is a configuration change, not a feature.

The `docs/HANDOVER.md` sentence "no new dynamic resource population is enabled by
terrain registration alone" and the `ReleaseWorldCatalogTests` assertion that a
full rollout registers 354 deposits are **both true and describe different
flags**:

| Flag | What it registers |
| --- | --- |
| `WAREBORN_FIRST_REGION_TERRAIN_COUNT=0..12` | terrain roots ONLY, from `IslandCatalog.FirstRegionTerrain`. No resources. This is the sentence in the handover. |
| `WAREBORN_RELEASE_WORLD_DISTRICTS=<cells>` | terrain **plus** every catalogued deposit and databank for the selected cells, via the `foreach (ReleaseIslandRecord island in releaseIslands)` block in `WorldEntities.Default`. This is the 354/1233 assertion. |

The chain for a release-world deposit is complete end to end:

1. `WorldEntities.Default` registers `DepositEntity(deposit)` for every selected
   island (`WorldEntities.cs`, the "Complete release-world population" block).
2. `ResourceInterestService`'s constructor binds an entity id for every streamed
   resource key before any peer connects.
3. `WorldResourceActivation.Activate` resolves the deposit through
   `MetalDeposits.ByKey`, whose FIRST lookup is
   `ReleaseWorldResources.DepositByKey`, then calls `_nodes.Register` +
   `HarvestReward.Register` + `_metal.Place(id, YieldUnits, ShotsToDeplete)`.
   That is authoritative harvest state, established at boot and independent of
   whether any peer has the entity checked out.
4. Databanks take the same route: `DatabankEntity(key, position)` carries
   `Databanks.AssetName`, and activation calls
   `DatabankLedger.Register(entityId, Databanks.GrantAmount, ...)` - the same
   ledger the Trades Challenge run proved at 4 x 10,000 knowledge.

This is exactly the shape the handover's "do not equate 'the prefab renders'
with 'the resource is authoritative'" warning demands: activation happens once
at boot from the world registry, and spatial interest only ever decides
visibility afterwards.

**Proved headless.** A throwaway boot with
`WAREBORN_RELEASE_WORLD_DISTRICTS=A2,A3,B2,B3` printed:

```
[world-resource] activated 387 boot resource entities independently of per-peer visibility.
[release-world] LOCAL TEST enabled: selectors='A2,A3,B2,B3', terrains=47, regions=5.
[world-directory] classified 435 registrations: global=1, region=434
                  (haven-region=127, release-a2-region=89, release-a3-region=65,
                   release-b2-region=64, release-b3-region=89)
[domain-host] local-single-process islands=47 ships=0 owned=434 globals=1 unowned=0 duplicates=0
[terrain-interest] ON: optional island terrain uses 4000 m load / 4400 m unload hysteresis
```

(That is the boot BEFORE this change. The final boot, with the named `tier1`
selector and the atlas shards of section 4, is in section 3.)

Of the 387 activations, **46 were `deposit-release-*` and 215 were
`databank-release-*`** - the complete Tier-1 population, every one of them. Zero
warnings and zero errors outside the four "persistence is OFF" lines a
`WAREBORN_DB`-less throwaway instance always prints.

## 2. Scope, verified against the catalogue

`release-runtime-catalog.json` was re-counted directly:

- The world has 254 ordinary islands; **exactly 46 are tier 1**, and they sit in
  exactly four map cells: A2 (11), A3 (12), B2 (11), B3 (12).
- Those four cells contain **nothing but tier-1 islands** (`cellTier == tier == 1`
  for all 46). `A2,A3,B2,B3` therefore selects precisely the Wilderness - the
  brief's numbers are confirmed.
- Content: **46 PvE deposits, 215 databanks, 12 islands with revival chambers,
  14 islands with tree species**, 16-point shell outline on all 46.

### The distribution is the important part

The 46 deposits are **not one per island**. They are concentrated on four:

| Island | Deposits | Surveyed PvE metals |
| --- | ---: | --- |
| Crimson Paradise | 23 | Iron q3 |
| Mount Spero | 14 | Bronze q3, Iron q4, Lead q3 |
| Breeze Isles | 6 | Bronze q2, Copper q4, Epilar q4, Iron q3, Tin q2 |
| Comm Strip | 3 | Iron q1, Lead q2 |

**SUPERSEDED 2026-08-18.** The table above and the paragraph below describe the
state until then: 46 deposits on four islands, because the generator computed
`deposit_count = ceil(cells * 0.05) if metals else 0` and only 38 of 254 islands
had a surveyed PvE metal table.

That guard is gone. The empty tables turned out to be a coverage gap in a
player-submitted community survey rather than a barren world - the survey visited
all 254 islands, its own UI reads an empty list as "No metals data", and retail's
island spawner component carries a `minMetalRockDeposits` floor. Tier 1 is now
**328 deposits, every island populated**: 4 from their own survey and 42 stamped
`inferred-tier`. The density rule itself did not change.

See `docs/research/findings-island-resource-population.md` for the full evidence,
the derived rule and the measured load. One correction it makes to this document:
the 0.05-per-cell density is **not** "the recovered retail figure". The decompile
has the field names and confirms the island reports its LOD0 mesh count to the
spawner, but the formula lived in the lost Scala worker. The shape is retail; the
value is ours.

Databanks are the opposite: every one of the 46 islands has 3-5, and all 215 are
placed on measured surface samples.

So a player who travels to Tier 1 today finds, on **every** island, real terrain
with collision, 3-5 scannable databanks and a metal mining loop. The doctrine
that produced the earlier four-island state ("an empty metal table remains
explicitly empty; it is not permission to invent a generic population") is
honoured differently rather than abandoned: the metals are derived from the
surveyed cohort, never invented freely, and every island whose table is derived
carries `metalSource: inferred-tier` in the catalogue and
`IslandSurveyProfile.MetalsAreInferred` at runtime.

## 3. Measured costs at 47 terrains

Final headless boot, `WAREBORN_RELEASE_WORLD_DISTRICTS=tier1` plus the required
interest/terrain env, against a throwaway data directory on UDP 17812:

```
[release-world] LOCAL TEST enabled: selectors='tier1', terrains=47, regions=5.
[world-directory] classified 481 registrations: global=1, region=480
                  (haven-region=127, release-a2-region=112, release-a3-region=65,
                   release-b2-region=64, release-b3-region=112), ship=0 across 0 hull root(s).
[domain-host] local-single-process islands=47 ships=0 owned=480 globals=1 unowned=0 duplicates=0
[terrain-interest] ON: optional island terrain uses 4000 m load / 4400 m unload hysteresis
[island-shell] distant non-physical island visuals: ON; fidelity=compact outline
[world-resource] activated 433 boot resource entities independently of per-peer visibility.
```

| Measurement | Value |
| --- | --- |
| Terrains registered | **47** (46 tier-1 + Haven) |
| Regions | **5** (Haven + release-a2/a3/b2/b3) |
| World registrations classified | **481** (global=1, region=480, unclassified=0) |
| Domain-host ownership | `islands=47 ships=0 owned=480 globals=1 unowned=0 duplicates=0` |
| Boot resource activations | **433** = 215 release databanks + 46 release deposits + 46 release atlas shards + 81 Haven trees + 24 fuel pods + 21 Haven metal nodes |
| Spawn plan length | **964 steps** (872 before the shards) |
| Load-barrier initial set | **22 keys** - Haven island + 6 near trees + 9 near metal nodes + global + 3 fuel pods. **No release island and no release resource is in it.** |
| Warnings / errors | **none** beyond the four "persistence is OFF" lines every `WAREBORN_DB`-less throwaway instance prints |
| Terrains physically loaded at the production 4000 m radius | min 5, **median 9**, max 12 (measured over all 46 islands as viewpoints) |
| Terrains physically loaded at the 1200 m default | min 1, median 1, max 3 |
| Nearest tier-1 island to the Haven spawn | **9.33 km** |

**The connect plan does not balloon.** The 872-step plan is a process-wide list
of `SyncStep` records built once, not per peer; per-peer cost is what the
connect gate actually sends. Every release terrain root is `AfterPlayer` and
therefore `IslandTerrainConnectPolicy.IsManaged`, and every release deposit and
databank is a streamed resource key and therefore
`ConnectInterestPolicy.IsGateable`. Both are fast-forwarded in a single turn,
sending nothing, when out of range. Because the nearest tier-1 island is 9.33 km
from the Haven spawn and the production terrain radius is 4 km, a fresh Haven
connect streams **zero of the 46 tier-1 terrains and zero of the 307 tier-1
resources** - it walks the same Haven-only chain it walks today.
`ReleaseWorldConnectCostTests` pins this.

The three radii stay separate, as the handover requires: connect
(`WAREBORN_INTEREST_INITIAL_RADIUS_M`, default 45 m), live resource
(`WAREBORN_INTEREST_RADIUS_M`, 120 m in production) and terrain
(`WAREBORN_TERRAIN_LOAD_RADIUS_M` / unload, 4000/4400 m). Nothing here widens
resource interest; the settle window is untouched.

## 4. What is implemented in this change

Everything below is additive and only fires when the release-world rollout is
already active.

1. **Named tier selectors.** `WAREBORN_RELEASE_WORLD_DISTRICTS=tier1` (aliases
   `t1`, `wilderness`) now resolves from the catalogue's own `cellTier`, so the
   Wilderness cannot drift if a future catalogue regeneration moves an island
   between cells. `tier1` is asserted to be identical to `A2,A3,B2,B3` and to
   contain exactly 46 islands, all with `Survey.Tier == 1`. `tier2`..`tier4`
   exist for symmetry. Exact cell ids keep working unchanged, and the selectors
   compose (`tier1,C6`).

2. **Atlas shards on release-world deposits.** This was the one real gap. Haven
   deposits and Trades Challenge deposits each register an `AtlasShardEntity`;
   release-world deposits registered none, so mining a Tier-1 deposit yielded
   metal but could never yield the Atlas shard that is the loop's payoff. Each
   release deposit now registers its shard immediately after itself (the order
   `AtlasShardEntity` requires, so the host's id is bound when the shard's spawn
   step resolves it), gated by the existing `WAREBORN_SPAWN_ATLAS` flag and
   `AtlasSpawnPolicy` rate so `WAREBORN_ATLAS_RATE` tunes it exactly as it tunes
   Haven. Per island the rate is applied to that island's own deposit index, so
   index 0 of each island always carries one. Cost at Tier 1: **+46 entities**
   (354 world-wide under `all`), all `AfterPlayer` and all gated by the same
   120 m resource interest as their hosts.

3. **A pure `ReleaseWorldPopulationPolicy`** in the engine-free Multiplayer
   project that answers, from the catalogue alone, how many terrains, deposits,
   databanks and shards a given selector produces, and which islands have no
   metal. The tests and the boot banner assert against it rather than
   re-deriving counts by hand.

4. **Tests** (`ReleaseWorldTierSelectionTests`, `ReleaseWorldConnectCostTests`
   and extensions to `ReleaseWorldCatalogTests`): the tier-1 selection is
   exactly 46 tier-1 islands; every tier-1 deposit, databank and shard is
   registered exactly once with no duplicate entity keys; the world registry,
   island registry and region registry agree at tier-1 scale with zero unowned
   and zero duplicate entities; every shard's host deposit is registered before
   it; and a fresh Haven connect executes no tier-1 step.

## 5. Deferred, with reasons and costs

### Trees - DEFERRED

14 of the 46 tier-1 islands have a surveyed tree species list (22 species
entries: Oak, Elm, Birch, Chestnut, Palm, Ash). None of them has a tree
**count** or a single tree **coordinate**. The catalogue's `trees` field is a
species list and nothing more.

Deposits and databanks could be placed because both had a number backed by
evidence - deposits from a 0.05-per-cell density (retail's SHAPE, our value -
see findings-island-resource-population.md section 3), databanks
from the survey's exact per-island count. **There is no comparable evidence for
tree density.** Any number chosen would be invented, and this repository has
consistently refused to invent populations.

To implement: (1) agree a density rule and record it as a decided reconstruction
rather than a fact; (2) add a `treePoints` array to the generator using the
existing `spaced()` sampler with the deposits and databanks passed as `occupied`
so nothing overlaps, and regenerate the embedded catalogue; (3) map species to
the eight already-verified per-species prefabs through the existing
`varyTreeSpecies` path; (4) register and activate through the existing
`TreeHarvest.Plant` route, which needs no change; (5) a visual acceptance pass,
because tree prefabs sit on terrain and a bad Y is immediately obvious. Roughly
a day, gated on a density decision that is the user's to make. Haven alone
carries 80 trees, so expect four figures across 14 islands - worth pacing behind
its own flag.

### Revival chambers - DEFERRED

The survey records revival-chamber presence for 12 of the 46 islands. Nothing
else about them survives: no coordinate, no prefab key in `prefab-keys.txt`, and
**no server system of any kind**. There is no revival-chamber entity, no
interaction handler and no respawn-anchor concept - `SpawnPolicy` only mentions
watching for the `RevivalChamberInterface` knowledge node. Spawning one would be
a prop with no behaviour.

To implement: a world entity + prefab identification, an 8055-style interaction
to bind a player's respawn anchor, a persisted anchor field per character, and a
respawn path that honours it instead of the fixed Haven spawn. That is a feature
in its own right (crew graduation from Haven leans on it), not a Tier-1 rollout
task.

### Metal on the 42 unsurveyed islands - DONE 2026-08-18

The decision recorded here was taken: an empty Cardinal table means "the
community never recorded it". The `if metals` guard was dropped, unsurveyed
islands take a tier-derived table explicitly labelled `inferred-tier`, and the
constants moved 354 -> 1930 world-wide and 46 -> 328 in Tier 1 (the estimate of
"roughly 900" here was for Tier 1 and was high; the real figure is 328).

Evidence, derivation and cost: `docs/research/findings-island-resource-population.md`.

### Distant island shells stay behind their own flag

`[island-shell] distant non-physical island visuals: OFF` in the boot above:
shells need `WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED=1` as well. That flag is
unchanged here. Without it a Tier-1 player sees nothing beyond the 4 km terrain
radius; with it they get the v2 compact-outline shells, which the handover
records as corrected but **not visually accepted**. The rollout recipe below
lists it separately for that reason.

## 6. Rollout recipe

```
WAREBORN_RELEASE_WORLD_DISTRICTS=tier1      # or the explicit A2,A3,B2,B3
WAREBORN_INTEREST_RADIUS_M=120              # REQUIRED; startup refuses without it
WAREBORN_TERRAIN_INTEREST_ENABLED=1         # REQUIRED; startup refuses without it
WAREBORN_TERRAIN_LOAD_RADIUS_M=4000         # production value; median 9 terrains loaded
WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED=1    # OPTIONAL, not visually accepted
```

Startup fails closed: `ReleaseWorldEnabled` requires both a positive interest
radius and terrain interest, and prints
`[warning] release-world rollout requested but safely disabled` otherwise, so a
half-configured deploy cannot put 47 terrains into the immutable connect plan.

## 7. What still needs a live client

Nothing in this document was proved with a real Unity client. Specifically
outstanding:

- that a player standing on any tier-1 island sees its terrain, walks on its
  collision and sees its databanks (the equivalent Trades Challenge pass
  succeeded, so this is expected to hold, but it is inferred);
- that a tier-1 deposit mines to depletion and drops its Atlas shard;
- that the 46 client bundles load without the memory pressure the joiner-crash
  work was about - the B3 twelve alone are 116.5 MiB compressed, and 46 islands
  is roughly four times that. The 4 km radius means a peer holds a median of 9
  at once, which is the number to watch;
- the two-client soak gate, per the standing multiplayer-safety rule. The change
  here adds no new high-rate or reliably-relayed component - shards and deposits
  are static `AfterPlayer` entities on the existing 120 m checkout - so a
  regression is not expected, but the gate is still owed.
