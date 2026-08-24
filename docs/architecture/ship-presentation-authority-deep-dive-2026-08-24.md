# Ship presentation authority deep dive — 2026-08-24

## Scope

Hull `3639` still showed two presentation faults after the duplicate hull
`190602` authority and moving-helm rewind were removed:

1. mounted components could slide relative to the hull while moving/turning;
2. near the end of settling, the ship could drift smoothly and then correct
   back several times.

This investigation separates the root `1130` stream from the mounted-member
`190602` stream. It does not treat a visual symptom as proof that the server
physics state moved backwards.

## Root stream evidence

The temporary passive client probe observed the stock
`SSPDeadReckoningVisualizer.AddControlPoint` boundary. During live movement:

- wire cadence was normally 240 ms;
- wire innovation was normally about 0.001 m;
- the client held four buffered points;
- smoothed server latency was about 540 ms; and
- the prediction gap was normally 0.009–0.020 m.

The server simultaneously reported a 22–29 ms RTT, no meaningful packet loss,
no fixed-clock pressure and the expected roughly 4.2 `1130` points per second.
This rules out a discontinuous authoritative root stream as the cause of the
large component separation.

## Proven post-settle heartbeat defect

After the hull reached exactly zero speed, the server continued sending one
zero-velocity `1130` point every five seconds. The passive client trace then
showed `halting=true` and a predicted gap growing to about 0.448 m.

The shipped `PathFollower.Move` no-motion fast path updates only the preceding
sample's timestamp when position and rotation remain inside its thresholds. It
does not replace that sample's velocity. A later heartbeat arriving after the
follower entered its halt branch can therefore rebuild a false
constant-velocity segment from the retained non-zero velocity, then spline the
hull back to the authoritative zero-speed point. That directly explains the
reported smooth drift followed by a hard correction near rest.

The correction removes the perpetual resting heartbeat after the existing
finite final zero-velocity repeats. This does not break checkout: the hull's
`1130` is freshly seeded from the latest `WorldEntities.TransformSeedFor`
position, which flight persistence updates every two seconds. Taking a resting
helm explicitly primes playback, and a sail/propulsion edge reactivates the
normal cadence.

## Render-phase mounted-member result

During the combined forward/left/right/settle run, a second temporary passive
probe compared every nearby `"~"` follower at render phase:

- rendered part pose versus the current hull PathFollower sample;
- rendered part pose versus the rendered hull transform;
- rendered hull versus PathFollower sample;
- part/root timestamps, update count and sleeping state; and
- Rigidbody existence, kinematic state and velocity.

It changed no pose, input, component or packet. After checkout/teleport outliers
were excluded, active-flight render alignment was conclusive:

- p95 member-to-rendered-root error was 0 m;
- p99 was at most 0.0008 m;
- ordinary maximum was about 0.0012 m;
- root and member Rigidbody interpolation were both `None`; and
- both Rigidbody-to-Transform gaps were 0 m.

Therefore active motion is not two server/client authorities fighting over the
mounted entity roots. They follow the rendered hull to about one millimetre.
Any remaining turn-only visual vibration must now be isolated below the entity
root (part animation, camera or internal prefab), rather than patched by moving
the whole part or changing the authoritative hull stream.

## Proven mounted-follower sleep tail

After the last finite root points, sail `3676` diverged gradually from 0.0008 m
to 0.1052 m and remained there. The root-to-PathFollower sample error stayed
zero, Rigidbody gaps stayed zero, and an actual Unity-child core stayed at zero.
This isolates a different lifecycle defect: the sail's own
`FixedUpdateLerpLocalTransformBehaviour` sleeps one second after its last
`190602`, while the root PathFollower can still spend its decompiled-default
five-second extrapolation and one-second halt window reaching the final pose.

The correction adds a bounded **member-only** drain. After the final root point,
only non-Unity-child mounted followers receive unchanged hull-local `190602`
updates for seven seconds (5 s extrapolation + 1 s halt + 1 s final follower
wake). No root `1130` and no engine-state update is emitted by this path. Moving,
manning, expiry and hull retirement all clear the deadline. Seven seconds is an
explicit WAReborn guard derived from decompiled client defaults; the release's
exact serialized ShipConfig remains lost.

## Verification so far

- focused rest-drain policy/wiring tests: 31/31;
- full multiplayer suite with the member drain: 4,891/4,891;
- Release game-server build: zero errors;
- temporary client authority probe build: zero errors.

The resting-root correction and mounted-follower drain are evidence-backed and
separate: the root stays silent while only its members finish following it. The
final live render-phase acceptance is the remaining gate.
