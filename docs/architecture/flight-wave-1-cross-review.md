# Flight reconstruction wave 1: integration and cross-review

Status: complete locally on `integ/flight-wave1-crossreview` from `origin/main`
`11931bd`. Nothing in this branch has been pushed, merged to main, deployed,
enabled, restarted or added to a client manifest.

## Reviewed inputs

- Track 1 `b0f2c08`: world-boundary cadence safety, parked recovery and transition
  journals.
- Track 2 `53d3b56`: fixed accumulator, durable scalar-flight snapshot and schema 18.
- Track 3 `5dcd7d2`: pure vector force/torque shadow primitives.

The three discovery/review records were read completely before integration.

## Conflict decisions

Track 1 and Track 2 both changed `FlightSession` and `ShipFlightService`. They were
resolved by assigning exactly one owner to each level:

1. `FixedFlightClock` is the only outer 20 ms accumulator and owns the 25-step
   stall cap.
2. With fixed stepping ON, each authoritative 20 ms step integrates once and the
   boundary policy evaluates that candidate once. There is no nested 12-way loop.
3. With fixed stepping OFF and bounds ON, the legacy 240 ms call keeps Track 1's
   twelve 20 ms reference slices. Bounds OFF retains the original single 240 ms
   integration.
4. Boundary telemetry aggregates all fixed steps in the one publication batch;
   invalid-state quarantine ends that batch rather than resuming from the safe
   anchor inside the same cadence.
5. Track 1's parked-hull wake and edge-triggered journal state were preserved, and
   both fixed-clock and boundary state are retired with a hull.
6. Track 3 cherry-picked without a runtime conflict and remains dependency-free
   value-returning code. No game service, clock, persistence, configuration or 1130
   writer references it.

## Independent review findings corrected

- Durable snapshots were initially read and written even while the fixed-step
  switch was OFF. Restore/write are now gated by `WAREBORN_FLIGHT_FIXED_STEP`; an
  OFF-mode pose write clears a stale checkpoint. This makes operational rollback
  truthful and prevents a later re-enable from reviving old velocity.
- Schema 18's per-ship `fixedClock` block was emitted by the game server but removed
  by the login server's allowlist. It is now explicitly projected with numeric
  bounds and unknown fields are discarded.
- An authority generation of `long.MaxValue - 1` could restore to the final epoch
  and then throw on the first pilot handoff. Both the exhausted and nearly exhausted
  values now fail closed to the legacy pose seam.
- Fixed-step boundary telemetry previously exposed only the twelfth step and a
  quarantine could resume during the same batch. It now aggregates the cadence and
  stops at the corrupt step.
- Direct `AdvanceFixed` callers could request an unbounded loop despite the service
  clock's cap. The session now rejects counts outside 0..25.

## State, compatibility and client review

- The ship domain remains the sole mutable flight/authority owner on the existing
  single poll thread.
- Process restart never restores a pilot, aboard peers or held control authority.
  Input is neutralized and the authority epoch advances before a command can be
  accepted.
- Durable snapshot v1 is additive. Old JSON loads with it absent; unsupported,
  corrupt, non-finite and epoch-exhausted data falls back to Q52.12 pose/yaw.
- The legacy pose remains updated atomically alongside an enabled checkpoint.
- Schema advances 17 to 18 additively. Bounds fields retain their meaning and the
  fixed-clock block is explicit when disabled or absent.
- All new authoritative behavior defaults OFF. Bounds retain their independent
  existing switch. Vector primitives have no switch because they have no runtime
  wiring or side effect.
- No packet/component format, 1130 cadence, game-client DLL or manifest changes.

## Combined acceptance gates

Automated gates cover all four bounds/fixed-step OFF/ON combinations, legacy versus
fixed integration ownership, twelve aggregated boundary evaluations, parked recovery,
invalid quarantine, zero-step sail wake, jitter, stall dropping, one emission decision
per batch, moving snapshot restore, authority rotation, stale-token rejection,
schema projection, vector determinism and vector input ceilings.

Live acceptance remains required before enabling fixed stepping: disposable undocked
hull, no passengers, normal 240 ms publication, no dropped steps under ordinary load,
captured moving checkpoint, clean restart/coast, old-token rejection, one injected
one-second stall and bounds entry/recovery evidence. Track 3 must remain unwired during
that acceptance.

## Residual risks and verdict

- Enabling fixed stepping changes scalar integrator feel from one 240 ms evaluation
  to twelve 20 ms evaluations when bounds are OFF; this is intentional, opt-in and
  requires live calibration.
- A two-second checkpoint can lose up to two seconds of motion on crash. Exact retail
  crash semantics are not recoverable.
- Per-generator fuel is deliberately absent until Track 7 supplies stable part
  identities.
- Track 3 sail yaw, centre of mass and inertia remain labelled approximations and
  cannot be promoted directly to authority.

Verdict: **GO for later controlled non-production/default-OFF merge and disposable-hull
acceptance; NO-GO for enabling fixed stepping in production or wiring vector shadow
into gameplay until that evidence is captured and reviewed.**
