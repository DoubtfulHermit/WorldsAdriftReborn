# Retail flight reconstruction plan

Status: active after production build `a70d4f1` (2026-08-22).

This plan turns the recovered retail flight audit into dependency-gated work.
It does not call Wareborn tuning "retail": equations and serialized values are
recovered where evidence survives; lost GSim balance values remain explicit
server tuning.

## Baseline now in production

- server-authoritative longitudinal flight and stock 1130 replication;
- recovered drag coefficient `0.007` and exponent `2.5`;
- per-mount sail geometry and the recovered 30% force floor, aggregated into
  the current scalar model;
- one authoritative force evaluator shared by runtime and admin telemetry;
- hull plus live mounted-part mass (mounted parts currently use the served
  50 kg approximation);
- secured helm, sail, pickup and mount authority;
- sail-only wake and shipyard-departure lifecycle;
- recovered release-world bounds enabled explicitly in production: horizontal
  push at `+/-17600 m`, clamp at `+/-17700 m`, vertical push above `800 m`,
  clamp at `1000 m`, evaluated in 20 ms reference slices.

The remaining model is still longitudinal kinematics. Pitch and roll are mostly
presentation, and there is no authoritative rigidbody collision simulation.

## Evidence labels

- **Recovered**: proved by retail decompile, serialized data, protocol or two
  independent preserved sources.
- **Tuning**: Wareborn value chosen to make the revival playable; never
  presented as an original constant.
- **Approximation**: deliberately simpler structure than retail.
- **Missing**: retail structure is known but not implemented.
- **Unrecoverable**: the value lived only in lost GSim/server data.

Every phase must preserve these labels in tests, telemetry and operator copy.

## Phase 0 - world safety envelope (deployed, acceptance open)

Scope is the recovered world-boundary policy already shipped in `a70d4f1`.

Live acceptance gate:

1. Verify normal interior flight, reverse, sail-only wake and dismount behavior.
2. Use a disposable/test hull to approach one horizontal push band and record
   boundary distance, applied velocity delta and clamp state.
3. Climb through 800 m and prove resistance; attempt 1000 m and prove the hard
   limit without NaN, teleport or passenger separation.
4. Relog after the test and prove the hull restores at a legal pose.

Rollback is `WAREBORN_FLIGHT_WORLD_BOUNDS=0`; no client patch is involved.

## Phase 1 - deterministic clock and durable flight state

Build a bounded 50 Hz fixed-step accumulator for all flight, while retaining
the stock 0.24 s network control-point cadence. Do not permit unbounded catch-up
after a stall. Persist position/orientation, linear/angular velocity, latched
input, fuel, authority epoch and the last accepted simulation time through one
versioned ship snapshot.

Acceptance:

- identical input produces identical hashes under different outer-loop jitter;
- a deliberate stall cannot slow world time silently or create a CPU spiral;
- restart resumes a moving unmanned ship within declared pose/velocity bounds;
- stale pre-restart input cannot overwrite the restored authority generation;
- old snapshots still load safely.

This phase precedes every richer force model because timing errors would make
later tuning and comparisons meaningless.

## Phase 2 - vector rigidbody shadow model

Add a deterministic shadow simulation without controlling live hulls:

- quaternion orientation and angular velocity;
- linear and angular force accumulators;
- engines and sails applied at their individual mount transforms;
- recovered relative-wind vector drag;
- recovered core-control torque (`axes * (0.5, 1, 0.5) * mass`);
- centre-of-mass and inertia approximation with provenance.

Acceptance compares current and shadow trajectories in the Inspector. Symmetric
engines must produce zero yaw; an off-centre engine must yaw in the correct
direction; mirrored sails must cancel torque; replay must be deterministic.
No stock-client change should be necessary because authority still emits 1130.

## Phase 3 - vector flight opt-in

Promote the shadow model behind a per-hull/test-world flag. Add recovered wings
(speed/orientation torque and reverse airbrake) and self-righting. Preserve a
fast rollback to the longitudinal evaluator.

Acceptance covers eight headings, forward/reverse, one through four sails,
asymmetric engines, wing authority from rest through 10 m/s, mass bands, pilot
dismount and two-player passenger coherence. Activation requires recorded
current-versus-vector telemetry, not subjective feel alone.

Engine power, sail power and reverse strength remain Wareborn tuning because
the retail server values are unrecoverable.

## Phase 4 - authentic lift, gravity and overload

Replace the million-kilogram placeholder lift with real sky-core capacity and
the recovered gravity compensation, overload sinking, vertical acceleration
and +/-2 m/s vertical cap. Audit every persisted production ship before
activation so existing constructions are not unexpectedly condemned.

Acceptance:

- under-capacity hull hovers;
- overloaded hull cannot climb and sinks predictably;
- fuel exhaustion disables engines, not sky-core lift or sails;
- served mass/lift components and admin telemetry equal authoritative state;
- a grandfather/migration policy exists for overweight legacy ships.

## Phase 5 - authoritative collision foundation

Implement conservative swept collision in this order:

1. hull versus island/terrain proxies;
2. hull versus hull;
3. mounted-part contact;
4. aboard-player coherence;
5. damage only after contact authority is trustworthy.

Run responses in shadow mode first. Acceptance includes high-speed tunnelling,
grazing contact, stable resting contact, two-ship impacts, determinism and
passengers remaining aboard. Retail damage arithmetic is unrecoverable, so any
damage curve is versioned Wareborn tuning. Client collision reports remain
diagnostic, never authoritative.

## Phase 6 - docking and shipyards

Replace radius-only snap semantics with explicit approaching, captured and
docked states. Freeze linear/angular velocity and interpolate authoritative pose
using the recovered retail lifecycle. Validate occupancy, ownership and
collision clearance; snapshot the state durably.

## Phase 7 - propulsion and fuel lifecycle

Make commanded hull throttle the single source for both thrust and fuel burn.
An unmanned ship with latched throttle must continue both moving and consuming
fuel. Empty fuel disables engines only. Persist generator/fuel state and add
individual engine spin state where the client can represent it.

Generator capacity `100` is recovered; consumption, transfer and engine-power
values remain tuning.

## Phase 8 - vector walls, storms and damage effects

After vector forces and collision exist, add wall gusts at position, torque,
yaw alignment, downward effects, lightning and damage. Preserve recovered
spatial bands (200 m full, 400 m fade and the documented visual ranges), while
labelling every lost force/damage magnitude as tuning.

## Phase 9 - scale, security and distributed readiness

- replace per-tick global ownership scans with reverse indexes and dirty updates;
- spatialize avatar relay and validate aboard-relative movement envelopes;
- expose per-domain fixed-step cost, catch-up pressure and replay hashes;
- load-test 5, 20, 50 and 100 active ships;
- prove one-ship snapshot handoff and stale-generation rejection;
- only then place a non-crewed ship or empty island on a remote worker.

Acceptance for remote work is no client disconnect, no dual authority, bounded
snapshot discontinuity and observable recovery after killing the worker.

## Delivery rules for every phase

1. Pure policy and mutation tests before service wiring.
2. Full Multiplayer and login/admin suites plus both Release builds.
3. Inspector telemetry must use the exact runtime evaluator.
4. Default-off or shadow rollout for behavior-changing physics.
5. No client manifest unless stock 1130/component presentation is insufficient.
6. Record live acceptance evidence and an explicit rollback before enabling the
   next dependent phase.

The immediate engineering target is **Phase 1**, while the immediate operator
target is completing the four-step Phase 0 live acceptance run.
