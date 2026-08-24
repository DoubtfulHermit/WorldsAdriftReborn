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

## Rejected continuity trial

Client manifest `2026.08.24-1` attempted to bypass both receive-side thresholds
by repeating the exact hull `MovePosition`/`MoveRotation` target every fixed
update and forcing active `"~"` followers to apply their composed target every
fixed update. Automated tests and compilation passed, but live acceptance
failed.

The continuous centimetre stepping disappeared. In its place, the local player
and independently followed ship structure drifted smoothly relative to one
another and then hard-corrected two or three times before the stationary pose.
The server trace remained monotonic from 3.58 through 0.01 m/s with no reverse,
authority correction, dropped control point or fixed-clock pressure. This proves
that blindly defeating the client thresholds exposes a contact/carry and
relative-follower disagreement; it is not a valid correction.

The manifest was withdrawn during the same test and restored to
`2026.08.23-5`. The client patch classes were removed from source. The Hermite
non-reversal regression remains because it records a useful negative result.

## State-coherent low-speed correction

The rejected trial repeated only `MovePosition` and `MoveRotation` after the
stock method returned through its no-motion optimization. It therefore left the
PathFollower's `PreviousSample` position/velocity and the Rigidbody velocity on
the older sample. The recovered local-player ground path reads those exact
values when deciding how a character standing on the ship should move. The
trial made the rendered hull continuous while leaving its contact/carry state
stale, which explains the observed smooth separation and later correction.

`ShipPathFollowerStateCoherence_Patch` does not implement a second movement
path. While a finite authoritative ship sample still has non-zero velocity or a
real rotation delta, it refreshes the PathFollower's own one-second
`_disableRigidbodyUpdatesTimer`. The original `PathFollower.Move` then performs
its normal complete branch in the original order: pose, rotation, velocity and
`PreviousSample`. The timer is allowed to expire after the final stationary
sample. Server drag, force, wire cadence and spline data are unchanged.

This is deliberately hull-only. The separate mounted-part rotation threshold
is not bypassed again until the hull/player correction has passed live
acceptance.

## Next diagnostic boundary

Do not lower or bypass either threshold again without separately measuring:

1. hull Rigidbody target, rendered pose and interpolation mode;
2. the real Unity-child deck pose and local player contact-relative pose;
3. one `"~"` helm and one distant mounted component's composed target/rendered
   pose; and
4. the exact frame on which local-player correction or relative smoothing resets.

Live acceptance must confirm that the final sub-0.5 m/s coast stays continuous
for both a helm-attached and deck-standing player, without the rejected trial's
player/structure separation. Turning shimmer remains a separate open gate.

## Instrument-only turn vibration follow-up

The subsequent 4x bare-hull acceptance passed the final coast: authoritative
speed fell monotonically from 1.92 m/s to rest, every domain frame was delivered,
and the player saw no late snapping or stutter. During yaw, however, the five
flight instruments visibly vibrated more than the hull and deck.

That difference has a concrete topology cause. Bar pipes are real Unity children
of the hull and therefore share its transform directly. Instruments were still
seeded as independent `Parent(hull, "~")` followers. The shipped
`FixedUpdateLerpLocalTransformBehaviour` accumulates shallow rotation until a
quaternion component differs by 0.01, so a gauge and the real-child pipe under it
advance on different visual thresholds during a turn. Retail mounted inert ship
parts into the ship hierarchy; they were not separately simulated rigidbodies.

The correction makes exactly the five `ShipInstruments.SchematicIds` real hull
children through the existing `MountedPartHierarchy` policy. It also excludes
them from mounted-member transform wakes, preventing `ParentUpdated` from
unparenting and reparenting them every flight frame. Helm, sails, engines, wings,
generators and every other physics-bearing part remain independent followers.
The existing hull-local persisted pose is unchanged; checkout after restart
selects the new hierarchy key without migrating world-state JSON or client code.
