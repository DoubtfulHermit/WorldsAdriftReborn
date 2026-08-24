# Flight hull single-pose-authority correction

## Live observation

Pack E0 acceptance on hull `3639` exposed two distinct visual symptoms:

- the helm and mounted structure vibrated during sustained left/right turns;
- near the end of an unpowered coast, below roughly 1 m/s, the hull made small
  forward correction snaps before reaching rest.

The server trace stayed healthy throughout. Relay drops, duplicate packets and
  sequence jumps were zero, the fixed-step clock recorded no pressure skips,
  and the state remained finite. The low-speed trace decayed monotonically from
  0.90 m/s to rest.

## Cause

An active hull was receiving two independent pose streams:

1. component `1130`, consumed by the retail `PathFollower` spline/dead-reckoning
   path; and
2. component `190602`, consumed by
   `FixedUpdateLerpLocalTransformBehaviour` as an absolute root transform.

Both client behaviours call `MovePosition`/`MoveRotation`. The delayed spline
pose and current absolute pose therefore competed on the same rigidbody. The
error was easiest to see while yaw changed, and again when the ship moved only
a few centimetres between control points.

Mounted `~` parts do not require the duplicate root transform. Their followers
compose their local transform against the parent's
`SSPDeadReckoningVisualizer.NextFramePosition/Rotation`. They do still need
their own unchanged `190602` values while the hull is moving, otherwise the
stock client sleeps them after one second.

## Correction

Active flight now publishes exactly one hull pose authority: the component
`1130` root stream. It continues to publish mounted-part `190602` wake values on
every moving root point and on the resting heartbeat. Static Unity children are
unchanged.

This is a server-only correction. It changes no protocol schema, persistent
state, client DLL or patch manifest.

## Verification

- focused flight/domain/control tests: 109/109;
- full multiplayer suite: 4,876/4,876;
- Release game-server build: zero errors;
- source contract prevents reintroducing a hull auxiliary transform beside the
  active `1130` stream.

## Live acceptance

One game boot is sufficient:

1. board hull `3639`, keep sails furled, and run full engine throttle for about
   ten seconds;
2. hold a moderate left turn for ten seconds, then a moderate right turn for
   ten seconds;
3. return steering and throttle to neutral, furl any open sails, and remain
   aboard until the ship reaches rest;
4. watch the helm/deck/mounted parts during both turns and again below 1 m/s.

Pass requires no turn vibration, no low-speed forward snaps, and no mounted
part lag or detachment. Rollback is the single correction commit plus a game
server restart.
