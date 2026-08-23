# Fixed simulation clock and durable flight snapshots

Status: independently integrated with Tracks 1 and 3 on
`integ/flight-wave1-crossreview`; not pushed, merged to main, deployed, or enabled.

## Period 1 — discovery

The game process drains ENet in one single-threaded poll loop. `Flight.Tick()` is
called every loop turn, but its `CadenceTimer` historically gated both physics and
publication. Therefore flight advanced once per stock `1130` publication interval
(0.24 seconds), not at a physics clock. The timer schedules on an ideal grid but,
after a stall longer than one interval, skips the missed publication and resets to
`now + 0.24s`; it never emits a burst. The old integrator still advanced one nominal
0.24-second slice, regardless of the actual stall.

Existing resumability had two different levels:

- `FlightSessionSnapshot` and `ShipDomainSnapshot` are pure, complete in-memory
  handoff objects carrying current motion, input, pilot binding, authority generation,
  members and aboard peers.
- `world-state.json` is an atomic, backward-compatible shared-world document. A
  built ship stored only Q52.12 position and yaw every two seconds. Dock linkage and
  each mounted sail's furl state were already additive fields. Pilot and aboard peer
  ids are connection-scoped and must not survive a process.
- Fuel is authoritative in `ShipFuelLedger`, per mounted generator, but the existing
  persistence contract explicitly does not save generator levels. Track 7 (fuel
  lifecycle) owns that per-part identity migration; this track does not flatten it
  into an incorrect hull-level number.

Retail evidence recovers Unity `FixedUpdate`-style 0.02-second evaluation (also the
world-edge reference step). The retail worker's backlog cap, stall policy, time origin,
snapshot wire/storage version, checkpoint interval, generator-fuel schema, and exact
crash-resume rules are lost. Wareborn's cap and persistence format are consequently
labelled Wareborn safety policy, not retail values.

## Period 2 — coding

- `FixedFlightClock` is a monotonic, deterministic 50 Hz accumulator.
- One hull may execute at most 25 catch-up steps (500 ms) per 0.24-second publish
  turn. Excess whole steps are dropped, never retained as a death spiral.
- Completed steps, dropped steps, pressure events and fractional remainder are
  exported per ship in schema 18 under `fixedClock` and logged on pressure.
- Physics stepping is separated from publication: `FlightSession.AdvanceFixed()`
  performs N 20 ms integrations and makes one stock-cadence emission decision.
- `WAREBORN_FLIGHT_FIXED_STEP=1` opts in to both the clock and durable moving-flight
  restore. The default path remains the previous one-call-per-0.24-second behavior,
  ignores any prior durable checkpoint, writes pose-only state and clears a stale
  checkpoint on its next persistence pass.
- `BuiltShipRecord.FlightSnapshot` is an additive version-1 checkpoint carrying every
  scalar represented by today's flight model (position, yaw, yaw rate, roll, pitch,
  speed command and XYZ velocity), all five held inputs, authority generation, and
  dock/sail/aboard/pilot lifecycle evidence. Old documents load with it null; old
  binaries ignore it and keep using pose+yaw.
- Periodic saves update the legacy pose and the new checkpoint in one atomic document
  replacement. A valid moving checkpoint wakes the flight service after boot.
- Restart never revives a pilot capability: pilot/aboard bindings are empty, controls
  are neutralized, and the persisted authority generation advances once. Momentum is
  retained so an undocked moving ship can safely coast from its last checkpoint.

This phase does not add vector 6DOF, torque, collisions, lift, docking physics or new
fuel behavior.

## Period 3 — review

The review checked:

- Determinism: fixed duration is an integer count of 20 ms steps; sub-step jitter is
  carried as a remainder. Weather samples receive deterministic step-end times.
- Time origins: process-monotonic elapsed time drives accumulation; Unix milliseconds
  remain only the client control-point timestamp.
- Stale/replayed authority: saved pilot ids are diagnostic only; the generation rotates
  before any new command can be accepted.
- Corrupt/partial/newer data: unknown versions, non-finite state, invalid counts and
  exhausted epochs fail closed to the legacy pose. Atomic JSON quarantines an unreadable
  whole document using its existing `.broken` mechanism.
- Stall safety: the backlog is consumed after the cap, not retained; pressure is visible.
- Rollback: legacy pose fields continue updating, durable restore is ignored while
  the switch is OFF, and the next pose-only write clears the old checkpoint so a
  later re-enable cannot resume stale momentum. Schema additions are optional.
- Concurrency: all mutation remains on the single poll loop; snapshots are written by
  the existing atomic writer.
- Lifecycle: dock capture writes a settled checkpoint; moving restore becomes active;
  pilot and aboard connections never resurrect.

Known dependency: exact per-generator fuel durability must be completed by the fuel
lifecycle track using stable part identities. Treating pooled hull fuel as durable here
would break retail's “fuel travels with the generator” rule.

## Period 4 — testing and non-production acceptance

Automated coverage includes jitter accumulation, exact step counts, deterministic
state hashes under different batching, deliberate one-second stalls, catch-up cap and
pressure counters, one publication for twelve physics steps, legacy JSON, unsupported
and non-finite snapshots, atomic round-trip, capture/destroy/restore/resume, authority
epoch rotation, stale-token rejection, schema-18 pressure serialization, and source
wiring/mutation guards.

Non-production restart acceptance procedure:

1. Back up `world-state.json`; use a disposable undocked ship and no passengers.
2. Start with `WAREBORN_FLIGHT_FIXED_STEP=1`; keep all vector/collision feature flags off.
3. Fly at moderate forward throttle for at least four seconds. Record hull id, position,
   velocity, authority generation, fixed-clock counters and current 1130 cadence.
4. Stop the process only after observing a fresh `FlightSnapshot` in the atomic file.
5. Restart without a client at the helm. Confirm the hull restores near the checkpoint,
   generation is exactly saved+1, pilot is null, input is neutral, and the moving hull
   coasts rather than teleporting or accepting the old command.
6. Reconnect, check out the ship, man the helm and test neutral/forward/idle/reverse.
   Confirm 1130 remains about 0.24 seconds and `droppedSteps=0` in normal operation.
7. On a disposable process inject a 1-second poll stall. Confirm at most 25 catch-up
   steps, a nonzero dropped count/pressure event, no packet burst and service recovery.
8. Restore the backup or disable `WAREBORN_FLIGHT_FIXED_STEP` for immediate rollback.

Production activation is intentionally not part of this changeset.

## First production acceptance failure and correction — 2026-08-22

The first Pack C activation failed its initial helm sweep on hull 3639. The
player observed discrete forward/reverse corrections and a vibrating, blurred
helm while changing throttle and yaw. Server evidence ruled out overload:
`droppedSteps=0`, `pressureEvents=0`, finite state, normal RTT, and coherent
domain membership.

The failure was the clock boundary itself. `ShipFlightService.Tick` returned
behind the 240 ms publication timer before calling `FixedFlightClock.Advance`.
Consequently the nominal 50 Hz clock executed about twelve 20 ms integrations
as one batch every 240 ms, using whichever control input was newest at the end
of that interval. An input change halfway through a publication window was
therefore applied retroactively to physics time that elapsed before the change.

The corrected service samples `FixedFlightClock` on every poll-loop turn and
advances any completed 20 ms steps immediately. `FlightSession.AdvanceFixed`
now accepts a separate `emitDue` decision: intermediate calls update only
authoritative state, while the independent stock timer still limits 1130 and
whole-domain publication to 240 ms. Membership refresh, docking capture and
helm echo remain publication-paced, so this correction does not turn the fixed
clock into a 50 Hz world scan.

Regression coverage pins both sides of the seam: intermediate steps move state
without emitting, and a forward-to-reverse input change inside one 240 ms
window produces a different state from the rejected retroactive twelve-step
batch. Production was rolled back to `WAREBORN_FLIGHT_FIXED_STEP=0` immediately
after the failed observation; reactivation requires the full test/build gates
and a repeat of C0 before any restart acceptance.

## Second production acceptance finding — first persistence write pressure, 2026-08-23

The unattended corrected-C0 run was visually smooth through forward/reverse,
yaw, climb/descent and a continuous cruise, but the acceptance gate still found
one fixed-clock pressure event: hull 3639 ran the 25-step catch-up cap and dropped
9 steps (about 180 ms). The journal fixes the event at 10:17:03, four seconds
after the first post-boot mounted-sail persistence mutation. The ship was parked
and unpiloted at the time; later flight remained finite and produced no second
event. This is not the rejected 240 ms physics batching bug.

The event followed the first post-boot mounted-sail persistence mutation, but
the save itself was not timed, so that correlation is not yet a root-cause
claim. The first diagnostic correction logs every real world save's caller and
elapsed time when it exceeds 100 ms. The instrumented production run must reproduce
the same first sail mutation; only direct timing evidence can decide whether
serializer/file-replace cold start, a scheduler pause or another poll-loop
operation caused the clock pressure. A persistence warm-up must not be merged as
a speculative fallback before that evidence exists.

Pack C remains **NO-GO** until an evidence-backed correction repeats the first
sail mutation and corrected C0 with `droppedSteps=0` and `pressureEvents=0`.
C3's deliberate stall is not allowed to hide or normalize this ordinary-load
event.

The instrumented clean-boot reproduction at 10:40 on the same production hull
completed the first sail mutation without a persistence warning (therefore in
less than 100 ms), and the fixed-clock counters remained at zero dropped steps
and zero pressure events. This falsifies a slow `AtomicJsonFile.Write` as the
cause of the earlier 680 ms gap; it does not make the intermittent gap disappear.
The next diagnostic boundary reports any server-loop turn over 100 ms split into
pre-flight timers, flight, post-flight services, ENet polling/packet handling and
spawn synchronization. It is observation only and stays silent on ordinary
turns. A repeat corrected-C0 run must either stay clean or name the responsible
stage before Pack C advances.

## Infrastructure root cause and corrected acceptance — 2026-08-23

The loop-stage diagnostic exposed an infrastructure incident rather than a
flight or persistence defect. An unauthorized Monero miner disguised as
`kmod-cache-update` had been running four threads inside the VPS Gitea container
since August 10, consuming about four CPU cores continuously. Four watchdog
shells kept it alive. Evidence was preserved under
`/opt/wareborn/backups/security-miner-20260823T085500Z`; the miner and watchdogs
were stopped and their directory was moved to the recoverable quarantine
`kmod-cache.quarantined-20260823T085500Z`. Idle CPU immediately recovered from
approximately zero to 93–95 percent. No cron persistence was found, but the Gitea
exposure and credentials still require a separate security audit.

After containment, corrected C0 on hull 3639 completed 19,877 fixed steps with
`droppedSteps=0` and `pressureEvents=0`. Forward/neutral/reverse input lifecycle,
yaw in both directions with recentering, climb/descent, a one-sail cruise and
clean settling all remained finite. The aim-independent combined sail-plus-turn
camera check is still marked **REAL EYES** because the local bridge could not
reliably reacquire a moving helm; it is not silently counted as automated proof.

C1 stationary restart then preserved exact XYZ/yaw, zero velocity, 16 decks, 10
mounted parts and two furled sails. The restored hull had no pilot or aboard
member until reconnect, and authority generation advanced exactly once from 17
to 18. The protected remembered login completed unattended after the acceptance
runner was corrected to wait five seconds on the landing screen before pressing
Play; pressing immediately can race character-list loading ahead of remembered
authentication.

The C2 moving restart used the native semantic sail interaction on hull 3639.
Immediately before restart its v1 checkpoint had both sails furled, no pilot,
neutral input, generation 18 and velocity `(1.050, 0, -1.304)` m/s (speed
1.674 m/s) at `(17398.391, -283.309, -1361.612)`. The backed-up source is
`/opt/wareborn/backups/pack-c2-moving-20260823T093803Z/world-state.json`. Boot
restored that exact checkpoint, advanced generation once to 19, kept aboard and
pilot state empty, and resumed the coast. Three seconds later it was at
`(17407.192, -283.309, -1372.550)` with speed 1.209 m/s, then settled normally
at `(17420.388, -283.309, -1388.948)`. C2 therefore passes the durable flight
contract. The player's stored logout point no longer lay inside the coasting
hull envelope, so reconnect truthfully fell back to Haven while the ship
continued separately; the rollout script explicitly says this is not yet a
guaranteed relative restore.

C3's controlled process suspension ran from `09:42:04.427Z` to
`09:42:05.443Z`. Recovery executed the cap of 25 catch-up steps, dropped 25
excess steps, recorded one pressure event, emitted no packet burst and retained
the connected peer. The camera was not aboard the ship after the documented C2
spawn split, so freeze/component visual quality remains **REAL EYES** rather
than an automated visual pass. Separately, ordinary reconnect work produced a
1.582-second `networkMs` turn and 54 dropped steps. That is not counted as the
controlled C3 proof and remains a real performance finding: the current network
bucket combines native poll wait and packet processing and needs finer staging.

Pack C is now **C0 PASS / C1 PASS / C2 PASS / C3 mechanical PASS with visual
REAL EYES / C4 pending**. Track 3–6 shadow evaluators are present, pure and
unwired, which proves zero gameplay effect but does not provide the scalar vs
vector/collision observations C4 asks Codex to compare. Do not silently call
that missing comparison a pass.
