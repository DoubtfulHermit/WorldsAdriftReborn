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

## Mounted-member boundary still under measurement

During active flight the server sends about 15–19 mounted-member `190602`
updates with each root point. The hull's root `190602` is correctly absent, so
there is no longer a second absolute hull authority. A `"~"` member is composed
each fixed update from its unchanged hull-local pose and the hull
`PathFollower.PreviousSample`.

The remaining live question is whether the visible component separation is:

- one fixed-frame ordering difference between the hull Rigidbody and relative
  followers;
- a member TransformState update/smoothing reset; or
- a specific part Rigidbody/prefab behavior rather than the shared hierarchy.

A second temporary passive probe compares, for every nearby `"~"` follower:

- rendered part pose versus the current hull PathFollower sample;
- rendered part pose versus the rendered hull transform;
- rendered hull versus PathFollower sample;
- part/root timestamps, update count and sleeping state; and
- Rigidbody existence, kinematic state and velocity.

It changes no pose, input, component or packet. One client restart and one
forward/turn/settle run will identify which owner the components actually
follow before any mounted-part correction is attempted.

## Verification so far

- focused `FlightSession`/`FlightTuning` tests: 25/25;
- full multiplayer suite after removing the resting heartbeat: 4,882/4,882;
- Release game-server build: zero errors;
- temporary client authority probe build: zero errors.

The resting-heartbeat correction is evidence-backed. The active mounted-part
fix remains deliberately uncommitted until the second live probe answers the
ownership/order question.
