# Wiring the fauna ecology: the design, and how the wiring actually went

2026-08-18, `feat/fauna-schools`. Written at the end of Phase 2 as the plan for
a then-gated wiring step; **the wiring has since LANDED** (the front-end
extraction moved the mirror into `WorldsAdriftServer/Web/Assets/map-fauna.js`,
shared by the admin console AND the live public map at `/map`, and the go was
given). The design below is kept as written, with a dated addendum where
reality differed.

## 0. WIRING ADDENDUM (post-landing corrections)

- **Schema went 8 → 9, not 7 → 8**: ships-on-the-map and the operator fields
  took v8 while this branch was staged. Everything else in section 5 holds.
- **Two consumers, one mirror.** The mirror now serves the admin console and
  the public map from the same `map-fauna.js`; the ecology block was ADMITTED
  to the public projection deliberately (world geometry + counts, zero
  identity), with one exception: **`worldSeed` stays admin-only** - an operator
  knob the browser derives nothing from, since blooms arrive as published
  numbers. The public leak corpus carries sentinels for both decisions.
- **The manta's vertical band keeps the ISLAND LAP's pace, not the bloom
  orbit's.** Measured during wiring: a bloom orbit is tens of metres across, a
  circuit takes ~30 s, and a band tied to it pumped the manta's full climb at
  up to 8 m/s of pure vertical - frantic, and over the pose budget. Retail's
  band traversal took one patrol lap (minutes); `mantaLapSeconds` is the
  recovered pace and is what both evaluators use.
- **`MaxSizeScale` is 2.0, not 1.8.** At 1.8 the largest tier-1 island rounds
  to 7 mantas against a group size of 4, so NO tier-1 island could ever carry
  a second group - and layering was the point. 2.0 is exactly two full
  baseline schools; the biggest island's worst case (8 + 12 = 20) still clears
  the per-peer 24 without the clamp.
- **Cross-flag id stability is scoped honestly**: ids are a pure function of
  the catalogue (`IslandFaunaCapacity.IdBlockFor` reserves each island's
  widest-case block, so quiet/peer/world knobs only choose WHICH reserved ids
  go live), but the ecology flag itself selects a different plan shape. That
  is safe because the flag is read once at boot and no client session survives
  a restart.
- Parity now covers the ecology at 1e-9 m
  (`The_ecology_mirror_returns_the_same_metres_as_the_evaluator`), with bloom
  parameters shaped exactly as the live feed publishes them.

---

## 1. What is already built (staged, unwired)

| module | what it holds | state |
|---|---|---|
| `IslandFaunaEcology` | blooms (analytic moving field maxima), `F(x,t)` + gradient, group limit-orbits, clearance floors, seeded uniforms | tested, unused by the service |
| `IslandFaunaCapacity` | AABB-driven capacity, quiet islands (empty/sparse/ordinary), per-peer clamp, group counts | tested, unused by the service |
| `IslandFaunaPolicy.SchoolsPerIsland` | still 1 - the live population shape is unchanged | live |

Design decisions already burned into the staged modules, so the wiring step
does not re-litigate them:

- **No day-index reseed.** The architecture sketch seeded bloom paths from
  `hash(seed, islandId, day)`; a reseed is a discontinuity, and on the wire a
  discontinuity is a despawn. Incommensurate periods give "different every day"
  continuously instead. Recorded in `IslandFaunaEcology`'s type remarks.
- **The limit orbit stands in for the ODE.** `v = α∇F + β(ŷ×∇F)` is not
  integrable in closed form; the staged motion is the bounded orbit that flow
  settles into, tested to stay within 2σ of its maximum.
- **Clearance by construction.** Bloom rings sit at
  `floor + drift + maxGroupOrbit`, so the closest approach equals the species'
  recovered floor (manta: half-diagonal + 10 m; jelly: 1.05× lateral). No
  terrain query exists, so no runtime check could save a bad parameter.

## 2. The wiring, service side

One new switch: **`WAREBORN_ISLAND_FAUNA_ECOLOGY`** (same token grammar as
every other fauna flag; OFF by default; a typo fails safe to the current
motion). One optional knob: **`WAREBORN_ISLAND_FAUNA_SEED`** (int; default
`IslandFaunaEcology.DefaultWorldSeed`).

With the flag ON:

1. **Population** comes from `IslandFaunaCapacity` instead of
   `IslandFaunaPolicy.PopulationFor`'s flat tier counts:
   `CapacityFor(species, tier, envelope, islandId)` →
   `ClampedToPeerBudget(..., perPeerBudget)` →
   `GroupCountFor(species, capacity)` groups of `capacity/groups` members
   (remainder to the first groups). Entity-id allocation keeps
   `IslandFaunaPlan`'s rule - ids allocated from the FULL demand in island
   order before any budget/quiet decision - so flipping the ecology flag, the
   seed, or the budget never moves an existing id onto a different animal.
   NOTE: because ids must be stable ACROSS THE FLAG ITSELF, the id ledger is
   allocated from the flat-tier demand exactly as today, and the ecology
   selects WHICH of those ids are live (quiet islands simply leave theirs
   unseeded), the same shape `IslandFaunaPlan.Build` already uses for the
   world budget.
2. **Motion**: `IslandFaunaMovement.WorldTransformAt` gains an ecology-aware
   sibling (`EcologyTransformAt`) that the registry's pose delegate selects at
   construction from the flag. Lateral school centre =
   `IslandFaunaEcology.GroupCentreAt(bloom, species, schoolIndex, t)` relative
   to the envelope's lateral centre. Vertical: mantas keep the recovered
   midpoint-to-top band driven by their orbit angle; jellies keep the recovered
   day/night altitude blend. Member offsets, orientation (banked heading from
   the same finite-difference machinery) and the 4 Hz cadence are untouched.
3. **Groups**: `FaunaCreature.SchoolIndex` already carries multiple groups;
   `SchoolsPerIsland` stays 1 in the legacy path, and the ecology path derives
   its group count from capacity. Nothing else in checkout changes - interest
   stays island-keyed, whole-island, retention-first.

Byte-discipline: with the flag OFF, every line on the wire is byte-identical
to today (the new code is unreachable); with fauna OFF entirely, non-fauna
lines remain byte-identical as always.

## 3. The published model additions (AdminPage's data, not its JS)

`IslandFaunaMapModel` grows, mirroring its existing precompute-everything
split (the browser gets NUMBERS, it restates only the time part):

```csharp
// constants block (FaunaMapConstants gains:)
bool   EcologyEnabled;
int    WorldSeed;
double MantaCirculationSigmaRatio;   // from CirculationSigmaRatioFor
double JellyCirculationSigmaRatio;
double MantaOrbitMetresPerSecond;    // from OrbitMetresPerSecondFor
double JellyOrbitMetresPerSecond;
double MaxGroupSpread;

// per island (FaunaIslandMotion gains a list):
record FaunaBloomModel(          // one per (species, bloomIndex)
    string Species,              // "manta" | "jelly"
    double Amplitude, double SigmaMetres,
    double AnnulusRadiusMetres, double RadialDriftMetres,
    double AngularDriftRadians,
    double OmegaRadial, double OmegaAngular, double OmegaMigration,
    double PhaseRadial, double PhaseAngular, double BaseAngleRadians);

// per island per species:
int GroupCount;                  // from IslandFaunaCapacity.GroupCountFor
```

All fields are produced by calling the ecology module's own functions -
never restated - exactly as `FaunaIslandMotion` does today.

## 4. The mirror JS (to be transcribed into AdminPage.cs on the go)

Added INSIDE the existing `faunaMotion(M)` factory, between the markers, so
the parity test's extraction keeps working unchanged:

```js
function bloomCentre(b,t){
  var r=b.annulusRadius+b.radialDrift*Math.sin(b.omegaRadial*t+b.phaseRadial);
  var a=b.baseAngle+b.omegaMigration*t
       +b.angularDrift*Math.sin(b.omegaAngular*t+b.phaseAngular);
  return {x:r*Math.sin(a),z:r*Math.cos(a)};
}
function groupOrbitRadius(b,species,groupIndex){
  var ratio=species==='manta'?M.mantaCirculationSigmaRatio:M.jellyCirculationSigmaRatio;
  var spread=1+(M.maxGroupSpread-1)*fraction((groupIndex+1)*M.goldenRatioFraction);
  return b.sigma*ratio*spread;
}
function ecologyGroupCentre(p,b,species,groupIndex,t){
  var c=bloomCentre(b,t);
  var r=groupOrbitRadius(b,species,groupIndex);
  var speed=species==='manta'?M.mantaOrbitSpeed:M.jellyOrbitSpeed;
  var a=(speed/Math.max(r,1))*t+2*Math.PI*schoolPhase(groupIndex);
  return {x:p.cx+c.x+r*Math.sin(a),z:p.cz+c.z+r*Math.cos(a)};
}
// schoolCentre() selects: M.ecologyEnabled && island has blooms
//   -> ecologyGroupCentre (lateral) + the EXISTING vertical laws
//   -> else the current mantaCentre/jellyCentre unchanged.
```

Vertical stays the existing mirror code (manta band, jelly dayness) - only the
lateral source changes, which keeps the mirror diff small and the parity
surface mostly untouched.

`AdminFaunaParityTests` grows one case per species with the flag on: evaluate
`IslandFaunaEcology.GroupCentreAt` + the vertical law in C# at fixed
timestamps against the extracted JS, same 1e-9 bound, same real-island
parameters.

## 5. The telemetry contract (schemaVersion 7 → 8, applied at wiring)

`StatsSnapshot.SchemaVersion` bumps to 8 **in the wiring commit** (not
before - the contract version and the payload must move together). The `fauna`
block grows an `ecology` object, written unconditionally (absence = older
server, never "no ecology"):

```json
"fauna": {
  "...": "existing v7 fields unchanged",
  "ecology": {
    "enabled": true,
    "worldSeed": 1,
    "islands": [
      {
        "islandId": "beautiful-wildlands",
        "quietFactor": 1.0,
        "capacity":  { "mantaRays": 5, "jellyFish": 7 },
        "expressed": { "mantaRays": 5, "jellyFish": 7 },
        "groups": [
          { "species": "manta", "index": 0, "bloom": 0,
            "behaviour": "Cruise", "epochSeconds": 0.0 }
        ],
        "blooms": [
          { "species": "manta", "index": 0,
            "amplitude": 0.62, "sigma": 41.2,
            "annulusRadius": 445.1, "radialDrift": 18.9,
            "angularDrift": 0.31,
            "omegaRadial": 0.011, "omegaAngular": 0.0068,
            "omegaMigration": 0.0027,
            "phaseRadial": 2.1, "phaseAngular": 4.9, "baseAngle": 1.2 }
        ]
      }
    ]
  }
}
```

- `quietFactor` makes an empty island a DELIBERATE zero on the map (the
  quiet doctrine's legibility requirement).
- `expressed` vs `capacity` is the Phase 3 rhythm's surface: until Phase 3
  wires, `expressed == capacity` and a `"phase"` field is absent. Phase 3 adds
  `"phase": "Bloom"`, `"phaseFraction": 0.42` per island per species.
- `groups[].behaviour/epochSeconds` is the architecture's ONE permitted piece
  of state - the published `(behaviour, epoch)` pair. Until Phase 4 wires
  behaviours it is constant `("Cruise", 0)`.
- `GameStats` (login-server side) parses the block tolerantly: every field
  `(type?)` with defaults, so a v7 snapshot renders exactly as today. The admin
  map draws the ecology layer only when `ecology.enabled` is present and true.

Estimated size: ~46 tier-1 islands × (2-4 blooms ≈ 220 B + groups ≈ 80 B +
capacity line ≈ 90 B) ≈ **18 KB** on top of the current 3.2 KB fauna section of
a 54 KB file. Acceptable for a few-second cadence; if it ever matters, blooms
are static per boot and could move to a once-per-session endpoint - noted, not
built.

## 6. Order of operations on the go

1. Confirm admin-map-ships has landed; merge/rebase so `AdminPage.cs` is
   current. Run `AdminFaunaParityTests` green BEFORE touching anything.
2. Wiring commit A (server): flag + capacity-driven plan + ecology pose path +
   schemaVersion 8 + telemetry writer + `GameStats` tolerant parse +
   `IslandFaunaMapModel` additions. The mirror is untouched in this commit:
   the model gains fields the existing JS simply ignores, so parity stays
   green throughout.
3. Wiring commit B (mirror): the JS from section 4 between the markers, the
   new parity cases, the map's ecology rendering. Parity green with the flag
   ON and OFF.
4. Gate: build, Multiplayer.Tests, parity, acceptance, `SOAK_FAUNA=1` soak
   with `WAREBORN_ISLAND_FAUNA_ECOLOGY=1` - FLAT required, fauna checkouts
   required, and the per-peer ceiling must still read 96/s in the boot line.

## 7. Provenance ledger for the new layers

- WAREBORN TUNING: bloom shapes/periods/fractions, circulation ratios, jelly
  orbit speed, capacity constants, quiet thresholds, group-count rule, the
  world seed.
- RECOVERED (cited in the module docs): the AABB as the sizing measure
  (HabitatVisualiser/FlockVisualiser/PatrolVisualiser), the clearance floors
  (patrol half-diagonal + 10 m; jelly rim station), per-species independent
  orbit phases (HabitatPatrolState 4332), the manta's 8 m/s, constant speed
  rather than constant lap time, the irregular-path CHARACTER
  (reached-waypoint patrol), populations swinging to critically-low
  (PopulationManagementState) behind the quiet doctrine.
