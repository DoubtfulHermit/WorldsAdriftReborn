# Client ship motion continuity — 2026-08-24

## Live evidence

After build `93ab672` removed the duplicate hull TransformState stream, the
24 August acceptance run showed:

- mounted parts no longer remained behind during forward acceleration;
- server flight speed settled monotonically and no relay drops, duplicates,
  jump rejects or fixed-clock pressure skips were recorded;
- the whole hull still advanced in small visible increments near rest; and
- mounted parts still shimmered relative to the hull during shallow turns.

The client log accepted every 1130 control point. A deterministic replay of the
live 3094 kg, one-engine drive and coast also proved the cubic Hermite path never
reversed during final settling. The remaining artifacts were therefore isolated
to client pose application, not authoritative motion or packet loss.

## Recovered receive-side thresholds

The shipped `PathFollower.Move` applies a new kinematic hull pose only when the
position differs by more than `0.0001` squared metres (1 cm) or rotation differs
by more than 0.1 degrees. Below 0.5 m/s, a 50 Hz position step is less than 1 cm,
so the desired path accumulates and is applied in visible centimetre batches.

The shipped `FixedUpdateLerpLocalTransformBehaviour` separately applies a
hull-relative entity only when a quaternion component differs by at least
`0.01`. WAReborn represents every mounted part as a separate `Parent(hull,"~")`
entity, so shallow parent rotation accumulates and the mounted set jumps together.
Retail's locally simulated single-Rigidbody ship hierarchy did not rely on this
topology for the local pilot view.

## Correction

`ShipPathFollowerContinuity_Patch` preserves the stock control-point spline,
remapping, timestamps and target pose. It repeats `MovePosition` and
`MoveRotation` with that exact finite target on each fixed update for a
`SSPDeadReckoningVisualizer` hull, including when the stock optimization skipped
the sub-centimetre/sub-0.1-degree step.

`ShipRelativeRotationContinuity_Patch` bypasses the coarse relative-transform
threshold only when all of the following are true:

1. the entity is explicitly hull-relative (`Parent.key == "~"`);
2. the exact parent has an active `SSPDeadReckoningVisualizer` and PathFollower;
3. that follower has a real rendered sample.

It does not rewrite local mount offsets, issue network commands, predict force,
or change the server's authoritative state.

## Acceptance

One client restart is sufficient:

1. with sails furled, throttle from rest and watch the hull through the first
   five seconds and the final sub-0.5 m/s coast;
2. hold a shallow left turn for ten seconds, centre it, then repeat right;
3. watch a deck edge and at least two mounted objects at different radii;
4. confirm the character remains at the helm and no component separates from
   its authored mount;
5. dismount at rest and confirm stationary parts remain stationary.

Pass requires no centimetre batching near rest and no relative mounted-part
shimmer during either shallow turn. Ordinary spline motion, server telemetry,
fuel and final rest must remain unchanged.
