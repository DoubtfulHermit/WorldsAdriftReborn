# Retail flight reconstruction program board

Status: active orchestration plan, 2026-08-22.

This board controls nine work tracks. Every track has four mandatory periods:
**Discovery**, **Coding**, **Review**, and **Testing**. A green branch is not a
deployment approval: prerequisite tracks, cross-review and live acceptance must
also be green.

## Dependency graph

```text
1 Bounds/stepping acceptance
            |
2 Fixed clock + durable snapshots
            |
3 Vector rigidbody shadow
       +----+---------+
       |              |
4 Lift/gravity     7 Fuel lifecycle
       |
5 Collisions
       |
6 Docking
       |
8 Vector walls/storms/damage
       |
9 Scale, hardening, remote workers
```

Discovery and pure-policy work may overlap. Live service wiring follows the
graph. Agents use separate worktrees and do not deploy, push, restart, or change
the client manifest without an integration gate.

## Track 1 - world bounds and deterministic reference stepping

### Discovery

- Reconfirm `WorldEdgePushback`, the 36 km MapFile, coordinate signs and all
  thresholds against schema-v17 production telemetry.
- Compare 20 ms subdivision with the legacy 240 ms integration across normal
  interior flight, wind, sails, engine and wall resistance.
- Specify disposable-hull horizontal/vertical live tests and rollback signals.

### Coding

- Correct only proven policy/integration discrepancies.
- Add bounded acceptance diagnostics and automation; no general clock, 6DOF or
  collision work.

### Review

- Audit NaN quarantine, clamp signs, threshold inclusivity, numeric stability,
  configuration fallback, telemetry truth and default-off behavior.

### Testing

- Pure boundary vectors, timing equivalence, invalid state and config tests;
  full suites/builds; live interior, horizontal edge, altitude and relog run.

Exit: Phase-0 live acceptance recorded and no unexplained interior-flight drift.

## Track 2 - fixed simulation clock and durable flight snapshots

### Discovery

- Inventory poll timing, 1130 emission, stalls, catch-up, every flight state
  owner, current persistence/snapshot formats, authority epochs and old records.
- Define restart semantics for piloted, unmanned-throttle, sail-only, docked and
  aboard-player cases.

### Coding

- Add a bounded deterministic 50 Hz accumulator while retaining 0.24 s network
  emission.
- Add catch-up pressure telemetry and a versioned additive durable flight
  snapshot; old records continue loading and stale pilot authority is revoked.

### Review

- Audit determinism, wall-clock/time-origin mistakes, catch-up death spirals,
  partial/corrupt writes, epoch replay, rollback and migration compatibility.

### Testing

- Jitter-equivalent replay hashes, deliberate stalls, catch-up caps, old/new/
  corrupt snapshots, capture-destroy-restore-resume and process-restart tests.

Exit: a moving unmanned hull survives a controlled restart within declared
pose/velocity tolerance without stale authority or duplicate entities.

## Track 3 - vector rigidbody flight, mount-position forces and torque

### Discovery

- Recover coordinate/force/torque equations and transform conventions from
  retail engine, sail, motion, control, wind, wing and self-righting code.
- Inventory mount transforms, centre-of-mass inputs and 1130 constraints;
  classify every surviving versus lost balance value.

### Coding

- First build a no-effect shadow evaluator: vector/quaternion state, linear and
  angular accumulators, force-at-position torque, mass/centre/inertia policy and
  current-versus-shadow telemetry.
- Later, after Track 2, add opt-in live authority with instant scalar rollback.

### Review

- Audit handedness, units, torque sign, symmetry, invalid/malicious transforms,
  numeric stability, determinism, part-count caps and tuning labels.

### Testing

- Symmetric/off-centre engines, mirrored sails, eight headings, replay hashes,
  extreme mass/geometry and performance; recorded shadow-versus-live matrix.

Exit: opt-in vector flight passes two-player passenger and control acceptance,
with stock 1130 clients and a tested scalar fallback.

## Track 4 - authentic lift, gravity and overload

### Discovery

- Recover lift/gravity compensation, vertical acceleration/caps and overload
  behavior; inventory sky-core capacities and every persisted production ship.
- Separate recovered structure from lost core balance data.

### Coding

- Replace placeholder lift in shadow mode, serve one authoritative mass/lift
  policy, and add a legacy-ship grandfather/migration mechanism.

### Review

- Audit overweight spawn/restart safety, fuel independence, negative/invalid
  capacity, part mutation invalidation and admin/component parity.

### Testing

- Under/at/over-capacity matrices, climb/hover/sink, fuel-empty lift, core
  detach, restart and every live production hull preflight.

Exit: no existing ship is silently destroyed or trapped when authentic lift is
enabled, and overload behavior is observable and reversible.

## Track 5 - authoritative collisions

### Discovery

- Catalogue available island hull/surface proxies, ship/part bounds, velocities,
  client collision reports and lost server damage behavior.
- Select deterministic broadphase and conservative swept-contact representations.

### Coding

- Shadow contacts first, then gated responses in order: terrain, ship, mounted
  part, aboard player; damage remains a later tunable policy.

### Review

- Audit tunnelling, false positives, stable resting contact, energy injection,
  griefing, client-report trust, complexity and deterministic ordering.

### Testing

- 60 m/s sweeps, grazing/resting contacts, two-ship impacts, island seams,
  passengers, replay determinism and adversarial packet cases.

Exit: authoritative motion cannot cross tested world geometry and two hulls
resolve contact without divergence or passenger loss.

## Track 6 - authentic docking

### Discovery

- Recover retail approach/capture/freeze/interpolation lifecycle and inventory
  current yard occupancy, persistence, ownership and sail-only departure seams.

### Coding

- Add approaching/captured/docked states, velocity freeze, authoritative pose
  interpolation, collision clearance, occupancy and durable snapshot fields.

### Review

- Audit double capture, stale yard links, owner/crew permission, disconnect,
  restart, deletion, departure and collision-order races.

### Testing

- Approach angles/speeds, occupied yard, restart docked, sail/engine departure,
  concurrent claims, destroyed yard and two-client observation.

Exit: docking never duplicates occupancy, teleports through collision or leaves
permanent links after departure.

## Track 7 - correct fuel and propulsion lifecycle

### Discovery

- Trace hull throttle as the true propulsion command, generator inventory,
  persistence and retail capacity/spin evidence; label lost consumption rates.

### Coding

- Drive thrust and burn from one hull command whether piloted or not; persist
  fuel/generator state; empty fuel disables engines only; add individual engine
  state where stock components support it.

### Review

- Audit free unmanned thrust, reman/delta suppression, negative fuel, duplicate
  generators, disconnect/restart, transfer abuse and tuning provenance.

### Testing

- Throttle-dismount burn, reman without delta, empty cut-off, sails/lift with no
  fuel, restart, multiple engines/generators and long-duration accounting.

Exit: no engine thrust occurs without matching authoritative fuel accounting
when fuel gating is enabled.

## Track 8 - vector wind-wall, storm and damage effects

### Discovery

- Recover wall bands, force directions, visual/physical ranges, storm cadence,
  lightning signals and every lost magnitude; map interactions with collisions.

### Coding

- After Tracks 3 and 5, add force-at-position gusts, torque/yaw alignment,
  downward effects and server-authored lightning/contact damage behind per-type
  tuning flags.

### Review

- Audit discontinuities at bands, frame-rate dependence, wall tunnelling,
  stacked segments, mass attenuation, safe tuning limits and client visual truth.

### Testing

- Cross each wall type at headings/masses/speeds, hover on boundaries, stacked
  influence, storm transitions, lightning authority and rollback mid-crossing.

Exit: visual, telemetry and felt wall state agree, with every invented magnitude
shown as Wareborn tuning.

## Track 9 - performance, multiplayer hardening and remote workers

### Discovery

- Profile ownership membership scans, avatar relay, physics step cost, interest,
  snapshot size, gateway commands and every cross-domain interaction.
- Threat-model worker loss, split brain, stale generations and malicious clients.

### Coding

- Add reverse indexes/dirty updates, spatial avatar relay, physics budgets/replay
  hashes, in-process gateway completion, committed snapshots, then one test
  remote worker for an empty island or non-crewed ship.

### Review

- Audit dual authority, snapshot tampering, epoch rollover, retry/idempotency,
  ordering, overload isolation, network partitions and rollback to local host.

### Testing

- 5/20/50/100 active-ship load tiers; hostile input/replay; worker kill -9,
  restore/takeover, partition/heal and cross-worker interaction without client
  disconnect or duplicate authority.

Exit: worker loss is visible and recovered with a bounded discontinuity, no
dual authority and no client reconnect.

## Integration gate

For every track the orchestrator records: evidence classification, commit(s),
focused/full test counts, independent review findings, client/manifest impact,
deployment flag, rollback, production acceptance evidence and prerequisite
status. Only then can the next dependent live wiring proceed.
