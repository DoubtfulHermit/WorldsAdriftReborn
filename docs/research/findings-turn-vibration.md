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

## Root cause on the LEGACY publisher (local dev servers)

> Production does **not** run this publisher - see evidence item 6 and
> "Production conditions" below. This section diagnoses the legacy path; the
> production path is diagnosed separately further down.

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

**6. WHICH SERVER THIS APPLIES TO - the phase lock is ON in production.**

Corrected 2026-08-25 after the coordinator verified the live host directly
(`root@62.171.161.19`, both the systemd unit and `/proc/<MainPID>/environ`):

```
WAREBORN_FLIGHT_FIXED_STEP=1
WAREBORN_FLIGHT_FORCES=1
WAREBORN_HELM_FLIGHT=1
```

`~/Games/WAReborn-servers/run-gameserver.sh`, which sets no `WAREBORN_*`
variables at all, is the **local dev launcher**, not the server the user plays
on. So:

| Server | Publisher | Does the `WallClock` defect above apply? |
|---|---|---|
| **Production** (`WAREBORN_FLIGHT_FIXED_STEP=1`) | fixed-step, `phaseLockedEmit: true` (`ShipFlightService.cs:1144`, `:1970`, `:1992`) -> `FlightStampMode.PhaseLocked` | **No.** Stamps are already exactly 240 ms apart. |
| **Local dev** (no flags) | legacy `session.Advance` | **Yes.** |

The `Continuity` correction below is therefore a **legacy-path fix**. It will
change nothing for a player on the live server, and that is stated plainly so
nobody re-attributes a live symptom to it. What production still suffers from is
analysed in "Production conditions" below.

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

## Production conditions: what still vibrates when stamps ARE even

Added 2026-08-25. Everything below is read off the **full retail decompile with
method bodies** at `/home/ttanurhan/Games/WAReborn-decompiled/acs` (see
`docs/HANDOVER.md:46`), not off the stripped assemblies. Two claims in the
earlier revision of this document were made without method bodies and are
**wrong**; they are corrected here rather than quietly dropped.

### Corrected: the 0.01 quaternion threshold does NOT gate a flying ship

The earlier claim - inherited from `client-ship-motion-continuity-2026-08-24.md:28-32`
- was that `FixedUpdateLerpLocalTransformBehaviour` "applies a hull-relative
entity only when a quaternion component differs by at least 0.01", quantising
mounted-part rotation into ~1.15 degree steps. The real gate is an **OR across
position and rotation**, and the position side is 1 mm per axis:

```csharp
// FixedUpdateLerpLocalTransformBehaviour.cs:274-287
private static float cheapQuaternionThreshold = 0.01f;   // :72
private static float cheapPositionThreshold   = 0.001f;  // :74

private bool TransformExceedsThreshold(TransformData last, TransformData now)
    => PositionExceedsThreshold(last.Position, now.Position)
    || RotationExceedsThreshold(last.Rotation, now.Rotation);
```

Any ship translating faster than ~0.05 m/s moves a mounted part more than 1 mm
per 20 ms physics step, so the position term opens the gate on **every**
FixedUpdate and the composed rotation is applied with it. At a 4 m lever arm and
a 20 deg/s turn the part moves 28 mm per step - 28x the threshold. **The
threshold never quantises anything during flight.** ELIMINATED.

### Corrected: the follower does NOT freeze between 190602 updates

`MaxSecondsToInterpolateAfterLastUpdate = 0f` was listed as an amplifier. It is
not, because a `"~"` follower does not take its world pose from its own
interpolator at all. `GetNextFrameData` composes the **hull's live pose** with
the local offset every FixedUpdate:

```csharp
// FixedUpdateLerpLocalTransformBehaviour.cs:231-241
fixedUpdateNonAuthoritativeMode.RunNextFixedUpdate();
var local = interp.GetInterpolatedValueForTime(fixedUpdateNonAuthoritativeMode.Timestamp);
Coordinates c = m.NextFramePosition + m.NextFrameRotation * local.Position;
Quaternion  r = m.NextFrameRotation * local.Rotation;
```

and `NextFramePosition/NextFrameRotation` are `PathFollower.PreviousSample`
(`SSPDeadReckoningVisualizer.cs:49-63`), refreshed by the hull's own 50 Hz
`PathFollower.FixedUpdate`. The 190602 wake only keeps `UpdatesEnabled` true
(`ManagedFixedUpdate:167-175`). A part therefore tracks the hull at 50 Hz
regardless of the 4.2 Hz wake. ELIMINATED.

This also closes H6 with code rather than argument. The child interpolator's
`pendingValues` is a **`CircularFifoQueue(5)`**
(`DelayedLinearInterpolator.cs:12`), so the frozen parent clock cannot leak
memory; and because `GetInterpolatedValueForTime` sets `CurrentTime = targetTime`
before computing `num = CurrentTime - previousTime` (`:64-71`, `:95-104`), a
frozen parent time yields `num = 0`, ratio 0, and the interpolator returns its
current value unchanged. Constant in, constant out.

### What is left, and it is one thing: the wire has no angular velocity

Component 1130 `SSPPredictedMotionState` has two fields, and its
`ShipControlPoint` has exactly five:

```
field1_timestamp (long) | field2_position (Coordinates) |
field3_rotation (Quaternion32) | field4_velocity (Vector3f) | field5_fsim_id_hash (int)
```

(`WAReborn-decompiled/gencode/Schema.Bossa.Travellers.Motion.Prediction/ShipControlPoint.cs:14-101`;
client mirror `acs/Bossa.DeadReckoning/ControlPoint.cs:19-30`.) `field4_velocity`
is **linear** velocity. There is no angular-velocity field anywhere in the
component.

That single omission produces three separate turn-only artefacts, none of which
exists in straight flight:

1. **Attitude is C0, position is C1.**
   `SplineInterpolator.CubicHermiteInterpolation` builds position from a cubic
   with real endpoint velocity tangents (`SplineInterpolator.cs:41-51`) but
   rotation from a bare `Quaternion.SlerpUnclamped` (`:44`). Angular velocity is
   therefore piecewise constant with a kink at every 240 ms point. A held,
   steady turn is smooth; every *change* of turn rate - winding in, unwinding,
   a gust, the bank-roll chasing `yawRate` (`FlightIntegrator.cs:299-308`) -
   lands as an angular-acceleration step at 4.2 Hz.

2. **Extrapolation FREEZES the rotation.**
   `ControlPoint.ExtrapolateWithConstantVelocity` advances position by
   `velocity * time` and copies `previous.Rotation` **unchanged**
   (`ControlPoint.cs:71-76`), because there is no rate to extrapolate. Whenever
   the playback clock outruns the newest buffered point,
   `SplineInterpolator.Interpolate` returns false (`SplineInterpolator.cs:23-26`)
   and `PathFollower.FixedUpdate:280-305` builds a halt ladder out of exactly
   those extrapolations. In straight flight that is very nearly exact and
   invisible. **In a turn the hull simply stops yawing** for the duration.
   When the next real point lands, `AddControlPoint:153-167` sets
   `_nextSampleRequiresSplineCorrection`, `StartSplineCorrection:174-194` picks
   `SlowSplineCorrectionTime = 5 s` (the fast 0.5 s path needs >25 m or >30 deg,
   `ShipConfiguration.cs:46-52`), and `ApplySplineCorrection:209` applies the
   correction **multiplicatively to rotation**. Freeze, then a five-second
   rotational unwind. That is the best available description of "vibrates
   heavily / re-snaps, but only while turning".

3. **Quaternion32 endpoint quantisation, which is exactly zero in straight
   flight.** `Quaternion32Packing.To10Bits` maps a component over
   `[-1/sqrt2, +1/sqrt2]` onto 0..1022
   (`Multiplayer/Placement/Quaternion32Packing.cs:148-153`), so the component
   step is `sqrt2/1022 = 0.001384`. A quaternion component error `e` is a
   rotation error of `2e`, giving a worst case of **0.0793 degrees** per point -
   about 5.5 mm at a 4 m lever arm. In straight flight consecutive points encode
   to the *same* lattice value, so the contribution is bit-exactly zero; in a
   turn every point re-quantises and the error is a fresh draw at 4.2 Hz. Small,
   but structurally turn-only. (An earlier estimate of 0.35 deg/LSB was 360/1022
   and is wrong - the packing is per component, not per degree.)

Position is immune to all three: it has real tangents, its extrapolation is
exact for constant velocity, and it is transmitted as full-precision `double`
`Coordinates` (`ShipControlPoint.cs:16`).

### Can the server fix this? No, and here is why, plainly

- **Emit angular velocity so the client can use tangents.** Impossible against
  the stock client: there is no field to put it in, and even if the schema were
  extended, `CubicHermiteInterpolation` would have to be rewritten to consume it.
  **Client mod required.**
- **Publish 1130 more often for a rotating hull.** Impossible against the stock
  client. `ControlPoint.ValidateControlPoints` drops any point whose timestamp is
  less than `desiredInterval * 0.95` after the previous one
  (`ControlPoint.cs:113-126`), with `desiredInterval = ShipConfiguration.SendInterval
  = 0.24` - a hard **228 ms floor**. Worse, on rejection
  `SSPDeadReckoningVisualizer.cs:116-121` does **not** advance
  `PreviousControlPoint`, so a 120 ms cadence would have exactly half its points
  silently discarded and still play at 240 ms, for double the bandwidth. The only
  lever is `SendInterval` itself, which lives in a client-side `ShipConfig`
  ScriptableObject (`ShipConfiguration.cs:6`, `:82-86`) - i.e. a patched client
  asset that would have to move in lockstep with the server's
  `ShipMotionPolicy.SendIntervalSeconds` and `StepsPerPublication`.
  **Client mod required.**

So **240 ms is too coarse for rotation, and neither remedy is available
server-side.** That is the honest answer to the question.

### What IS server-fixable on the production path

One thing, and it is real. `FixedFlightClock.Advance` caps a backlog at 25 steps
and then **consumes the whole accumulator anyway, including the part it refused
to simulate** (`Multiplayer/Ship/Flight/FixedFlightClock.cs:50-61`). Emission
stays honest - `_simulationStep` counts only executed steps (`:63`), so every
published point still encloses exactly twelve integrated 20 ms steps - but
`FlightStampMode.PhaseLocked` advances the stamp by exactly one interval
regardless. **Every dropped step is therefore 20 ms of permanent, never-recovered
lag of the wire clock behind wall clock**, and `Continuity`'s resync is
unreachable in fixed-step mode because `phaseLockedEmit` wins
(`FlightSession.StampModeFor`).

The client turns accumulated lag into artefact 2 above:
`_serverLatency = UpdateNow - (stamp - ExtrapolationTime)`
(`PathFollower.cs:146-147`) is clamped at `MaximumServerLatency = 5.0`
(`ShipConfiguration.cs:20`); past that clamp the playback time outruns the buffer
and the halt / frozen-rotation / 5 s spline-correction cycle begins.

The correction charges the dropped simulated time to the next emitted stamp, so
the wire clock stays locked to wall clock with zero permanent drift and the rate
error is confined to the one segment that actually lost simulation. It rides the
same `WAREBORN_FLIGHT_STAMP_CONTINUITY` opt-in, whose meaning is now "keep the
1130 wire clock aligned with real elapsed time" on both publishers.

**This is a correction for an occasional lurch, not a claim that it is the whole
symptom.** Whether it matters live is a measurable question, answered below.

### Ranked candidates for PRODUCTION

| # | Candidate | Evidence | Fixable? |
|---|---|---|---|
| 1 | Rotation freezes during every extrapolation, then unwinds over a 5 s spline correction | `ControlPoint.cs:71-76`, `PathFollower.cs:280-305`, `:153-167`, `:174-194`, `:209` | **Client-only.** Server can only avoid triggering it by never letting the buffer erode. |
| 2 | Attitude is C0 across every 240 ms point while position is C1 | `SplineInterpolator.cs:41-51` vs `:44` | **Client/protocol-only.** No angular-velocity field exists. |
| 3 | Phase-locked stamp banks permanent lag when the catch-up cap drops steps | `FixedFlightClock.cs:50-63`, `PathFollower.cs:146-147`, `ShipConfiguration.cs:20` | **SERVER-FIXABLE - fixed in this branch, default OFF.** |
| 4 | Quaternion32 endpoint quantisation, +/-0.079 deg, zero in straight flight | `Quaternion32Packing.cs:148-153` | **Not fixable** - the wire type is `Quaternion32`. |
| 5 | Send-time jitter vs phase-locked stamps poisons the client's latency estimate and erodes the buffer | `PathFollower.cs:146-147`, `:106` (>=2 s convolution), `:251-256` | **Server-fixable in principle; unmeasured.** The new cadence trace measures exactly this. |

Eliminated for production: the 0.01 quaternion apply threshold; the follower
freezing between wakes; per-part pose competition (H1); the `WallClock` stamp
defect (that publisher is not running); dropped-step batches emitting *short*
points (`_simulationStep` counts executed steps only, so they do not).

### The measurement that decides between 1/5 and the rest

Before any further change, three numbers from the live host:

1. `grep -c 'flight fixed-clock pressure' <server log>` - each occurrence is a
   dropped-step event, i.e. candidate 3 firing.
2. `fixedClock.droppedSteps` and `fixedClock.pressureEvents` from the shipdiag
   JSON snapshot - monotonic totals, so one snapshot answers "has it ever
   happened" and two answer "how often".
3. `WAREBORN_FLIGHT_CADENCE_TRACE=1` and watch the `[flight-cadence]` line during
   a sustained turn. `worstStampDev` must be `0.0ms` under fixed step - if it is
   not, the phase lock is not doing what it claims. `drift` is the buffer-erosion
   budget: bounded oscillation around zero is healthy, a growing positive number
   is candidate 5 and predicts candidate 1.

If pressure is zero and drift is flat, candidates 3 and 5 are dead and the live
symptom is candidates 1, 2 and 4 - which are client-side interpolation limits
that no server change can reach.

### If it is client-side: exactly what the mod would change

Recorded so the decision can be made separately, since a client change triggers
the standing patcher-update rule. In increasing order of intrusiveness:

1. **Lower `SendInterval` in the patched `ShipConfig` ScriptableObject** from
   0.24 to e.g. 0.12, and move `ShipMotionPolicy.SendIntervalSeconds` and
   `FixedFlightPublicationSchedule.StepsPerPublication` to match. This halves
   every artefact above at once: the C0 kink rate doubles so each kink is half as
   large, the quantisation draw happens twice as often over half the arc, and the
   extrapolation window halves. It is a config asset, not code. Risk: it doubles
   1130 bandwidth per flying ship, and the two sides MUST ship together or the
   0.95 reject floor silently drops half the stream.
2. **Give rotation a tangent.** Patch `SplineInterpolator.CubicHermiteInterpolation`
   to squad-interpolate using the neighbouring control points' rotations as
   implicit tangents. No schema change, no server change, no extra bandwidth -
   it derives the missing derivative from points the client already buffers.
   This is the surgical fix for artefact 1 and it is worth costing.
3. **Extrapolate rotation.** Patch `ControlPoint.ExtrapolateWithConstantVelocity`
   to carry an angular delta derived from the last two received points, so a
   buffer underrun no longer freezes the yaw. Directly targets candidate 1.

Options 2 and 3 need no server or schema change at all, which makes them
strictly cheaper than option 1. None of them may be attempted without
re-validating against the rejected `2026.08.24-1` continuity trial, which proved
that defeating client smoothing blindly exposes a contact/carry disagreement
(`docs/architecture/client-ship-motion-continuity-2026-08-24.md:35-47`).

## The client correction (option 2, implemented)

Added 2026-08-25 on `fix/client-rotation-interpolation`, after the live
measurement came back **2,406,852 completed fixed steps, 5 dropped steps, 1
pressure event**. That kills candidates 3 and 5 as the live driver and leaves
candidates 1 and 2, which are client-side. Option 2 is implemented; option 3 is
designed but deliberately not shipped (see below).

### What it does

`SplineInterpolator.Interpolate` is postfixed so that ATTITUDE is squad
(spherical-quadrangle) interpolated instead of bare-slerped, using the
neighbouring buffered control points as implicit tangents:

```
s_i   = q_i * exp( -( a * log(q_i^-1 q_i+1) + b * log(q_i^-1 q_i-1) ) / 2 )
squad = slerp( slerp(q_i, q_i+1, t), slerp(s_i, s_i+1, t), 2t(1-t) )
```

with the Kim/Kim/Shin non-uniform weights `a = h_prev/(h_prev+h_next)` and
`b = h_next/(h_prev+h_next)`, where `h_prev` and `h_next` are the intervals
either side of the point whose inner control quaternion is being built. No
schema change, no server change, no extra bandwidth: the missing derivative is
derived from points the client already buffers.

- Pure policy: `Multiplayer/Ship/Flight/ShipRotationSplinePolicy.cs`, source-linked
  into the net35 mod exactly like `HelmYawResponsePolicy`, so the unit tests run
  the same code the client does.
- Harmony glue: `WorldsAdriftReborn/Patching/Flight/ShipRotationSpline_Patch.cs`.
- Toggle: `[Flight] Flight_SmoothShipRotation` in `WorldsAdriftReborn.cfg`,
  **default true**, re-read live (`WAConfig_Patch` reloads that file every 5 s),
  so it can be flipped mid-flight and A/B'd against the exact symptom without a
  relaunch.

### Three properties that make it safe, each pinned by a test

1. **It is the identity on a steady turn.** Under a constant angular rate the two
   weighted tangent logs cancel exactly - for uneven stamps as well as even ones -
   so `s_i = q_i` and squad degenerates to the slerp retail already draws. It acts
   only where the turn RATE changes, which is exactly where the C0 kink is.
2. **It never moves an authoritative pose.** At `t=0` and `t=1` the result is
   `q_i` and `q_i+1` bit-for-bit. It chooses a smoother route BETWEEN two
   attitudes the server really sent; it cannot walk the client off the server.
3. **`s_i` depends only on the point and its two neighbours**, never on which
   segment is being drawn - which is what makes the join C1: the segment arriving
   at `q_i+1` and the segment leaving it compute the same `s_i+1` and therefore
   agree on the angular rate there.

### Why `Interpolate` and not `CubicHermiteInterpolation`

Two reasons, both load-bearing. `Interpolate` is the only overload handed the
whole control-point LIST, which is where the neighbour attitudes live. And
`CubicHermiteInterpolation` is also the engine of
`PathFollower.ApplySplineCorrection` (`PathFollower.cs:205`), which drives it
with rotation DELTAS from identity rather than world attitudes - smoothing there
would silently reshape the post-underrun recovery ramp. Patching `Interpolate`
leaves that path untouched by construction.

### Where the tangents come from, and what happens at a buffer edge

- **Forward neighbour** `q_i+2`: read straight out of the buffer at
  `fromIndex + 2`. The client plays back `ExtrapolationTime = 0.75 s` behind the
  newest stamp, i.e. about three 240 ms points of lead, so it is normally there.
- **Backward neighbour** `q_i-1`: **not** normally in the buffer, because
  `PathFollower.FixedUpdate:306-309` trims with `RemoveRange(0, fromIndex)` after
  every successful interpolation, leaving the current from-point at index 0 for
  the rest of its life. It *is* present on the single frame the segment advances
  (at `fromIndex - 1`, immediately before the trim), and that is when the patch
  captures it, into a weak per-buffer memory keyed on the `List<ControlPoint>`
  instance. Nothing is invented: this only remembers a point the client really
  received.
- **A missing neighbour contributes a zero tangent** (`s = q`, the textbook
  clamped end condition), never a guessed rotation. With BOTH missing the formula
  would collapse to plain slerp anyway, so that case short-circuits to "do not
  touch retail's value" rather than recomputing the same answer in double
  precision and handing back a rounding delta.
- Neighbours are additionally rejected if they are not `Received`, are not
  strictly earlier/later, sit more than `4 x` the current segment away (a clear,
  re-seed or halt happened across the gap), or imply an attitude step over
  90 degrees (a correction or teleport, not a turn).
- Segments shorter than half a `SendInterval` are skipped entirely. That is what
  keeps the patch off `DeadReckoningSender`, which drives the same static method
  over its own 50 ms pre-smoothed buffer on the SEND path.

### Why this is not the rejected `2026.08.24-1` trial

That trial bypassed the two receive-side apply thresholds by REPEATING the hull
`MovePosition`/`MoveRotation` target every fixed update and forcing every `"~"`
follower to re-apply its composed target. It compiled and tested green, and it
failed live acceptance: repeating the pose left `PathFollower.PreviousSample` and
the Rigidbody velocity on an older sample, and the local player's contact/carry
path reads exactly those - so player and ship drifted smoothly apart and then
hard-corrected two or three times before rest
(`docs/architecture/client-ship-motion-continuity-2026-08-24.md:33-61`).

This change does none of that. It alters ONE FIELD of the ONE sample retail was
already about to use, before retail uses it. The sample count, the schedule,
`PathFollower.Move`, both apply thresholds, `PreviousSample`, the Rigidbody
velocity and the spline-correction machinery are all the stock code paths, and
the value handed to them is still a real attitude on the arc between two server
poses. Nothing downstream can observe a state it would not otherwise have seen -
which is the specific failure the trial produced.

### Option 3 (extrapolate rotation): designed, NOT shipped

`ControlPoint.ExtrapolateWithConstantVelocity` copies `previous.Rotation`
unchanged (`ControlPoint.cs:71-76`), so a buffer underrun freezes the yaw and the
next real point unwinds it over a 5 s spline correction. Carrying an angular
delta there is the direct fix for candidate 1, and it is deliberately deferred:

- The method is `static` and takes only a `ControlPoint` struct, so it has no
  identity to hang a per-ship angular rate on. `FsimIdHash` is the server's
  worker-id hash - identical for every ship - so it cannot key one. The only
  available arming signal is call ORDER inside `PathFollower.FixedUpdate`, and
  that same method is also called from `AddControlPoint`'s halt-recovery branch
  and from the spline-correction setup at `PathFollower.cs:315`, where changing
  the extrapolated target changes the correction quaternion itself.
- More importantly, it would inject invented rotation with no server backing into
  the halt ladder, which feeds `PreviousSample` - the exact contact/carry state
  the rejected trial desynchronised. A deck-standing player would be rotated by a
  guess and then hard-corrected when the real point landed: the trial's failure
  signature, reached by a different route.
- The live measurement says underruns are rare on production anyway (5 dropped
  steps in 2.4 M).

If option 2 lands and a residual freeze/unwind is still visible during a lag
spike, revisit this with its own flag and its own live acceptance - not before.

### Verification

- `ShipRotationSplinePolicyTests`: 20 tests. They pin the DEFECT (retail's slerp
  stepping the attitude rate by 25 deg/s across a join where the turn rate
  changes), the CORRECTION on the same data, the steady-turn identity on both
  even and uneven stamps, endpoint preservation, bounded deviation, unit-norm
  output, hemisphere-flip robustness, off-yaw axes, and every fail-safe path
  (both neighbours absent, a stale neighbour, a non-advancing segment, a
  teleport-sized step, NaN/Inf, a zero quaternion).
- Multiplayer suite: 5,185 passed / 0 failed (5,165 baseline + 20 new).
- Server suite: unchanged - this change is client-side.
- `dotnet build WorldsAdriftReborn -c Release` against the real game assemblies:
  clean, and the shipped DLL still references `mscorlib 2.0.0.0` (CLR 2.0), which
  `build-manifest.sh` refuses to publish without.

### In-game A/B

Both halves of the A/B are on ONE client - the hull's pose is server-replicated
even for the pilot, so a single player can see the difference without a second
peer. Edit `BepInEx/config/WorldsAdriftReborn.cfg` while flying; the value is
re-read within 5 s and takes effect on the next control-point segment.

1. Board the equipped ship, take the helm, bring it to a **low, steady speed**
   (one engine at part throttle, or sails only). Low speed keeps the ship in
   frame and stops forward motion masking the rotation.
2. `Flight_SmoothShipRotation = false`. Hold `A` into a hard sustained turn, then
   release - the bar must hold its angle and the ship must keep turning. Watch a
   **far mounted part** (outboard engine, wing, sail) for a full ten seconds: the
   long lever arm is where the rate kink is loudest. Then the helm's own steering
   bar, then the five flight instruments on their bar pipes.
3. Set it to `true` and repeat the same turn without relaunching. Log line
   `[WAR][flight] ship attitude spline smoothing is ON` confirms the flip landed.
4. Pass = the arc becomes a smooth sweep instead of advance-and-catch, with the
   steering latch from step 2 intact and no new separation between the player and
   the deck at any point.

A residual *uniform, very fine* shimmer at high turn rates is candidate 4, the
`Quaternion32` endpoint quantisation (+/-0.079 degrees, ~5.5 mm at a 4 m lever
arm), which is a wire-type limit and is not fixable at all. Report that as
"smooth but grainy" rather than "vibrating", so the two stay separable.

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
- **Dropped-step compensation (the production-path half).**
  `FlightStampPolicy.LostSimulationMilliseconds(droppedSteps, stepSeconds)` turns
  a capped batch's thrown-away simulation into milliseconds, and `NextStamp` adds
  it to the phase-locked candidate. `ShipFlightService`'s fixed-step branch
  charges it to the FIRST point a batch emits and then clears it, so it is counted
  once. Zero - the default - reproduces today's stamps exactly.
- **`ShipFlightService.StampContinuityEnabled`** reads
  `WAREBORN_FLIGHT_STAMP_CONTINUITY`, **default OFF**. One flag, one meaning on
  both publishers: *keep the 1130 wire clock aligned with real elapsed time.* On
  the legacy path that means not stretching a point for poll jitter; on the
  fixed-step path it means not banking permanent lag when the catch-up cap drops
  simulation.
- **`FlightSendCadence` + `WAREBORN_FLIGHT_CADENCE_TRACE`** (default OFF, pure
  measurement, no behaviour change): per hull, the wall-clock spacing of
  consecutive 1130 sends against the spacing their stamps claim, and the running
  difference. That difference is the client's playback-buffer erosion budget.

Default OFF because it changes wire timestamps, which is what the client's
latency estimate is built from - the evidence for the mechanism is strong, the
evidence for how a live client's estimator responds is not, and this must be
A/B-able against the exact symptom.

### How to enable it

```
# a systemd drop-in, or an export in the launcher
Environment=WAREBORN_FLIGHT_STAMP_CONTINUITY=1
Environment=WAREBORN_FLIGHT_CADENCE_TRACE=1     # measurement only, safe to leave off
```

Startup logs `[info] legacy 1130 stamp continuity is ON (...)` and
`[info] 1130 send-cadence trace is ON (...)`. Rollback is removing the variables
and restarting; nothing persistent changes.

**Set expectations honestly.** On the LIVE server (fixed step ON) this flag only
removes the dropped-step lag - candidate 3. If
`grep -c 'flight fixed-clock pressure'` on the live log is zero, it will change
nothing you can see, and the remaining symptom is candidates 1, 2 and 4, which
are client-side. On a LOCAL dev server (no flags) it additionally removes the
`WallClock` stamp stretching, which is a much larger effect.

## Verification

- `FlightStampPolicyTests` + `FlightStampContinuityWiringTests`: 29 new tests.
  They pin the defect (`Wall_clock_stamps_stretch_unevenly_under_ordinary_poll_jitter`),
  the correction (`On_the_wire_interval_matches_the_simulated_interval_exactly`),
  the invariants every mode owes the client (monotonicity and
  `ShipMotionPolicy.IsLegalSeparation`), the OFF path being byte-identical, the
  steering latch, and the source contract that no new per-part publication was
  invented.
- `FlightProductionCadenceTests`: 17 further tests for the production path -
  that an uncompensated phase lock banks lag, that compensation tracks wall clock,
  that lag grows past the client's 5 s clamp without it, that compensation never
  rewinds and never violates the reject floor, that zero is byte-identical to
  today, and that the cadence trace reports drift only when drift is real.
- Full Multiplayer suite: 5,165 passed / 0 failed (5,119 baseline + 46 new).
- Server suite: 1,262 passed / 26 skipped, unchanged.

## Visual verification checklist

One scenario, one boot. **Do the measurement first** (previous section): if
`fixedClock.droppedSteps` is zero on the live host, expect this flag to change
nothing there and treat the run as a client-side confirmation instead.

Set `WAREBORN_FLIGHT_STAMP_CONTINUITY=1` and `WAREBORN_FLIGHT_CADENCE_TRACE=1`,
restart the game server, and confirm both `[info]` lines report ON.

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

If the vibration is unchanged with the flag on - which is the EXPECTED outcome on
the live server unless the log shows fixed-clock pressure - then the driver is
candidate 1 or 2 and it is client-side. The decision then moves to the three
client-mod options listed above, of which patching
`SplineInterpolator.CubicHermiteInterpolation` to give rotation a tangent is the
cheapest: no schema change, no server change, no extra bandwidth. That is a
separate decision because it triggers the patcher-update rule.
