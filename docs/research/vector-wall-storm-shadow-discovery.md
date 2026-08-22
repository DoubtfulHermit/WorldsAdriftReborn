# Vector wall, storm and damage SHADOW — Track 8 record

Status: pure-policy implementation, 2026-08-22. No live wiring, service change,
client change, manifest change, deployment or restart.

This is Track 8 of `docs/architecture/retail-flight-program-board.md`. The code
is deliberately observation-only. It cannot move a hull, change a component,
apply health damage, detach a part or schedule the live island storm service.

## Discovery period

### Three systems which must not be conflated

1. **Weather walls** are permanent, authored XZ line segments (`1204`). They
   provide the wind/storm/sand curtain and are the only subject of the new
   vector force policy.
2. **Island understorms** are timed island lightning/resource-reset events
   (`1254`). `IslandStormService` already runs their quiet/30-second telegraph/
   active lifecycle. Recovered cadence is 6,300 seconds; the current 45-second
   duration is WAREBORN tuning. There is no recovered evidence that an
   understorm applies ship wall forces or ship damage, so this track does not
   invent either.
3. **The Blight** is a debris storm (`1269`) and remains intentionally absent.

### Recovered wall geometry and bands

From the shipped `WallData`, `WeatherWalls`, `WindPhysicsVisualizer`,
`WallGustBehaviour` and `WallConstantTorqueBehaviour`, already documented in
`docs/research/findings-storm-walls.md`:

- distance is XZ-only distance to a finite line segment; force height is
  effectively unbounded;
- physics begins inside 400 m, ramps linearly to full strength at 200 m, and
  stays full through the centreline;
- the visual field begins inside 800 m; therefore a wall can and should be
  visible for 400 m before it is physical;
- only a Storm Rift inside 300 m is lightning-eligible;
- wind drag selects the nearest physical wall overall;
- gust and yaw queries select the nearest wall of each type, so different types
  may stack while duplicate same-type segments do not;
- a Wind Rift blows radially away from its centreline plus a vertical component;
  Storm Rift, Sand Storm and World End blow along wall forward; Typhon forward
  and Ice Storm down are hard-coded but have zero release-map segments;
- the Wind Rift gust direction is straight down; Storm Rift and Sand Storm gusts
  are random horizontal; Typhon, Ice Storm and World End gusts are zero;
- gust force is applied at a point using a 0.5-second triangular envelope;
- Wind Rift has no yaw table. Storm Rift, Sand Storm and World End apply a
  slowly varying yaw-only torque which weakens as the bow aligns with the wall;
- wall-wind mass attenuation is
  `1 - clamp01(massKg / 4000) * 0.75`, a soft 4:1 ramp, never a threshold;
- shipped drag shape is exponent 2.5 and coefficient 0.007, with acceleration
  clamped so drag cannot overshoot relative wind in one physics step.

### Recovered visual and damage truth

- `1204` supplies client visuals, weather audio, debris and ambient bolts.
- The client is only an exposure sensor: sails report wall wind (`5129`),
  wings/engines report sand intensity (`1256`), and the hull reports storm
  eligibility (`1224`).
- Every health amount, interval, target arbitration and detach rule lived on
  Bossa's lost server. There is no recoverable retail damage arithmetic.
- The strike loop let the client choose/report a hit part. A new server must not
  trust that choice; only server-owned target identities may enter arbitration.

### Lost values

All 50 values formerly delivered through `1229 GlobalWallDataState` are absent:
five wind multipliers, 24 gust strengths/timings and 21 torque values. Damage
rates and lightning scheduling are also absent. Any non-zero value is therefore
labelled **WAREBORN tuning**, injected explicitly, bounded, and disabled by
default. The deterministic replacement PRNG is also WAREBORN policy, not retail.

### Existing implementation

- `Walls/WallFlightInfluence.cs` projects selected release walls into the
  current horizontal scalar `WeatherWallSegment` model.
- `WindField.SampleAt` mixes the nearest wall into ambient wind.
- `FlightIntegrator` remains longitudinal kinematics. It has no force at
  position, angular state, wall yaw, gust state or damage authority.
- `WorldWalls` already serves the 44 client wall entities and is not changed.
- `IslandStormService` already owns understorm cadence and is not changed.

The shadow comparison accepts the current scalar wall-force vector as data. It
never reads or calls these live types.

## Coding period

`VectorWallStormShadow.cs` adds a pure evaluator built on Track 3's
`ShadowVector3` and `ShadowForceAccumulator`:

- recovered physical and visual band functions;
- six wall types and whole-segment XZ geometry;
- injectable, copied, default-off per-type tuning;
- nearest-overall drag and nearest-per-type gust/yaw selection;
- recovered mass attenuation and drag clamp;
- explicit fixed-tick gust pulses, deterministic horizontal direction, separate
  small/big strength and force-at-position torque;
- yaw alignment policy for only the recovered wall types;
- visual/physical/lightning telemetry and scalar-versus-shadow force delta;
- deterministic, idempotent exposure/lightning **intents**, never damage;
- bounded counts, IDs, geometry, mass, speed, step, force, torque and tuning.

Track 2 must eventually own gust/damage scheduling and persist any active pulse.
Keeping the scheduler outside the evaluator prevents wall-clock dependence and
makes replay hashes possible.

## Review period

### Findings closed in review

- **Band discontinuity:** exact 200/400/800 boundaries and 300 m lightning edge
  have explicit tests. The physics function is continuous at both transitions.
- **Frame dependence:** a pulse uses fixed ticks and the recovered 0.5 s envelope;
  equal elapsed time at 5 ms and 10 ms steps produces the same peak.
- **Tunnelling:** accepted speed is at most 250 m/s and step at most 100 ms, so a
  valid step spans at most 25 m, far below the 200 m falloff width. Track 5's
  swept contacts must still run before any future authoritative wall response.
- **Stacking:** drag is one nearest wall; gust/yaw may stack once per type;
  duplicate wall IDs are rejected deterministically.
- **Mass extremes:** invalid/negative mass fails; mass is capped; attenuation
  saturates at 4,000 kg exactly as retail.
- **Safe tuning:** every lost magnitude defaults off and is bounded. Calculated
  drag is capped to Track 3's shadow-force safety limit.
- **Double damage:** at most one intent per nearest type per interval, stable IDs
  include wall/type/ship/bucket/target, and duplicate target IDs are removed.
- **Spoofing:** target identities are explicit server-owned inputs; malformed,
  control-character and delimiter-unsafe IDs are rejected before deterministic
  intent-key construction. No client packet or target report is consumed here.
- **Visual truth:** every sample exposes visual and physics intensity separately;
  a visible-only wall cannot be reported as mechanically selected.

### Deliberately still open

- Retail gust interval values and slowly wandering torque target values are
  unrecoverable. The shadow accepts clock-authored pulses and a bounded torque
  magnitude; an integrated scheduler must label its cadence as WAREBORN tuning.
- Health, armour, conductivity, repair and detach authority do not exist yet.
  Suggested fractions are inert intent metadata and must not be enabled until a
  separately reviewed damage service can dedupe them durably.
- Current scalar wall force is longitudinal. A trustworthy force/torque parity
  view depends on Track 3 integration, not only this policy.
- Client visual agreement requires the exact same non-zero type tuning to be
  represented in a complete `1229`; partial `1229` remains forbidden.

## Testing period

Focused tests cover:

- exact bands and visible-but-nonphysical range;
- all six wall types and eight hull headings;
- 0/1,000/2,000/4,000/40,000 kg attenuation;
- downward Wind Rift wind/gust and force-at-position torque;
- gust determinism and step partition equivalence;
- cross-wall/aligned yaw and Wind Rift's no-yaw rule;
- nearest-wall selection, stacked/corner type influences and deterministic order;
- 300 m lightning boundary, deterministic replay and recovered exposure targets;
- default-off behavior, malformed numbers/IDs/geometry, caps and a 128-wall set.

Full suite and Release-build evidence is recorded with the Track 8 commit.

## Exact dependencies and integration gates

| Track | Dependency |
|---|---|
| 2 fixed clock/snapshots (`53d3b56`) | **Required for live scheduling.** Supplies fixed tick, catches up deterministically, snapshots active gust cadence/intents and prevents restart replay. Not needed by the pure evaluator. |
| 3 vector rigidbody (`5dcd7d2`, locally cherry-picked as `4776574`) | **Direct compile dependency.** Supplies `ShadowVector3`, force-at-position accumulator, mass/COM/inertia policy and eventually live vector authority. |
| 4 lift/gravity (`847c58f`) | **Force-composition dependency.** Downward Wind Rift force must enter the same vertical accumulator before lift caps/overload are evaluated. No compile dependency today. |
| 5 collisions (`9358b1b`) | **Ordering/safety dependency.** Swept collision/contact resolution must precede live wall response and wall/contact damage must not be charged twice. No compile dependency today. |

Live integration order is therefore: Track 2 + Track 3 + Track 4 + Track 5,
then shadow telemetry, then separately gated force-only authority, and damage
intents last. Track 6 docking must suppress/define wall motion for captured hulls;
Track 7 fuel must remain the sole engine-thrust gate. Neither is modified here.

## Deployment impact

None. There is no environment switch, service construction, `FlightSession`
call, telemetry schema change, wire component, client DLL or manifest change.
