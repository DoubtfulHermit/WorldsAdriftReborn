# Retail flight reconstruction: all-track integration review

Status: complete locally on `integ/flight-program-consolidation`. Nothing in this
worktree has been pushed, merged to main, deployed, enabled, restarted, or added
to a client manifest.

The later merged/default-off state and the controlled production enablement
sequence are tracked in `flight-elastic-runtime-visual-rollout.md`. Its visual
tests are required in addition to the automated gates recorded here.

## Integrated dependency stack

The program starts from production planning head `11931bd` and preserves the
reviewed Wave 1 final head `fd5a21f`. Wave 2 and Wave 3 were based on earlier or
parallel Wave 1 heads, so neither branch was merged wholesale. Only their unique
commits were replayed by patch equivalence.

1. `e2bc98d` - world-boundary cadence hardening;
2. `25aa300` - fixed clock and durable moving-flight snapshots;
3. `40b5db4` - deterministic vector force/torque primitives;
4. `31db3a4`, `bda1f15`, `fd5a21f` - Wave 1 integration, mutation review,
   quaternion and part-kind hardening;
5. `0159c86` - authentic lift/gravity/overload shadow policy;
6. `bc1d65a` - deterministic collision shadow policy;
7. `b82368c` - authentic docking lifecycle policy;
8. `da109eb` - Wave 2 force/lift/collision integration review;
9. `80c19cd` - hull-authored fuel and per-generator lifecycle;
10. `515dccb` - deterministic vector wall/storm shadow policy;
11. `325eb8f` - bounded worker/scale foundations;
12. `fc02ef6` - Wave 3 cross-review;
13. `3c944db` - explicit default-OFF Track 7 rollout gate.

`ee37c55` records the intermediate Wave 1+2 review. No duplicate Track 3
primitive or parallel authority-generation type was introduced.

## All-nine authority and rollout audit

| Track | Integrated state | Merge-time behavior |
|---|---|---|
| World bounds/reference stepping | existing production authority | Bounds retain their independent deployed switch and legacy reference slicing. |
| Fixed clock/durable flight | runtime-capable | `WAREBORN_FLIGHT_FIXED_STEP` remains explicit `1` opt-in and default OFF. Snapshot read/write is gated with it. |
| Vector rigidbody/torque | PURE | No game service, packet, component, persistence, or clock calls it. |
| Lift/gravity/overload | PURE/SHADOW | Value-returning policy only; no authoritative hull motion. |
| Collision | PURE/SHADOW | Conservative swept contacts only; no response, damage, or live terrain geometry. |
| Docking | isolated policy/registry | No live construction, component write, durable docking DTO mutation, or gateway transaction. |
| Fuel lifecycle | runtime-capable | `WAREBORN_FUEL_HULL_DEMAND` is explicit opt-in and default OFF. OFF preserves pre-Track-7 fuel behavior and JSON shape. |
| Vector walls/storm/damage | PURE/SHADOW | Default tuning is inert; no live force, health, detach, component, or understorm wiring. |
| Performance/workers | prerequisite model | Inverse local membership index is live; worker protocol, migration, recovery and remote authority are not wired. |

Track 7 required one consolidation correction. Its reviewed code originally used
the historically default-ON fuel subsystem as its rollout boundary. The new
`WAREBORN_FUEL_HULL_DEMAND` gate owns only the new hull-demand, per-engine burn,
engine-only dry gate and durable per-generator lifecycle. Unset, false and invalid
values retain the old pilot mirror, one ship-level burn rate, run-dry throttle
clamp, full-on-restore registration, and omit null `GeneratorFuel` fields. The
persistence writer independently rejects calls while OFF. Existing
`WAREBORN_FUEL` and `WAREBORN_FUEL_GATES_THRUST` defaults were not changed.

The rollout flag is documented rather than added to the stats schema: adding a
new cross-process configuration projection solely during consolidation would
expand schema 18 without the required review. Operators must inspect the service
environment before Track 7 acceptance.

## Compatibility and ownership

- Schema 18 remains additive. The login allowlist projects `fixedClock`; Wave 2
  and Wave 3 add no stats fields.
- Old world JSON loads with flight and generator snapshots absent. Null generator
  snapshots are omitted on write, preserving the old serialized shape while Track
  7 is OFF. Explicit zero and valid version-1 generator snapshots round-trip when
  enabled; invalid/future values fail closed.
- Durable hull motion and generator fuel remain separate DTOs and stable keys.
- `ShipDomain` remains the sole live mutable flight and authority-generation
  owner. The worker protocol reuses the same `AuthorityGeneration` value but is
  not referenced from runtime startup or game services.
- No client packet/component shape, 1130 cadence, DLL, or manifest changes.

## Verification evidence

- All-track focused matrix: **290 passed, 0 failed**.
- Full multiplayer suite: **4,795 passed, 0 failed**.
- Full login/admin suite: **1,228 passed, 26 intentional database-dependent
  skips, 0 failed**.
- Multiplayer Release build: **0 errors** (8 existing warnings).
- Game-server Release build: **0 errors** (70 existing warnings).
- Login-server Release build: **0 errors** (16 existing warnings).
- `git diff --check`: clean.
- One definition each of `ShadowVector3`, `ShadowForceAccumulator`,
  `FixedFlightClock`, and `AuthorityGeneration`.
- Static runtime-reference audit found no call into vector collision, integrated
  vector motion, vector wall/damage, authentic docking, or worker recovery.
- Mutation probe changed the Track 7 blank-value result from OFF to ON: both the
  explicit-rollout and production-config tests failed. The mutation was restored
  before the clean runs.

The unreadable-JSON messages during tests are expected corruption-recovery
fixtures and affected only temporary test files. A parallel test build issued one
transient shared-output copy retry; it recovered and both suites passed, followed
by a clean sequential focused/build gate.

## Residual risks

- Fixed-step plus moving-snapshot restore has not passed the disposable-hull live
  restart acceptance and must remain OFF.
- Track 7 ON-mode has not passed its combined fixed-step/fuel restart acceptance;
  its tuning and engine-count effect can change range materially.
- Vector force geometry, lift, collision and docking remain shadow policies; AABB
  collision is too conservative for authority and docking lacks a domain-gateway
  transaction.
- Wall magnitudes, gust timing and all damage arithmetic are lost retail data.
  Any later non-zero policy is Wareborn tuning and needs durable deduplication.
- Worker contracts lack authenticated canonical transport, leases/consensus,
  durable result horizons, session rebinding, universal mutation routing and real
  performance evidence.
- The Track 7 rollout state is not yet in inspector telemetry; this is a required
  observability item before production enablement, not before a default-OFF merge.

## Exact verdict

**GO** for a later reviewed merge of this stack with:

- `WAREBORN_FLIGHT_FIXED_STEP` unset/OFF;
- `WAREBORN_FUEL_HULL_DEMAND` unset/OFF;
- vector motion, lift, collision, docking, wall force/damage and worker protocols
  remaining unwired;
- no client manifest change.

**NO-GO** for enabling fixed stepping or Track 7 until a backed-up disposable,
unoccupied hull proves motion/fuel persistence, authority rotation, neutral input,
stale-token rejection and clean rollback across restart.

**NO-GO** for live vector motion, collision response, docking writes, wall force or
damage until each downstream geometry/transaction/durability gate in the Wave 2
and Wave 3 reviews is closed.

**NO-GO** for any remote worker, migration, failover or multi-VPS authority claim
until every Track 9 gateway, transport, lease, session, snapshot and load-test gate
is satisfied. `local:primary` remains the only live authority.
