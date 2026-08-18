# Island fauna: jellyfish and manta rays

Static-analysis report, 2026-08-16. This identifies what survived in the retail
player client and what Wareborn would need to reconstruct. It does not claim
that the original GSim or UnityWorker ecology has been recovered.

## What survived

The client prefab census resolves `JellyFish`, `MantaRay`, `MantaRayEgg` and
`MantaRay_Egg` (`docs/research/loop/data/prefab-names.tsv`; the packaged resolver
list is `WorldsAdriftRebornGameServer.Multiplayer/Ship/client-entity-prefabs.txt`).
The egg prefabs are not aliases: `MantaRayEgg` carries hatch state, whereas
`MantaRay_Egg` is a visual/scan shell whose `MantaRayEggSetter` only randomizes
its material seed (`WAReborn-decompiled/acs/MantaRayEggSetter.cs`).

Retail represented manta rays as `SpeciesType.MantaRay`. Jellyfish used the
separate basic-species values `JellyFishSeed`, `JellyFishFlower`,
`JellyFishDesertA` and `JellyFishDesertB`. Relevant state included Health 1160,
Age 1166, Mortality 1171, AnimationControllerFSIM 1175, Gender 1177, Species
1182, Flock 1199, Habitat 1200, BasicCreatureSpawner 4321, BasicCreature 4322,
contact damage 4323/4324 and MantaRayVariant 4326. The generated definitions are
under `WAReborn-decompiled/gencode/Bossa.Travellers.Creatures*` and the component
ids are indexed by `WAReborn-decompiled/component-map.tsv`.

Manta variants contain a biome and optional Arrowtail/Forktail variant. The
client maps gender to tail selection and biome to materials, but no surviving
player-client table proves retail population counts or exact biome eligibility.
The jelly species names suggest floral/seed and desert families; treating those
names as exact spawn rules would still be inference.

## Where authority lived

Retail ecology was split. GSim owned habitat, flock and population bookkeeping.
UnityWorker owned terrain queries, movement, physics and AI. The player client
received state and ran readers, animation, audio and presentation. Evidence:

- `WAReborn-decompiled/acs/IslandPreprocessor.cs` installs habitat and patrol
  visualizers only for UnityWorker.
- `WAReborn-decompiled/acs/Assets.Scripts.PrefabExporting.Preprocessors/CreaturePreprocessor.cs`
  installs manta movement, feeding, wandering, fighting, mating, flocking,
  patrol and mortality conduct on UnityWorker.
- `WAReborn-decompiled/acs/Assets.Scripts.PrefabExporting.Preprocessors/JellyFishPreprocessor.cs`
  installs jelly movement on UnityWorker while the client receives animation
  and scanner behavior.

Manta patrol selected targets just beyond an island's lateral bounding radius,
used a sinusoidal vertical offset across the island half-height and advanced in
roughly ten-degree orbit steps (`acs/PatrolVisualiser.cs`). Flocks could travel
between habitats and raise their path in 20 m increments until clearance was
available (`acs/Assets.Scripts.Visualisers.Creatures.Flock/FlockVisualiser.cs`).

Jelly movement used the nearest checked-out island bounds. During daytime it
moved laterally away from the island centre and sought the bounds-min altitude
when outside; at night it orbited while inside and returned toward the centre
when outside (`acs/Assets.Scripts.Visualisers.Creatures/JellyFishMovement.cs`).

## Interaction evidence

Jelly contact emitted a worker-side contact event and GSim returned a shocked
event with a duration; the player client knocked out the matching local player.
Mantas had health, damage, mortality and corpse presentation. Dead manta could
become a meat source, and client material/audio data names `mantaRayMeatRaw`.
Wareborn does not yet implement the corresponding creature-material economy.

## Wareborn implementation path

1. Add an opt-in smoke test that streams one jelly and one manta through normal
   island/region interest. Implement the minimum component-reader closure first;
   a prefab-only AddEntity is an inert visual shell, not restored fauna.
2. Add a deterministic `IslandFaunaDomain`: bounded per-island populations,
   analytical jelly day/night paths and manta perimeter orbits. The server owns
   transforms and sends them only to interested peers.
3. Add interactions: jelly shock, manta health/death, corpse state and raw-meat
   salvage. Persist stable fauna state or respawn deterministically.
4. Add tuned habitat caps, respawn, eggs and optional flock migration. Until
   original GSim/template configuration is recovered, label counts as Wareborn
   tuning rather than retail-faithful values.

Fauna belongs to an island simulation domain. Individual creatures should not
become worker/domain boundaries.

## Implementation status

Stages 1 and 2 above are implemented as a pure, engine-free, server-owned core
inside `WorldsAdriftRebornGameServer.Multiplayer`. Stages 3 and 4 are not
started. Six files carry the work; no client mod, game-server, csproj, engine
glue or prefab-manifest file is involved, and the Multiplayer assembly keeps its
zero external references.

- `Islands/IslandFaunaPolicy.cs` - `FaunaSpecies`, the prefab-name mapping onto
  the existing `JellyFish`/`MantaRay` census entries, `FaunaCreature`, the
  opt-in gate, a budget parser and the deterministic per-island population.
- `Islands/IslandFaunaMovement.cs` - closed-form manta orbit and jelly day/night
  drift.
- `Islands/IslandFaunaRegistry.cs` - the clock-driven bounded pose registry.
- One xUnit file per production file under
  `WorldsAdriftRebornGameServer.Multiplayer.Tests/Islands/`.

The decisions worth reading back before extending this:

- OFF BY DEFAULT. `WAREBORN_ISLAND_FAUNA` accepts `1`/`true`/`yes`
  case-insensitively and nothing else, matching
  `IslandTerrainInterestPolicy.EnabledFrom`. Fauna transforms are a new relayed
  sender, so the standing multiplayer-safety rule applies: the feature arrives
  off and is switched on deliberately.
- A DISJOINT ENTITY-ID BAND. Creatures count upwards from `2_100_000_000L`,
  a hundred million clear of `TreeFall.FirstLogEntityId` at `2_000_000_000L`.
  The bands must not overlap: a fauna pose and a falling-log pose naming the
  same entity would corrupt the client's entity table in a way that reads as a
  protocol bug. Like a log, a creature is deliberately not a world registration.
- BOUNDED, AND SLOWER THAN SHIPS. The registry refuses past a world-wide
  concurrent cap rather than throwing, emits complete absolute poses that
  supersede rather than deltas, is silent when nothing is due, and pushes at a
  cadence deliberately below the 20 Hz ship/log rate because fauna drifts rather
  than falls.
- RESTART-REPRODUCIBLE BY CONSTRUCTION. Population is a function of the surveyed
  tier; a pose is a closed-form function of (creature, elapsed seconds). No
  `Random`, no `DateTime`, no accumulated physics state, nothing persisted. A
  rebuilt registry on a fresh clock replays the same ids and the same poses.
- GEOMETRY DERIVED AS RATIOS. Manta lateral radius and vertical half-height come
  from the `IslandTerrainEnvelope` extents rather than from absolute metres, so
  a tiny, huge or anisotropic island rescales instead of putting the creature
  inside the rock; island-local results reach world space through
  `IslandDefinition.LocalToGlobal`.

PROVENANCE, kept explicit because this is where invention is tempting. Every
COUNT is WAREBORN TUNING - GSim owned population bookkeeping and GSim is not
preserved, so no surviving artefact states how many creatures an island carried.
The DIRECTION those counts move in is WIKI-SOURCED, from the
worldsadrift.fandom.com Biome and Creatures pages placing tier 1 Wilderness at
the calm end and tier 4 Badlands at the hostile end. The movement constants are
RECOVERED, from `acs/PatrolVisualiser.cs` for the manta perimeter orbit and
`acs/JellyFishMovement.cs` for the jelly day/night rules. The four retail jelly
basic-species values are collapsed to one `JellyFish` member: the names survived,
the per-island eligibility did not, and four members would be four claims this
project cannot support. Seeding jellyfish at all is an explicit era choice -
they were discontinued late in retail's life, so their presence presents the
earlier world rather than the last one.
