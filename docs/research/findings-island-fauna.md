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

CORRECTION, 2026-08-18. "A sinusoidal vertical offset across the island
half-height" was too loose and this project shipped the wrong reading of it.
`UpdatePatrolTarget` computes

```csharp
Vector3 vector4 = Vector3.up
    * Mathf.Sin(orbitDegrees * ((float)Math.PI / 180f) * 0.25f)
    * islandSurfaceData.BoundsExtents.y;
```

and `CreatureReachedPatrol` WRAPS `orbitDegrees` into [0,360]. The sine's
argument therefore only ever covers [0, PI/2] and the offset only ever covers
[0, +extents.y]: it is NEVER NEGATIVE. Retail's patrol occupies the band from
the island's vertical MIDPOINT to its TOP. The lateral term is likewise exact -
`new Vector2(BoundsExtents.x, BoundsExtents.z).magnitude + 10f`, the horizontal
half-DIAGONAL plus a flat ten metre standoff, not a ratio.

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

## Schooling: what retail did, and what this server does instead

Recovered 2026-08-18 by an exhaustive sweep of the decompiled client.

A retail flock was NOT a formation and NOT one networked transform with
client-side offsets. It was a SEPARATE SpatialOS entity acting as an ATTRACTOR:

- component 1199 `FlockState` carries `females`/`males` member lists, a
  `speciesType`, origin/target habitat ids, a `targetHabitatVector`, a
  `flockPhase` (`None, GettingReady, ReadyForCongregation, InTransit,
  RoamingOrigin`) and member heartbeats. There is NO leader field and NO size
  field (`gencode/Bossa.Travellers.Creatures.Habitat.Flock/FlockStateData.cs`).
- each creature carries component 1197 `InhabitantState`, whose
  `flockEntityPosition` is the flock's replicated world position.
- each creature is a full, independently networked entity that solved its own
  position on the UnityWorker with a five-rule boid steerer
  (`acs/Assets.Scripts.Visualisers.Creatures/MovementController.cs`): cohesion
  1.5, separation 1.5, alignment 1.5, seek-flock-target 15, wander 10. Spacing
  was EMERGENT from separation acting on live neighbour transforms refreshed
  every 10 s - never a formation table.

Two distances survive and are the only anchors on how big a flock is in metres:
a member declares itself ready inside `sqrMagnitude < 100f` (10 m) of the flock
entity (`FlockingConductVisualiser.cs`), and a flock counts itself caught up
inside `Mathf.Pow(15f, 2f)` (15 m) (`FlockVisualiser.cs`).

GROUP SIZE IS NOT RECOVERABLE. `FlockStateData`'s membership is two unbounded
`List<EntityId>`; `BasicCreatureSpawnerState` (4321) carries only an inhabitants
map and a `hasDoneGenesisSpawn` flag; `PopulationManagementState` carries a
"critically low" TIMER and no threshold. Every generated
`Bossa.Travellers.Creatures*` struct, every visualiser and both data files the
decompile ships were searched: no min, max, count, density or capacity for a
flock exists anywhere. Those numbers lived in GSim/FSIM, which is not preserved.
`MovementController.Settings.separationComfortRadius` is declared and used but
never assigned a literal - it was a `[SerializeField]` baked into the Unity
prefab, and the prefab binaries are not in this decompile.

JELLYFISH DID NOT FLOCK, proved three ways. `JellyFishMovement.cs` contains no
flock reference; `JellyFishPreprocessor.cs` installs `BasicMovementController`
and never `FlockingConductVisualiser`, so a jelly did not even carry the boid
solver; and `FlockStateData.speciesType` is a `SpeciesType`
(`None,Tree,Beetle,MantaRay,Stalker`) while a jelly is a `BasicSpeciesType`, so a
jelly could not be typed into a flock. What jellies had instead was DENSITY:
each picked its own `Random.Range(0f, 360f)` heading and `Random.onUnitSphere`
orbit axis and drifted independently about the same island centre. A player
reads a dozen of those as a shoal even though no component says so.

WHAT THIS SERVER DOES. The STRUCTURE is kept - a school is a moving attractor
point, members are clustered around it, and each member is still its own
networked entity, exactly as retail wired it. The boid SOLVER is not, and cannot
be: boids are an integrator carrying velocity and live neighbour transforms,
and this server's restart-reproducible design is that a pose is a closed form of
(creature, seconds). `Islands/IslandFaunaSchool.cs` therefore places members on
a golden-angle cluster with a slow common weave - a WAREBORN reconstruction of
what those boid rules settle into, with the cluster RADIUS anchored between the
two recovered distances above. Group sizes are WAREBORN TUNING and are labelled
as such at every constant.

## What is still unproven

- THE SOAK GATE CANNOT SEE THIS FEATURE. `tools/relaybot/run-soak.sh` was run at
  the full population (3,866 creatures, whole catalogue) and at the production
  tier-1 population (460), with the bots stood inside a real island's envelope
  via the new `--centre` flag. Every run reported VERDICT: FLAT with drift under
  0.1 ms - and every run also reported "fauna: 0 creature checkout(s)". Optional
  island terrain is never checked out to a bot ("[terrain-interest] added ..."
  never appears) at a 1200 m terrain radius or at 12000 m, so the radius is not
  the variable, and a creature is never sent to a peer that does not hold its
  island. So the soak proves that a large fauna POPULATION does not perturb relay
  staleness, and does NOT exercise the fauna checkout or pose path at all. Any
  earlier soak figure quoting a fauna pose rate was measuring something else.
  The per-peer rate bound is therefore held by `IslandFaunaInterestPolicy`'s cap
  and its unit tests, not by the soak.
- THE CLIENT'S OWN FLOCK READERS ARE UNTESTED. Schooling here is geometry only:
  no 1199 `FlockState` and no 1197 `InhabitantState` are put on the wire, so the
  client is never told that a group of creatures is a group. Whether the retail
  `FlockClientVisualiser` would add anything visible is unknown.
- THE MANTA SPEED AND THE DAY/NIGHT CYCLE LENGTH ARE PURE INVENTION. 8 m/s and
  1200 s have no surviving evidence behind them; only the SHAPES they drive do.
- NO IN-GAME VISUAL CONFIRMATION. Nothing here has been looked at with a client.
  The claim that a school is now visible from an island rests on arithmetic
  against the extracted AABBs, not on a screenshot.

## Implementation status

Stages 1 and 2 above are implemented and WIRED. Stages 3 and 4 are not started.
The decision logic is a pure, engine-free core inside
`WorldsAdriftRebornGameServer.Multiplayer` (which keeps its zero external
references); the game-server side is thin glue. No client mod is involved.

- `Islands/IslandFaunaPolicy.cs` - `FaunaSpecies`, the prefab-name mapping onto
  the existing `JellyFish`/`MantaRay` census entries, `FaunaCreature`, the
  opt-in gate, a budget parser and the deterministic per-island population.
- `Islands/IslandFaunaMovement.cs` - closed-form manta orbit and jelly day/night
  drift.
- `Islands/IslandFaunaRegistry.cs` - the clock-driven bounded pose registry.
- `Islands/IslandFaunaPlan.cs` - which creatures a world actually gets once the
  world is bigger than the budget, and the id-stability rule that makes the
  budget safe to tune.
- `Game/IslandFaunaService.cs` (game server) - boot seeding, per-peer checkout
  and the pose push.
- `ComponentsSerializer` - the 190602 live-pose override plus 1182
  `SpeciesState` (rays) and 4322 `BasicCreatureState` (jellies).
- One xUnit file per pure production file under
  `WorldsAdriftRebornGameServer.Multiplayer.Tests/Islands/`.

THE DESPAWN, DIAGNOSED AND FIXED (2026-08-18). The player reported "i have seen
some manta rays here and there but they kinda despawn". Cause: fauna checked out
per CREATURE against its LIVE position at the global resource radius
(`WAREBORN_INTEREST_RADIUS_M`, 120 m in production, 155 m unload). That is right
for a deposit, which never moves, and wrong for an animal. Measured against the
release catalogue's own extracted AABBs with the player standing at each island's
own landing point, the fraction of one manta lap spent inside 120 m was 0% on
THIRTY of the forty-six tier-1 islands and under 30% on all but four. Where it
was non-zero the manta crossed the boundary twice a lap, and each crossing was a
RemoveEntity followed later by a fresh AssetLoadRequest + AddEntity - the
repeated "added MantaRay ... to <same peer>" lines in the production log.

A SECOND, INDEPENDENT CAUSE made it worse: the vertical band was wrong (see the
CORRECTION above). Mantas were flown symmetrically about the island AABB's
MIDPOINT, so half of every lap was spent between the midpoint and the BOTTOM of
the box - the tip of the rock spire. The release catalogue's own landing points
sit at a MEDIAN 0.755 of AABB height, so "the midpoint" is typically fifty to a
hundred and fifty metres under the player's feet. Retail's recovered band,
[midpoint, top], straddles that ground.

THE FIX IS STRUCTURAL, NOT NUMERIC. `Islands/IslandFaunaInterestPolicy.cs` keys
checkout on the ISLAND rather than the animal: a peer holds an island's whole
population while it is within 600 m of that ISLAND'S ENVELOPE (800 m unload).
Standing anywhere on an island gives an envelope distance of zero, so the
population is held for the entire visit and cannot flicker however wide the
orbit is - and no future movement change can reintroduce the churn, because a
creature's position is no longer an input to the decision. Measured against all
254 release islands, a peer standing on any island's landing point has exactly
ONE island inside 600 m, so the radius buys the island you are on and nothing
else.

Two alternatives were measured and rejected. Raising the global
`WAREBORN_INTEREST_RADIUS_M` gates every tree, deposit, databank and shard in the
world, so it would multiply client entity counts on a client just tuned to
120 fps in order to fix a feature with a couple of dozen entities in it. Widening
the existing 35 m hysteresis cannot help: the manta is not dithering across the
boundary, it is crossing it decisively and travelling hundreds of metres past.

THE WIRE BOUND MOVED FROM THE WORLD TO THE PEER, and that is what allowed "more
wildlife". `WAREBORN_ISLAND_FAUNA_MAX` used to be the only bound on what one peer
could be sent, so it was held at 24 against a tier-1 demand of 138 - eight of
forty-six Wilderness islands populated, thirty-eight empty. Admission is now
capped in creatures PER PEER (`WAREBORN_ISLAND_FAUNA_PEER_MAX`, default 24), so
the worst case a peer can receive is 24 x 4 Hz = 96 updates/s NO MATTER how large
the world's population is. The world-wide cap now bounds only how much wildlife
EXISTS - a dictionary entry and a closed-form pose each - and defaults to 4000,
which covers the complete 254-island catalogue (3,866 creatures). Tier 1 seeds
460 creatures across 46 of 46 islands.

Admission is WHOLE-ISLAND and retention-first: an island already held keeps its
place ahead of any newcomer, and a population that does not fit the remaining
budget is skipped rather than half-streamed. Both rules exist to stop the budget
itself becoming a new source of churn - a per-creature cap would make a school
orbiting past the boundary swap members in and out, which is the original bug
rebuilt through the back door. `IslandFaunaPolicy.PopulationFor`'s largest output
(19, on a tier-4 island) is deliberately below the per-peer budget, so a player
standing on an island is never shown a truncated school.

A CREATURE IS NOT A WORLD REGISTRATION, and that is forced rather than chosen.
`IslandFaunaRegistry.Add` refuses any id below the 2,100,000,000 fauna band
while `WorldEntityRegistry` ids come from `EntityIdAllocator` counting up from
1, so the two id schemes are mutually exclusive. A creature therefore cannot
ride `ResourceInterestService`, whose entire input is the registration list, and
a `fauna-` prefix in `ResourceInterestPolicy.IsStreamedResourceKey` would be
dead code because a creature has no registration key to match. `IslandFaunaService`
carries its own per-peer checkout instead, reusing `ResourceInterestPolicy`'s
pure geometry rather than copying it. Measured consequence: a tier 1 headless
boot with fauna on and off produces byte-identical registration, boot-resource
and ownership-audit lines.

ONLY RELEASE-CATALOGUE ISLANDS CAN CARRY FAUNA AT ALL: the population is a
function of the surveyed tier and Haven has none, so a Haven-only world seeds
nothing and says so at boot. This also means the PLAYER SPAWN carries no
wildlife - the Haven spawn is 3.8 km from the nearest island - so the fauna a
player sees is the fauna of whatever island they travel to.

WHAT IS ACTUALLY AUTHORITATIVE. The server owns each creature's transform and
its species, and nothing else. There is no health, mortality, age, gender,
habitat, contact damage or manta variant - a creature cannot be hurt, cannot
hurt a player, and cannot die. That is stage 3. Schooling is present as GEOMETRY
only: no 1199 `FlockState` and no 1197 `InhabitantState` are ever put on the
wire, so a school is a group of creatures that MOVE together, not a group the
client knows about. Whether the retail client's flock readers would add anything
visible on top of that is untested.

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
- GEOMETRY DERIVED FROM THE ISLAND'S OWN ENVELOPE, never absolute metres, so a
  tiny, huge or anisotropic island rescales instead of putting the creature
  inside the rock; island-local results reach world space through
  `IslandDefinition.LocalToGlobal`.
- CONTINUOUS IN TIME, which is a separate promise from being a pure function and
  is enforced by its own test. The first version switched the jelly's radius and
  altitude instantly at the day/night boundary; on the wire a teleport is
  indistinguishable from a despawn-and-respawn, which is the complaint the whole
  feature was reported for. Phase terms are blended on a smooth ramp, and the
  manta's vertical band is traversed up and back rather than snapped (retail's
  target snapped at the 360-degree wrap and its steered creature glided down; a
  closed form has no glide).
- CONSTANT SPEED, NOT CONSTANT LAP TIME. Retail advanced the patrol target when
  the creature REACHED it, so lap time followed island size. The first version
  fixed the lap at 144 s regardless, which works out to 23 m/s on the largest
  island in the catalogue - a manta ray at 84 km/h. At a fixed 8 m/s the smallest
  tier-1 island laps in about a minute and the largest in about eight.

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
