# Vector rigidbody flight shadow: discovery, review and acceptance

Status: implemented as pure observation-only primitives on 2026-08-22. It is not
wired to the runtime, cannot publish component 1130 and has zero gameplay effect.

This record is the Track 3 hand-off for the retail-flight reconstruction plan. It
deliberately does not own the fixed clock, flight-session persistence or snapshot
format. Those are Track 2 seams and must be rebased and wired by the integrator
after Track 2 lands.

## Period 1 — discovery

### Sources read

Retail client decompile under `/home/ttanurhan/Games/WAReborn-decompiled/acs`:

- `Assets.Visualizers/EngineVisualizer.cs`
- `SailBehaviour.cs`
- `Assets.Visualizers/ShipMotionVisualizer.cs`
- `Assets.Visualizers/ShipControlVisualizer.cs`
- `ShipControlsBehaviour.cs`
- `ShipLiftVisualizer.cs`
- `Assets.Visualizers.Weather/WindPhysicsVisualizer.cs`
- `Assets.Scripts.Visualisers.ShipParts/WingVisualizer.cs`
- `Assets.Visualizers/SelfRighteningVisualizer.cs`
- `ShipConfiguration.cs`
- `WingTorqueData.cs`

Current server paths:

- `Ship/Flight/ShipForceModel.cs`, `ShipForceEvaluation.cs`, `FlightIntegrator.cs`
- `ShipMotion.cs` and `Placement/Quaternion32Packing.cs`
- `Game/Crafting/MountedParts.cs`, `Game/PartMountService.cs`
- `Persistence/WorldStateSnapshot.cs`

### Coordinate and representation map

| Quantity | Convention | Evidence / confidence |
|---|---|---|
| Hull-local axes | `+X right`, `+Y up`, `+Z forward`, right-handed cross product | Unity `Transform`, `Vector3.right/up/forward`; recovered |
| Engine direction | mounted engine `transform.forward` | `EngineVisualizer.Update`; recovered |
| Engine application point | prop transform if present, otherwise part transform; null for torqueless engines | `EngineVisualizer.Update`; recovered |
| Sail direction | yaw-joint right vector, signed toward wind | `SailBehaviour.Update`; recovered |
| Sail application point | sail transform position | `SailBehaviour.Update`; recovered |
| Mount position | hull-local Q52.12 fixed-point metres | `MountedParts.Mount.LocalOffset`, `MountedPartRecord.LocalX/Y/Z`; proven in current server |
| Mount rotation | hull-local packed `Quaternion32`, W-first decoded value | `PackedRotation`, `Quaternion32Packing`; proven in current server |
| 1130 position/velocity | global metres and global m/s | `ShipControlPointSpec`; proven |
| 1130 orientation | packed Quaternion32 at wire edge | `FlightIntegrator.PackedRotation`; proven |
| 1130 cadence | 0.24 s; client reject floor 0.228 s | `ShipConfiguration.SendInterval` and control-point validator; recovered |

The shadow works entirely in hull-local coordinates. A later owner must rotate the
result once into world axes before integrating velocity and must never mix Unity
origin-relative coordinates with global 1130 metres.

### Recovered equations and behavior

Engines:

```text
F = ShipThrustMultiplier × CurrentPercentSpin × (Boost + EngineState.Power)
    × partForward
```

`ShipThrustMultiplier` ships as 1.0. `Power`, `Boost`, spin dynamics and part mass
were produced by the lost GSim or prefab/material system. The new shadow implements
the force geometry and accepts the magnitude as explicit tuning/data. Boost is not
implemented in this slice.

Sails:

```text
efficiency = abs(dot(normalize(wind), yawJoint.right))
lift = signed(yawJoint.right × efficiency × |wind| × SailState.Power)
force = lift - project(lift, hull.right)
minimum = 0.3 × |wind| × SailState.Power
```

Before this equation, retail normalizes every wind weaker than 1 m/s to unit
strength and substitutes hull-local forward for exact calm. This counter-intuitive
fallback is preserved and mutation-tested.

The shipped `ShipMotionVisualizer` takes the constant-false branch, so it projects
hull-right out and applies the remainder at the sail position. It only rescales a
non-zero remainder to the minimum; normalizing zero remains zero in Unity. Sail yaw
is dynamic retail behavior and is not reconstructible from the static mount rotation
alone. A future adapter must supply the current effective yaw or label mount rotation
as an approximation.

Force accumulation:

```text
localForce  = inverse(hullRotation) × worldForce
r           = localApplicationPoint - Rigidbody.centerOfMass
rawTorque  += cross(r, localForce)
retailTorque.x = 0
retailTorque *= max((|torque| - 2500) / |torque|, 0) × 0.5
```

The X suppression is important: ordinary off-centre engine/sail forces cannot roll
the retail hull through this path. Wings, core controls and self-rightening apply
torque by separate paths and are explicitly outside this slice.

Other recovered systems mapped but deferred:

- wind drag is force `mass × GetDrag(wind × massMultiplier - velocity)` with the
  shipped serialized coefficient/exponent `0.007 / 2.5`;
- hull wind multiplier is `1 - clamp01(mass/4000) × 0.75`;
- wing torque maps pitch/yaw/roll to local `(+X,+Y,-Z)` and depends on velocity;
- core control torque scales by `mass ^ CoreMassExponentialFactor`;
- self-rightening uses a force couple about world up and angular drag 1;
- lift counters gravity up to the lift ceiling and overload blocks vertical input.

These are discovery inputs for later tracks, not hidden behavior in this shadow.

### Recovered, tuned, approximated and lost

| Item | Classification |
|---|---|
| Axis handedness, transform directions, `r × F`, torqueless path | recovered |
| X torque suppression, 2500 N·m dead zone, 0.5 scale | recovered |
| Sail efficiency shape and 0.3 minimum | recovered |
| `ShipThrustMultiplier = 1` | recovered shipped value, remotely overridable |
| Drag `0.007`, exponent `2.5` | recovered serialized shipped values, remotely overridable |
| Engine/Sail `Power`, Boost, spin response | lost GSim/data; Wareborn tuning until measured |
| Effective live sail yaw | client-dynamic; later adapter approximation unless observed |
| Hull centre of mass and inertia tensor | Unity collider result not available server-side; approximation |
| Hull bounding box used by shadow inertia | later adapter-derived Wareborn approximation |
| WingTorqueData serialized asset values | not recovered here; deferred |
| Retail live remote config overrides | unknowable |

## Period 2 — coding

`VectorRigidBodyShadow.cs` adds dependency-free primitives only:

- finite double-precision vectors and normalized quaternions;
- validated engine/sail propulsors;
- deterministic linear-force and raw-torque accumulation;
- separately exposed retail-filtered torque;
- point-mass centre of mass and diagonal cuboid inertia approximation;
- recovered engine and sail vector force shapes;
- scalar-current versus vector-shadow comparison records;
- 256-part, 256-metre, mass and force safety ceilings.

There is deliberately no configuration switch, static service instance, timer,
session mutation, persistence record, game-server reference or control-point writer.
The only possible output is a value returned to a caller.

## Period 3 — review

Self-review findings and resolutions:

1. **Torque sign:** a +X-mounted +Z engine must produce -Y yaw. A mutation guard
   asserts `cross(position-COM, force)` and the filtered -750 N·m result for a
   -4000 N·m raw torque.
2. **Torque semantics:** raw physical torque and retail-filtered torque are both
   retained. This prevents later work from confusing retail's deliberate X
   suppression with a mathematical cross-product limitation.
3. **Units:** all positions are metres, masses kg, forces N, torque N·m and inertia
   kg·m². Q52.12 decoding belongs in the future adapter, not the pure model.
4. **Quaternion safety:** construction normalizes finite values and rejects zero or
   non-finite input. Rotation order is W-first and matches Quaternion32 decoding.
5. **Numeric stability:** every public evaluation validates finite values, positive
   hull mass/extents and bounded part geometry before arithmetic. Deterministic
   index order is retained; no dictionaries or random sampling are used.
6. **Malicious geometry:** offsets beyond 256 m, per-part mass beyond 100,000 kg,
   force magnitude beyond 100 MN, non-finite values and more than 256 parts fail
   closed. No exception or partial live state is possible.
7. **Inertia honesty:** the result carries `IsApproximation=true`. It uses a solid
   cuboid hull plus mounted point masses and the parallel-axis theorem; it does not
   claim Unity collider equivalence.
8. **Compatibility:** existing scalar types are unchanged. The comparison record
   reads `ShipForceEvaluation` but does not alter it.
9. **Scope:** lift, collisions, walls, wings, self-rightening, angular integration,
   live hull authority and 1130 emission are absent by design.

No unresolved correctness defect was found in the shadow slice. The largest fidelity
risk is effective sail yaw; integration must not pretend a static packed mount rotation
is the automatically trimmed yaw joint.

## Period 4 — tests

The focused suite covers:

- symmetric engines cancel torque;
- an off-centre engine produces the recovered torque sign and filter result;
- torqueless engines retain force but produce no torque;
- engine direction at eight 45-degree headings;
- mirrored sails produce equal forward force and cancel yaw torque;
- exact-calm and weak-wind sail fallbacks;
- centre of mass and inertia across multiple mass/part configurations;
- bit-identical replay over 1,000 evaluations;
- invalid/NaN/infinite/out-of-range transforms and quaternions;
- the 256-part cap and a broad 512,000-part-visit performance tripwire;
- scalar/vector comparison axes;
- mutation guards for the recovered constants.

## Later opt-in acceptance matrix

This matrix is for a later integration branch after Track 2. Shadow means telemetry
only; live means authority has explicitly been switched for a disposable ship.

| Scenario | Shadow gate | Opt-in live gate |
|---|---|---|
| Centre-mounted engine | forward delta explained and bounded | straight acceleration, no angular drift |
| ±X symmetric engines | raw/filtered yaw near zero | straight flight under eight headings |
| One +X engine | expected negative-y torque | visible yaw sign matches shadow |
| Mirrored sails | lateral/yaw cancellation | straight sail-only departure |
| Asymmetric sails | stable non-zero torque comparison | turn direction matches shadow |
| Light/reference/heavy hull | finite COM/inertia and force/mass response | monotonic acceleration by thrust-to-mass |
| 1/16/64/256 parts | bounded tick cost | no cadence misses or 1130 bursts |
| Restart during motion | bit-stable shadow input after Track-2 restore | position, rotation, linear/angular velocity resume |
| Malformed mount | rejected counter, no result used | no crash, movement unaffected |
| Two clients aboard | identical observed 1130 path | no deck slip or divergent orientation |

Promotion must be staged `off → shadow telemetry → disposable-ship opt-in → bounded
cohort → default`. Any scalar/vector discrepancy must be classified as recovered
geometry, lost tuning, adapter error or later-system omission before enabling live.

## Precise Track 2 rebase and wiring dependency

Rebase this commit after the final Track 2 fixed-clock/snapshot commit. Resolve no
clock or persistence conflict by moving those seams into this branch: this branch
does not touch them.

The later adapter should run exactly once per Track-2 authoritative fixed step and:

1. snapshot mounted parts in deterministic entity-id order;
2. decode Q52.12 offsets and Quaternion32 rotations once per mount revision;
3. derive/label hull half-extents and part masses;
4. supply current engine spin and effective sail yaw;
5. call `VectorRigidBodyShadow.TryEvaluate` with the same wind sample as the scalar
   evaluator;
6. store a bounded comparison record keyed by Track-2 simulation tick;
7. expose telemetry only, without feeding the result into Track-2 state or 1130.

Only a later reviewed opt-in phase may integrate the returned force and torque. That
phase needs angular velocity/orientation in the Track-2 durable snapshot and is not
part of this commit.
