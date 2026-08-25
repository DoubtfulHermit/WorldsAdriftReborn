# Turn vibration: the helm and mounted parts shake while the ship yaws

Investigation date: 2026-08-25. Branch `fix/turn-vibration-rootcause`, based on
`integ/flight-runtime-program` @ `5832150`.

## The symptom, restated precisely

Steering is latched: press `A`, the bar holds its angle after release, the ship
keeps turning until `D` counters it. That latch is correct retail behaviour and
is preserved. **While the ship is turning**, the helm and every mounted
component visibly vibrate and re-snap, as if client and server were fighting
over their positions. Straight flight is clean. Three prior corrections reduced
this but did not remove it.

## Root cause

The legacy 1130 publisher advances **exactly one 240 ms step of simulation per
emitted control point**, and then stamps that point at **wall clock** whenever
the poll loop happened to be late. Because a control point carries linear
velocity but **no angular velocity**, the client can hermite-ease an uneven
interval out of the position path but can only slerp the attitude across the raw
timestamp gap. The rendered turn rate is therefore

```
renderedRate = commandedRate * 240 / stampDelta
```

and `stampDelta` varies by the full poll jitter on every single point. In
straight flight the attitude delta is zero and the error is unobservable. In a
sustained turn it is a ~4 Hz rate wobble on the hull's attitude, amplified into
visible lateral motion at each mounted part by its lever arm from the hull's
rotation axis - which is exactly why the helm and mounted parts show it far more
than the hull's own origin does.

### The evidence chain

**1. The legacy path integrates a constant step.**
`FlightSession.Advance` delegates to `AdvanceFixed` with `fixedStepCount: 1` and
`fixedStepSeconds: stepSeconds`:

- `WorldsAdriftRebornGameServer.Multiplayer/Ship/Flight/FlightSession.cs:226-241`
- the loop that consumes it: `FlightSession.cs:293-313` (`IntegrateForDuration`
  once, for the whole `fixedStepSeconds`)
- `IntegrateForDuration` at `FlightSession.cs:438-453` calls
  `FlightIntegrator.StepEvaluated(_state, _input, durationSeconds, ...)` once.
  With world bounds on it substeps, but the substeps total the same duration
  (`FlightSession.cs:476-509`).

`stepSeconds` is `ShipMotionPolicy.SendIntervalSeconds` = **0.24 s**
(`WorldsAdriftRebornGameServer.Multiplayer/ShipMotion.cs:123`), passed at
`WorldsAdriftRebornGameServer/Game/ShipFlightService.cs:1177-1180`.

So the simulated interval represented by every legacy point is exactly 240 ms,
always. A pure test pins this:
`FlightStampContinuityWiringTests.Simulated_yaw_advances_by_the_same_amount_on_every_point`.

**2. The stamp is wall clock whenever the poll was late.** The historic rule,
before this branch:

```csharp
long stamp = _everEmitted && (phaseLocked || nowMs < _lastStampMs + stepMs)
    ? _lastStampMs + stepMs
    : nowMs;
```

`FlightSession.cs:544-558` after this change; the quoted form is what stood there before it. `phaseLocked` is false for the legacy
caller - the comment on that branch said so in as many words: *"Legacy callers
intentionally retain the old wall-clock behavior."*

**3. The poll loop is late by a varying amount on nearly every point.**
`_cadence` is a 0.24 s `CadenceTimer` (`ShipFlightService.cs:284`, gate at
`:1061`). `CadenceTimer.Due` is drift-free - it advances `_nextDue` by a fixed
interval (`WorldsAdriftRebornGameServer.Multiplayer/RelayCadence.cs:156-179`) -
so the *ideal* fire times are exactly 240 ms apart, but each actual fire lands on
the next turn of the ENet loop. That loop *"turns once per EVENT"* and otherwise
blocks for its 50 ms poll timeout
(`WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.cs:370-379`). Poll
jitter of tens of milliseconds is therefore the normal case, not an exception.

With jitter `j`, the emitted stamp is `ideal_k + max(j_k, j_{k-1})`, so
`stampDelta = 240 + max(j_k, j_{k-1}) - max(j_{k-1}, j_{k-2})` while the
simulated delta stays 240. A 50 ms poll window gives a rendered rate between 83%
and 100% of commanded, alternating point to point.

**4. Rotation has no derivative on the wire, position does.** Server side,
`FlightIntegrator.ToControlPoint` builds
`ShipControlPointSpec(timestampMs, X, Y, Z, VxMps, VyMps, VzMps, IsAtRest)` -
`WorldsAdriftRebornGameServer.Multiplayer/Ship/Flight/FlightIntegrator.cs:324-329`.
The attitude leaves separately as a packed quaternion,
`FlightIntegrator.PackedRotation` (`:338-342`), with **no angular velocity
field**.

Client side (decompile of
`StrippedAndPublicizedAssemblies/UnityClient@Windows_Data/Managed/Assembly-CSharp.dll`;
assemblies are stripped, so this is type/field evidence, never a method body):

```csharp
struct Bossa.DeadReckoning.ControlPoint {
    double   Timestamp;
    Vector3d Position;
    Vector3d Velocity;
    int      FsimIdHash;
    bool     Received;
    [JsonIgnore] Quaternion Rotation;
}
```

and `SplineInterpolator.CubicHermiteInterpolation` takes **position and velocity
only** - `Rotation` is not part of the spline signature. So the position path has
real endpoint tangents and absorbs an uneven interval as a slightly eased curve;
the attitude path has only two endpoint rotations and a gap to divide by.

**5. Why the mounted parts show it more than the hull.** The hull's rendered
pose comes from `PathFollower.Move` on its kinematic rigidbody. A mounted `"~"`
part is composed by `FixedUpdateLerpLocalTransformBehaviour`, whose cached
`IFixedUpdateNonAuthoritativeMode` is the hull's `SSPDeadReckoningVisualizer`
(the interface's **only** implementer in the assembly), exposing
`NextFramePosition` / `NextFrameRotation`. A rate error in the hull attitude is
therefore multiplied by each part's offset from the rotation axis before it
reaches the eye, and the two consumers sample the same wobbling spline through
different code paths. Both are zero-error in straight flight.

**6. The already-shipped fix for this exists but is inert in production.**
`docs/architecture/flight-publication-phase-lock.md` records the same class of
defect measured live: *"nominal 240 ms turn points contained 11, 12, or 13
completed 20 ms physics steps ... producing alternating measured turn rates
around 18-23 degrees/second for a nominal 20 degrees/second command"*. The
correction was implemented **only** in the fixed-step publisher
(`ShipFlightService.cs:1113-1173`, `phaseLockedEmit: true`), and
`docs/architecture/fixed-clock-durable-flight-snapshots.md:142` records
*"Production was rolled back to `WAREBORN_FLIGHT_FIXED_STEP=0`"*. The live
launcher `~/Games/WAReborn-servers/run-gameserver.sh` sets **no `WAREBORN_*`
variables at all**, so every flight the user tests runs the legacy wall-clock
publisher with the phase lock switched off.

## Why the three prior fixes reduced but did not eliminate it

They each removed a genuine, separate amplifier and left the driver untouched.

| Prior fix | What it removed | Why the vibration survived |
|---|---|---|
| `fix/flight-turn-and-settle-jitter` (`d7cf547`, `eeb0862`) | a resting heartbeat that revived stale PathFollower velocity, plus the mounted-follower sleep tail after rest | both are **rest-phase** defects. Neither touches a moving hull's stamp. |
| `fix/instrument-pipe-attachment` (`40fab6e`) | instruments not mounting on bar pipes | a placement/catalogue fix; no motion path involved. |
| `fix/instrument-turn-vibration` (`5b21d27`) | the five flight instruments' `"~"` follower composition, by making them real Unity children of the hull | this removed the client's **per-part** quantization for those five parts only. The helm, engines, sails, wings and generators are deliberately excluded (`MountedPartHierarchy.cs:49-63`) because a real parent can destroy their rigidbody. So exactly the set the user still sees shaking is the set that fix did not cover - **and the underlying hull attitude wobble was never addressed at all**, which is why even the newly-parented instruments only got quieter rather than still. |

The 24 August deep dive
(`docs/architecture/ship-presentation-authority-deep-dive-2026-08-24.md:60-77`)
also concluded from a live probe that *"active motion is not two server/client
authorities fighting over the mounted entity roots. They follow the rendered hull
to about one millimetre."* That measurement was of **position**, in metres. A
pure attitude-rate error contributes ~0 m at the entity root and is fully
consistent with a millimetre-tight position match - which is why that probe
correctly cleared the hypothesis it was testing and could not see this one.

## Eliminated hypotheses

Recorded because a dead hypothesis is worth as much as the live one.

**H1 - mounted parts are both parented client-side and receiving server world
poses.** ELIMINATED. Active flight publishes exactly one hull pose authority:
`ShipFlightService.cs:1282` passes `rootAuxiliary: null` with the comment
recording that two root authorities *"visibly fight during turns"*. The hull's
own 190602 is sent only on a mount commit (`PartMountService.cs:383`) and the
three admin paths (`ShipFlightService.cs:663/776/823`), never in the tick. Per
part, the only pose on the wire is the hull-local 190602 wake; there is no
per-part world pose anywhere, and `ShipReplicationCursor.RootTargetsHull`
(`Multiplayer/Ship/Domains/ShipReplicationCursor.cs:53-56`) makes a per-part 1130
structurally impossible. This was the single-hull-pose-authority correction and
it is still in force.

**H2 - an interest checkout re-seeds parts mid-flight.** ELIMINATED. A crewed or
piloted hull is force-loaded for every peer regardless of distance
(`ShipDomainInterestService.cs:307-310`, `ShipDomainInterestPolicy.cs:33-42`), and
a client re-declaring component interest gets nothing re-added because the
handler dedupes through `ServedComponents.UnservedOf`
(`WorldsAdriftRebornGameServer.cs:4509-4534`). Ship-domain checkout cannot churn
while somebody is aboard.

**H3 - command 1073 echoes per part during flight.** ELIMINATED. 1073 is
`ClientAuthoritativePlayerState`, a **player avatar** component
(`Multiplayer/MirrorSendPolicy.cs:63`). It is never written to a part or a hull.

**H4 - the server recentres or normalises the steering value per tick (which
alone would make the bar oscillate).** ELIMINATED, both ends.
Server: `FlightControlInput` persists the held value and explicitly documents why
it must not decay (`Multiplayer/Ship/Flight/FlightControlInput.cs:5-15`); the only
transformation is a one-shot 0.1 deadzone applied at construction
(`:41`, `:109-113`), and the only zeroing is `LatchedThrottleOnly()` on dismount
(`:106-107`). `DecideEmission` never touches `_input`
(`FlightSession.cs:361-436`). Two new tests pin this:
`The_server_never_recentres_a_held_steering_axis` and
`Only_an_explicit_new_input_moves_the_latched_axis`.
Client: `HelmYawResponsePolicy.ApplyReversal`
(`Multiplayer/Ship/Flight/HelmYawResponsePolicy.cs:22-38`) only accelerates the
journey back through centre on an opposing input and is monotone toward the new
sign; it cannot oscillate and it preserves the latch.

**H5 - the vector-authority path publishes something extra per part during
turns.** ELIMINATED. It is default OFF and additionally requires
`WAREBORN_FLIGHT_FIXED_STEP=1` and `WAREBORN_FLIGHT_FORCES=1`
(`Multiplayer/Ship/Flight/FlightRuntimeFlags.cs:13-16`, `:135-146`). Even when on,
a promoted hull funnels through `AdvanceAdopted` into the **same**
`DecideEmission` state machine and the same single `BroadcastDomainMotion`
(`ShipFlightService.cs:1956-1993`, `FlightSession.cs:338-354`); the shadow
observer writes no wire traffic at all. Worth noting for a future promotion: the
vector path is *already* phase-locked, so it does not inherit this defect.

**H6 - the frozen parent 190602 timeline makes the child interpolator pick the
wrong sample.** ELIMINATED as a cause of *this* symptom, but a real latent
defect. It is true that a `"~"` child's local-transform interpolator is sampled
at the parent hull's 190602 timestamp
(`Multiplayer/ShipPartMotionPolicy.cs`, `ParentStampFor` remarks), that child
stamps advance every flight point (`ShipFlightService.cs:1548-1549`), and that the
hull's own 190602 is never sent during flight - so the parent sampling time is
frozen. It cannot produce the symptom, because **every enqueued child sample
carries the identical hull-local transform** (constant offset, constant
`mount.PackedRotation`); interpolating or extrapolating between equal values
returns that value at any sampling time, valid or nonsensical. The world pose the
player sees is composed from the hull's live pose, not from which local sample was
selected. Two follow-ups this leaves behind, neither a blocker here:
(a) `PartMountService.cs:362-365` claims *"On a MOVING built hull this stamp is
owned by the hull's motion clock (ShipFlightService publishes the same parentless
hull update per flight wake)"* - that is factually wrong about the current code
and directly contradicts `ShipFlightService.cs:1278-1281`; the comment should be
corrected. (b) the child sample queue grows unboundedly against a frozen parent
clock during a long flight.

**H7 - two unsynchronised monotonic stamp counters for one 190602 timeline.**
NOT the cause here, real hazard. `PartMountService.NextTimelineSample()`
(`PartMountService.cs:59-69`, used by the flight wake) and
`ShipPartMotionService._sample` (`ShipPartMotionService.cs:42`, `:128`, used by
ferry/nudge/admin) both mint stamps for the same entities' 190602. A stamp
regression is reachable if an admin or ferry path interleaves with a flight wake,
and `PartMountService.cs:46-57` says in as many words that this must not happen.
None of those paths run during ordinary piloted flight, so it is not this bug.

**H8 - two authored local poses per part fight each other.** ELIMINATED.
`ShipPartState.attachPos/attachRot` is written only on mount commit
(`PartMountService.cs:405-406`) and in the checkout seed
(`ComponentsSerializer.cs:2402-2417`); it carries the same hull-local offset as
the TransformState and is never re-sent per tick.

**H9 - the pilot camera is the thing vibrating.** ELIMINATED by the prior work's
own live probe: `docs/architecture/flight-publication-phase-lock.md:7-9` records
that *"Passive client probes ruled out the pilot/camera LateUpdate order"*, and
the earlier acceptance found instruments vibrating **more** than hull and deck -
camera jitter would shake everything equally.

## Still client-side, and deliberately not patched

Two amplifiers remain on the client and are documented here rather than fixed,
because patching the client DLL triggers the project's standing patcher-update
rule and neither is the driver.

1. **The `"~"` follower apply threshold.**
   `DefaultPhysicalParameters.MinAngleToInterpolateBetween = 0.01f` and
   `MinDistanceToInterpolateBetween = 0.0001f`, consumed through
   `ILerpTransformSettings` and overridable per prefab by `TransformNature`. A
   `"~"` part's composed rotation is therefore applied in ~1.15 degree steps. With
   a *constant* yaw rate that is a uniform, mild shimmer; with a wobbling rate it
   becomes irregular re-snapping. Removing the driver is what turns the second
   into the first. A client mod that wanted to remove it entirely would have to
   raise `TransformNature.MinAngleToInterpolateBetween` on ship-part prefabs (NOT
   the global default - the same threshold gates the send side), and would need to
   be re-validated against the rejected 2026.08.24-1 continuity trial, which
   proved that blindly defeating these thresholds exposes a contact/carry
   disagreement
   (`docs/architecture/client-ship-motion-continuity-2026-08-24.md:35-47`).

2. **`MaxSecondsToInterpolateAfterLastUpdate = 0f`.** Interpolation ceases the
   instant the last update's window is consumed. Our 4.2 Hz member wake refills it
   in time, so this is currently latent, but it is the reason a member wake cadence
   below the point cadence would read as snapping.

Neither can be measured further without a live client; both are named so the next
person does not re-derive them.

## The correction

Server-side, pure logic in the Multiplayer assembly plus one line of glue.

- **`Multiplayer/Ship/Flight/FlightStampPolicy.cs`** (new): `FlightStampMode`
  (`WallClock` / `PhaseLocked` / `Continuity`) and `NextStamp`. The historic rule
  is preserved verbatim as `WallClock` and is still the default.
- **`Continuity`** stamps the point at `last + step` - matching the simulation it
  represents - and resyncs to wall clock only once the wire clock has fallen a
  **whole publication interval** behind. That threshold is not a taste call: a
  drift-free `CadenceTimer` keeps a phase-locked wire clock within one poll period
  of wall clock indefinitely, so the only way to lag a full 240 ms is the timer's
  stall branch (`RelayCadence.cs:170-177`), which re-bases `_nextDue` to `now` and
  drops the missed ticks. Resyncing exactly there preserves the reason the legacy
  rule existed - keeping the client's smoothed server-latency estimate sane across
  a pause - without letting ordinary jitter stretch a point.
- **`FlightSession`** now routes every stamp through the policy; the private
  plumbing carries a `FlightStampMode` instead of a `bool phaseLocked`. The public
  signatures are unchanged apart from one optional `stampContinuity = false`.
- **`ShipFlightService.StampContinuityEnabled`** reads
  `WAREBORN_FLIGHT_STAMP_CONTINUITY`, **default OFF**, and is passed only to the
  legacy `session.Advance` call. It is not consulted in fixed-step mode, which
  already phase-locks.

Default OFF because it changes wire timestamps, which is what the client's
latency estimate is built from - the evidence for the mechanism is strong, the
evidence for how a live client's estimator responds is not, and this must be
A/B-able against the exact symptom.

### How to enable it

```
# a systemd drop-in, or an export in run-gameserver.sh
Environment=WAREBORN_FLIGHT_STAMP_CONTINUITY=1
```

Startup then logs `[info] legacy 1130 stamp continuity is ON (...)`. Rollback is
removing the variable and restarting; nothing persistent changes.

## Verification

- `FlightStampPolicyTests` + `FlightStampContinuityWiringTests`: 29 new tests.
  They pin the defect (`Wall_clock_stamps_stretch_unevenly_under_ordinary_poll_jitter`),
  the correction (`On_the_wire_interval_matches_the_simulated_interval_exactly`),
  the invariants every mode owes the client (monotonicity and
  `ShipMotionPolicy.IsLegalSeparation`), the OFF path being byte-identical, the
  steering latch, and the source contract that no new per-part publication was
  invented.
- Full Multiplayer suite: 5,148 passed / 0 failed (5,119 baseline + 29 new).
- Server suite: 1,262 passed / 26 skipped, unchanged.

## Visual verification checklist

One scenario, one boot. Set `WAREBORN_FLIGHT_STAMP_CONTINUITY=1` and restart the
game server; confirm `[info] legacy 1130 stamp continuity is ON` in the log.

1. Board the equipped ship and take the helm.
2. Bring it to a **low, steady speed** - one engine at part throttle, or sails
   only. Low speed matters: it keeps the ship in frame and stops forward motion
   masking the rotation.
3. Hold `A` until the ship is in a **hard, sustained turn**, then release. The bar
   must **stay** at that angle and the ship must **keep turning** - that latch is
   correct and must not have changed.
4. Watch, for a full ten seconds of turning, in this order:
   - the **helm's own steering bar**: it should hold its angle dead still. A
     rhythmic tremor a few times a second is the failure.
   - the **five flight instruments** on their bar pipes: still relative to the
     pipe under each one.
   - a **far mounted part** - an outboard engine, a wing, a sail - is where the
     old defect was loudest, because it has the longest lever arm. It should
     sweep smoothly through the arc, not advance-and-catch.
   - the **deck and hull** relative to the world horizon: a smooth constant sweep,
     not a stutter.
5. Counter with `D` to centre the rudder and let the ship come out of the turn.
   The transition should be smooth in both directions.
6. Return throttle to neutral, furl sails, stay aboard through final rest, and
   confirm the previously-fixed behaviour has not regressed: no low-speed forward
   snapping, no mounted part lagging or detaching.

Pass = no per-part vibration or re-snapping at any point of step 4, with the latch
from step 3 intact. A residual *uniform, very fine* shimmer on the helm or a sail
at high turn rates is the remaining client-side apply threshold (section "Still
client-side") and is not a failure of this correction - report it as "smooth but
grainy" rather than "vibrating", so the two stay separable.

If the vibration is unchanged with the flag on, the driver is not the stamp and
the next place to instrument is a live client probe of the hull's
`SSPDeadReckoningVisualizer.NextFrameRotation` versus the hull rigidbody's
rotation, sampled every FixedUpdate during a held turn - that is the one seam this
investigation could not measure offline.
