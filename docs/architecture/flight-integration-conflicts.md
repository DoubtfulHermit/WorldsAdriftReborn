# Flight-Runtime Program — Step 6 Integration Record

Integration branch `integ/flight-runtime-program` from main @ `6d6478a`, combining:
`feat/flight-contracts` (1 commit), `feat/truthful-part-mass` (4), `feat/vector-lift-runtime`
(14, skipping its 3 patch-equivalent duplicates `cec2819`/`5439246`/`bf369b8`), and
`feat/collision-docking-runtime` (8, skipping duplicate `d0565a2`). 27 commits cherry-picked
in the brief's dependency order, followed by the semantic integration commits below.

## Textual conflicts and their resolutions

### 1. `ShipMassEvaluator.cs` — the cross-branch fingerprint conflict (pick `978ed7b`)

Both branches rewrote `FingerprintOf`: Step 1's fix `ab53a65` canonicalised the hash order
(build per-part canonical strings, `Array.Sort` ordinal, then hash — restart-stable), while
the vector branch's `978ed7b` (based on a tree without that fix) folded `PrefabEvidence`
into an *unsorted* canonical string.

**Resolution:** both, exactly as the brief demands — `PrefabEvidence` is now a field of the
per-part canonical string (`part:StablePartKey:MaterialEvidence:PrefabEvidence:MassKg:Provenance`)
and those strings are still `Array.Sort`ed ordinally before hashing. The doc comment lists the
resulting sort key. Both the permuted-remint test and the prefab-only-core test pass; mutation
M8 (drop the sort) dies against `Reminted_ids_in_a_permuted_mount_order_keep_the_fingerprint...`.

### 2. `StatsSnapshot.cs` — both branches extended `ShipDomainStat` (pick `d51ae0a`)

Vector branch added `VectorAuthorityStat` (+`VectorShadowComparison`); collision branch added
`FlightCollisionDockingStat`. Three hunks plus the two struct bodies sharing a closing brace.

**Resolution:** union. `ShipDomainStat` carries both stats; the constructor takes both as
trailing optional parameters (`vectorAuthority` then `collisionDocking`); both struct
definitions kept whole.

### 3. `ShipFlightService.cs` — field block, RegisterHull, RetireHull (picks `d51ae0a`, `bbab1cb`)

- Field block: vector branch's adapter/reseed/restore/half-extents dictionaries vs collision
  branch's `_dockingDriver`. **Union — both kept.**
- `RegisterHull`: vector's parked `_pendingVectorRestore` (gated on `IsPromoted` +
  `!durable.WasDocked`) vs docking's `_dockingDriver.Restore` (gated on `DockingTxnEnabled` +
  snapshot + resolved yard). **Union — both blocks, independent gates.**
- `RetireHull`: vector's five-dictionary cleanup + `ShipMassSnapshots.Retire` vs `bbab1cb`'s
  generation-stamped `_dockingDriver.Retire(hullEntityId, authorityGeneration)`.
  **Union — all cleanups plus the stamped retire (the generation read stays at the top of the
  method, before the domain is removed).**

### 4. `WorldsAdriftRebornGameServer.cs` stat ctor (~:2700)

Both branches appended their stat argument. **Resolution:** pass both,
`VectorAuthorityStatFor` then `CollisionDockingStatFor`, matching the ctor parameter order.

## Semantic integration items (brief §"Semantic integration work")

1. **FlightRuntimeFlags collision** (`1757bc1`): the game-assembly
   `internal static FlightRuntimeFlags` (env reads + Console warnings in a static ctor that
   read `ShipFlightService.FixedStepEnabled` from static init) was deleted. The Multiplayer
   sealed `FlightRuntimeFlags.Parse` now decides all six gates
   (VECTOR_AUTHORITY, VECTOR_HULLS, LIFT_RUNTIME, COLLISION_OBSERVE, COLLISION_RESPONSE,
   DOCKING_TXN) with the dependency chains as tested data
   (TXN ⇒ OBSERVE ⇒ FIXED_STEP; LIFT ⇒ VECTOR ⇒ FIXED_STEP+FORCES). Warnings are logged once
   from the service constructor; every gate's live value prints at startup and is visible in
   the admin stat. The immutability pin test covers the new gates automatically.
2. **Single stamp minter** (`209de60`): `ShipDockingRuntimeDriver.ObserveAfterSlice` no longer
   constructs its stamp from `CompletedSteps` + `domain.Generation`; the service commits each
   observed slice through the hull's `FlightAuthorityAdapter` (pure-scalar hulls commit their
   committed session state through the same minter) and the driver receives
   `LastStamp`/`CurrentPose`. A slice with no honestly committed frame yields no observation
   (fail closed). The docking scan's observed pose still reads `session.State`, which for
   promoted hulls is definitionally the adopted projection of the committed pose
   (`AdvanceAdopted`, pinned by `FlightSessionAdoptedTests`) — extracting yaw from the pose
   quaternion instead would have added a second conversion.
3. **Collision proxy mass** (`209de60`): `ShipMassSnapshots.For(hull).TotalFlightMassKg`
   (cache hit on the same snapshot the propulsion build used), replacing a second
   `PropulsionFor` evaluation and its 1 kg fallback when the force model is off.
4. **Docking × vector reseed** (`dba2da4`): the transactional freeze path
   (`RunDockingScan`, `FreezeVelocity`) now requests `_vectorReseedRequested` exactly like
   legacy `DockAt`/`EmergencyStop`. The `WasDocked ⇒ restore-at-rest / no stale vector
   restore` rule holds without new code: the transaction claims through the same
   `ShipDockRegistry.Shared` that `BuiltShips.IsHullDocked` (the `WasDocked` capture source)
   reads. Pinned by `FlightRuntimeIntegrationWiringTests`.
5. **Per-slice vs per-step collision** (`0b82f37`): DECIDED — keep per-slice observe-only for
   Step 6 (routing proxies through `IntegratedFlightShadow`'s per-step seam at 50 Hz is a
   Step-7-sized change). The gap is hard-encoded:
   `FlightRuntimeFlags.PerStepCollisionPathExists = false` (code), a startup warning whenever
   RESPONSE=1 rides over it, and `FlightCollisionDockingStat.PerStepEvaluation` (admin).
   Response stays observe-graded; the geometry gate independently rejects every
   `ConservativeEnvelope` subject.
6. **Rest snap vs docking capture** (`67851be`):
   `Vector_rest_snap_never_fights_docking_capture_or_departure` drives the production
   `VectorFlightRuntime` into the production `DockingRuntime`: the snap (0.01 m/s) leaves the
   capture band (≤2 m/s) real motion alone, holds exact rest after the freeze+reseed, and
   never pins a hull with live departure propulsion; occupancy holds until the release
   envelope clears.
7. **Wiring-needle preservation**: all `ShipMassSnapshotWiringTests` needles survive
   (`ShipMassSnapshots.For(hullEntityId).TotalFlightMassKg`, `HullStructuralMassKg`, policy
   needles, invalidation hooks) — verified green in the combined suite.
8. **Flag-immutability pin**: the STATIC READONLY IS LOAD-BEARING comments on both the field
   and the type survived the merge verbatim;
   `Mode_flips_require_a_restart_because_the_parsed_flags_are_immutable` now also guards the
   three collision/docking gate properties.

## Deliberate conservative choices

- `ObserveCollisionAfterSlice` requires the adapter's stamp to equal the observed slice's end
  step in the current generation; anything else skips the observation entirely (no clearance)
  rather than observing under an older stamp.
- The extra startup `[info]` lines (flag values) are treated as sanctioned flag-visibility
  telemetry (contract §7 requires every gate's live value be operator-visible).

## Mutation log (each mutation applied, targeted tests run, reverted; tree verified clean)

| Mutation | What was mutated | Result — killer test(s) |
|---|---|---|
| M1 double gravity | second `gravity*dt` term on the seam's vertical velocity (`IntegratedFlightShadow.TryStep`) | KILLED (6 fail) — `Vector_vertical_force_and_lift_apply_gravity_exactly_once`, `A_coreless_hull_falls_under_exactly_one_gravity`, rest-snap gates |
| M2 second pose source | `FlightSession.AdvanceAdopted` stops adopting the vector projection | KILLED (4) — `FlightSessionAdoptedTests`, `Publication_carries_only_..._the_adapters_pose` |
| M3a default-ON VECTOR_AUTHORITY | `== "1"` → `!= "0"` | KILLED (7) — `Everything_defaults_off_with_no_warnings` et al. |
| M3b default-ON VECTOR_HULLS (all promoted) | `IsPromoted` ignores the index set | KILLED (4) — `Master_with_prerequisites_enables_observer_phase_with_no_promoted_hull` et al. |
| M3c default-ON LIFT_RUNTIME | `== "1"` → `!= "0"` | KILLED (11) |
| M3d default-ON COLLISION_OBSERVE | same | KILLED (4) — `Collision_and_docking_gates_default_off...` |
| M3e default-ON COLLISION_RESPONSE | same | KILLED (10) |
| M3f default-ON DOCKING_TXN | same | KILLED (10) |
| M4a accept replayed step | `SupersedesWithinGeneration`: `>` → `>=` | KILLED (5) — stamp tests, docking replay tests, the one-stamp chain gate |
| M4b accept any generation (docking) | `StampAcceptable` → `stamped.IsValid` only | KILLED (3) — `Duplicate_stale_and_foreign_generation_frames_fail_closed` et al. |
| M5a dropped proxy counts as complete | drop `RejectedProxyCount == 0` from `ObservationRan` | KILLED (1) — `Dropped_subject_hull_proxy_never_yields_a_clear_clearance` |
| M5b truncated clearance record clear | drop `EvaluationComplete` from `CollisionClearanceRecord.IsClear` | KILLED (4) — `Truncated_collision_work_can_never_issue_clearance` et al. |
| M6 legacy SetDocked overwrite undetected | delete the lifecycle's stale-claim check | KILLED (1) — `Legacy_SetDocked_overwrite_is_detected_as_stale_and_fails_closed` |
| M7 admin re-evaluates | `FlightStatFor` stops reading `LastForceEvaluation` | KILLED (1) — `FlightForceModelWiringTests` needle |
| M8 fingerprint order broken | delete `Array.Sort` in `FingerprintOf` | KILLED (1) — permuted-remint test |
| M9a wrong vector step numbers | glue commits slice-end for every step | KILLED (1) — wiring needle (game assembly untested → text-scan needle per brief) |
| M9b reseed hook deleted | delete the docking-freeze `_vectorReseedRequested.Add` | KILLED (1) — wiring needle |
| M9c adapter ignores parked restore | `AdapterFor` consumes a fresh dictionary instead | first SURVIVED (needle was satisfiable by `RetireHull`'s cleanup `Remove`), needle scoped to `AdapterFor`'s two-arg consume, then KILLED (1) |

`ShipLiftPlans` grandfather `false→true` skipped per the brief (already `true` by design —
grandfather-all seam, deferred list). Known text-scan limitation, documented: a needle proves
the call *text* exists; a semantics-preserving sabotage (e.g. `false && ...` around a needle
line) is beyond a source scan — the brief's sanctioned alternative for logic that matters is
moving it into Multiplayer, which is where every actual decision already lives.

## Defect found by the combined OFF-path check

`BuiltShipRecord.DockingSnapshot` landed from the collision/docking branch WITHOUT
`JsonIgnore(WhenWritingNull)` (the vector extension had it), and `AtomicJsonFile` does not
omit nulls globally — so a world state written with every gate off would have gained
`"DockingSnapshot": null` on every built ship, a production-path byte diff against main.
Fixed (`419d2ad`) and pinned by
`A_gates_off_built_ship_record_serializes_with_neither_extension`, which serializes a
gates-off record and asserts NEITHER nullable extension property appears (verified to fail
without the attribute).

## Defense-in-depth note

Truncated-terrain honesty is enforced at three independent layers
(`HullCollisionObserver.Observe` forces observation off, `ObservationRan` checks
`Terrain.EvaluationComplete`, `CollisionClearanceRecord.From/IsClear` refuse incomplete
batches). A mutation of any single layer alone can be masked by the other two; M5a/M5b killed
the two layers that are singly load-bearing.

## Deferred by design (unchanged from the brief — NOT addressed in Step 6)

- Grandfather-all lift seam (needs a persisted build epoch).
- Abandoned-sinking 24 h accumulator (glue passes `false`).
- World bounds through the vector runtime (startup warning exists).
- 1258 live re-push on mid-flight capacity change (converges on checkout).
- Per-wing Power/AirBrake (lost retail data; refused, not invented).
- 1113/1106/1222 component serving (audited; 1124 wing torque law runs server-side).
- Live docking near islands fails closed under conservative envelopes (Step 7 decision).
- Old-binary re-save drops new snapshot fields on rollback (degraded-not-corrupt; Step 7
  runbook line).
- Per-step proposed-motion collision evaluation (this branch encodes it as a hard response
  prerequisite; see semantic item 5).
