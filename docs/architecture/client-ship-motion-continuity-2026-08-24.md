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

## Next diagnostic boundary

Do not lower or bypass either threshold again without separately measuring:

1. hull Rigidbody target, rendered pose and interpolation mode;
2. the real Unity-child deck pose and local player contact-relative pose;
3. one `"~"` helm and one distant mounted component's composed target/rendered
   pose; and
4. the exact frame on which local-player correction or relative smoothing resets.

The safe production behavior remains the pre-trial client plus the already
accepted single authoritative hull stream. Remaining low-speed and turning
artifacts stay open.
