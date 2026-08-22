# Retail lift, gravity and overload discovery

Status: Track 4 discovery/coding/review/test record, 2026-08-22. This branch is
pure shadow policy only. It does not wire `FlightSession`, change components,
move ships, deploy, restart a service or require a client manifest.

## Evidence and provenance

### Recovered from shipped code or data

- `ShipLiftVisualizer` computes `TotalLift = AtlasMultiplier * 1258.totalLift`,
  `Load = 1257.totalMass / max(1, TotalLift)` and overload as the strict test
  `totalMass > TotalLift`.
- `ShipControlsBehaviour.UpdateVertical` stops updating the vertical control and
  displays the exact retail message when overloaded.
- `ShipControlVisualizer.UpdateFloating` applies vertical lift as
  `clamp(-mass*g + compensation + commandedLift, 0, TotalLift*abs(g))`.
  The core cancels weight; it is anti-gravity, not aerodynamic lift. It consumes
  no fuel and works at zero airspeed.
- `ShipPreprocessor` carries serialized defaults `_liftSpeedCap = 2` m/s and
  `_liftAccelerationCap = 1` m/s2. The speed comparisons are strict (`>` and
  `<`), so exactly 2 m/s still accepts input.
- `ShipControlVisualizer` smooths commanded lift with Unity `SmoothDamp`, using
  `smoothTime = deltaTime * 8`. The shadow evaluator mirrors Unity's scalar
  polynomial and overshoot guard.
- Abandoned flight uses -0.05 m/s2 until vertical velocity is below -0.1 m/s.
  `ShipAbandonedBehaviour` marks a ship abandoned after `CoreDampenTime >= 86400`
  (24 hours without a registered/aboard player).
- `1257 ParentingMassAdderState` is hull plus mounted `1121 OriginalMassState`
  contributions. `1258 ShipLiftState` carries total lift, total torque and a
  reliability bit. `1115 ShipCoreState` carries one per-core `maxLift` float.
- The release item catalogue says the base Atlas Sky Core can lift exactly
  1000 kg. Atlas Enhancer and Core Generator each increase lift by **at least**
  400 kg. "At least" is a lower bound, not an exact final value.
- The recovered community Atlas Core table is already encoded by
  `MaterialCatalog.SkyCoreLiftKg`: `1000 + rate * (10 + quality)`, reproducing
  all twelve published metal rows at Q1 and Q10.
- Retail restricted a ship to one core. Core modules are socketed below
  `CoreMain`; orphan modules do not create an independent flying ship.
- `EndOfTheWorld_Patch` already pins the expired shutdown `AtlasMultiplier` to
  1. This is required for any nonzero client lift and is already shipped.

### Wareborn tuning or reconstruction

- `HullMassCalculator.UnitsPerHullCell`, units per deck and the mixed-hull metal
  share are labelled chosen calibration. The material weight ordering and table
  are recovered, but retail's absolute frame-unit scale did not survive.
- Mounted parts currently contribute a flat 50 kg. Retail used authored
  per-part masses, which have not survived.
- The live `1258` value of 1,000,000 kg is an explicit Wareborn safety seed. It
  is roughly a thousand times the recovered base core and deliberately prevents
  overload while the migration is unfinished.
- The legacy grandfather floor introduced by this branch is a safety mechanism,
  not retail balance. Its *formula* is derived from recovered mechanics:
  `mass * (1 + 1/abs(g))` preserves both hover and the full recovered +1 m/s2
  climb. It remains shadow-only until persisted by Track 2.

### Unrecoverable or not yet recovered

- The exact Unity project gravity vector has not been extracted reliably from
  the shipped `globalgamemanagers`. Player fall measurements (~-17.7 m/s2) are
  not sufficient proof of `Physics.gravity`, because player movement may add
  acceleration. Gravity is therefore an explicit shadow input, never invented.
- The final per-upgrade lift equations and six blank-description module effects
  are lost. The 400 kg values are only declared minima.
- Persisted `MountedPartRecord` stores part identity/prefab but not the metal and
  quality used to craft a core. Consequently an existing core's exact recovered
  capacity cannot be reconstructed after restart from today's save.
- The retail worker's `8067 ShipPartAccumulateState` aggregation implementation
  and core torque balance values are not present. Track 3 owns torque.
- The compensation controller observes actual rigidbody acceleration and known
  wind/engine/edge forces. Its code survives, but its live inputs do not exist in
  the current scalar session. Track 3 must supply them from its vector accumulator.

## Production-save implications

`WorldStateSnapshot` persists hull geometry/materials and mounted-part identity,
but not core material/quality. The branch adds `ProductionHullLiftAudit.Audit`,
which is read-only and deterministic:

1. Decode every non-salvaged hull and calculate the same hull plus mounted-part
   mass used by current flight and 1257.
2. Count `CoreMain`/`atlasSkyCore` and the two upgrades with recovered minimums.
3. Report a **known minimum** capacity, never an invented exact capacity.
4. Flag corrupt hulls, no-core/orphan-module cases and multi-core records.
5. Classify each row as authentic, requiring a legacy lift floor, invalid, or
   (for future builds) blocked before activation.

It does not mutate or rewrite the snapshot. An integration tool must run the
audit against a copy of production state before activation, retain the report,
and write an additive versioned grandfather field only after backup. Existing
ship list order must never change because mounted parts address it by index.

## Review record

- **Overweight restore/spawn:** existing overweight ships get an explicit
  grandfather decision; new equivalents are blocked. Capacity equal to mass is
  rejected as insufficient because it removes climb headroom.
- **Fuel independence:** no fuel field exists in the evaluator. Empty fuel can
  disable engines in Track 7 but cannot disable core lift.
- **Invalid data:** nonfinite/nonpositive mass, gravity or timestep quarantines
  the evaluation. Negative/nonfinite capacity becomes zero lift and observable
  overload, never infinite lift.
- **Detach/mount invalidation:** the audit recomputes from current hull and part
  records. An orphan module contributes zero without `CoreMain`; multiple cores
  are surfaced and never multiply the one-core budget.
- **Component/admin parity:** `LiftGravityTelemetry` projects the same mass and
  effective capacity intended for 1257/1258, admin and shadow comparison.
- **Legacy safety:** authentic capacity remains visible beside the effective
  grandfather floor. Operators can see and reverse the exception rather than a
  hidden million-kilogram fallback.
- **Restart representation:** the audit is read-only and deterministic across a
  JSON round trip. Actual persistence of smoothing state, lift disposition and
  grandfather capacity belongs to Track 2.

## Exact integration dependency

This branch cannot safely affect live flight by itself.

1. **Track 2 (`53d3b56`, `feat/fixed-clock-snapshots`)** already provides the
   authoritative fixed `dt` and `DurableShipFlightSnapshot` v1. Track integration
   must extend that snapshot additively (v2) with current command lift force, smoothing
   velocity, authentic/effective capacity, migration disposition and any
   abandoned timer. Restore must revoke stale pilot authority and preserve a
   moving overweight hull without a one-frame fall.
2. **Track 3 (`5dcd7d2`, pure shadow only)** must provide vector mass/gravity,
   external vertical forces and force-accumulator telemetry. Its quaternion and
   force-at-position model remains read-only until Track 2 integrates.
3. The Track-4 adapter then calls `RetailLiftGravityShadow.Step` inside Track 3's
   fixed step, applies the returned world-up lift force at centre of mass
   (`torqueless=true`) to its accumulator, and
   publishes `LiftGravityTelemetry` beside scalar flight. No live switch should
   exist until shadow deltas, production audit and restart tests are green.
4. Component 1257, component 1258 and admin output must be switched in one
   integration commit from the same projection. Serving authentic 1258 without
   matching authoritative enforcement/admin truth would split the client and
   server overload decisions.

Track 3 currently caps `ShadowForceAccumulator` at 256 calls, the same as its
maximum mounted-part count. Integration must reserve a separate aggregate force
slot (or make gravity/lift intrinsic to integration) so a legal 256-part ship is
not rejected merely because lift is the 257th force. Gravity must be applied
exactly once; do not both put `mass*g` in the accumulator and add it again while
integrating acceleration.

## Testing performed on this branch

The dedicated matrix covers under/at/over capacity; hover/climb/sink; strict
speed caps; abandoned sinking; fuel independence; invalid values; compensation;
legacy versus future builds; core detach; orphan modules; multiple cores;
mounted-part invalidation; corrupt/salvaged hulls; JSON restart representation;
and 1257/1258/admin parity. Full suite/build evidence belongs in the commit
handoff, not this durable discovery record.
