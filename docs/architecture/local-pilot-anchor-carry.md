# Local pilot anchor carry

Status: implemented client-side; automated checks pass; live visual acceptance pending.

## Observed failure

On hull 3639, with both sails furled, commanding bare-hull throttle caused small
forward presentation steps for roughly 5–10 seconds. The gap between the local
player and helm grew on each step, then stabilized at constant speed. Repeating
the acceleration from rest reproduced it. Live server evidence ruled out clock
pressure and transport loss: fixed-clock dropped steps and pressure events were
zero, packet loss was zero, and the normal 240 ms ship control-point cadence was
maintained.

The slow bare-hull drift itself is intentional. `BareHullBaselineDriveTests`
pins the previously accepted rule that a sky core can move slowly without
engines or sails. This change does not remove or retune that behavior.

## Cause

Retail `ClientAuthoritativePlayerMovement.ShouldCorrectPosition` explicitly
disables its moving-ground correction while `PilotVisualizer` has a vehicle.
That is valid for retail: the ship is one locally simulated Rigidbody and
physics carries the pilot. Wareborn moves the authoritative hull through a
kinematic `PathFollower`, while the helm remains a separate `"~"` follower
entity. During a changing-velocity correction the helm advances but the pilot
Rigidbody is not carried by the same correction.

## Correction

`PilotBodyAnchorFollower` extends the existing one-time `#PilotPosition` anchor:

- it exists only for the local player while the native pilot reader names the
  exact hull captured on Man;
- every fixed step it places the local player Rigidbody at the shipped helm
  prefab's authored `#PilotPosition` and carries the anchor velocity;
- a late render pass removes a one-frame body/IK gap;
- it releases on the native dismount transition or any lost/changed lifecycle;
- a checkout-sized velocity discontinuity (60 m/s or more) is not injected into
  the player;
- it never writes ship state, sends a component update, moves another player,
  changes 1130 cadence, or changes flight forces.

The opt-in semantic bridge reports `pilotAnchorCarryActive` and
`pilotAnchorGapMeters` for unattended acceptance.

## Acceptance

One continuous client run is sufficient:

1. Man the helm while the ship is stopped. Confirm bridge state reports carry
   active and an anchor gap near zero.
2. With sails furled, command high throttle and watch through the first 10
   seconds. The body/camera must remain fixed to the helm with no growing gap.
3. Return to idle, settle, then repeat high throttle. The second acceleration
   must behave the same as the first.
4. Turn left and right while accelerating. Hands/body must remain on the helm;
   the deck must not slide under the player.
5. Dismount. Carry must report inactive, ordinary walking must work, and the
   player must retain the ship's motion rather than teleporting to the old pose.

Any remaining whole-hull or mounted-part shimmer with the measured anchor gap
at zero is a distinct PathFollower/`"~"` presentation issue, not pilot carry,
and should be recorded separately rather than broadening this patch.
