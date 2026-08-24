# Flight publication phase lock

## Live evidence

During the 2026-08-24 Pack E acceptance, the client rendered persistent
vibration while turning and occasional corrections near rest even after duplicate
root authority and mounted-member lag were removed. Passive client probes ruled
out the pilot/camera LateUpdate order and showed mounted parts within millimetres
of the rendered hull root.

The remaining 1130 stream exposed the server defect: nominal 240 ms turn points
contained 11, 12, or 13 completed 20 ms physics steps. They were nevertheless
presented on the stock point cadence, producing alternating measured turn rates
around 18–23 degrees/second for a nominal 20 degrees/second command and correction
spikes during final slowdown.

## Cause

`FixedFlightClock` and the legacy 240 ms `CadenceTimer` were independent. The
service advanced every available fixed step, then used the unrelated wall timer
to decide whether that resulting state became a control point. This made the
physical interval represented by a point disagree with its playback interval.

## Correction

With `WAREBORN_FLIGHT_FIXED_STEP=1`, each control point is now emitted only after
exactly 12 completed 20 ms steps. Poll batches are split at boundaries: 13 steps
become `12 + emit`, then `1 + no emit`; a capped 25-step catch-up becomes two
exact publications plus one retained step. Phase-locked timestamps advance by
exactly 240 ms even when the hosting poll arrives late.

The fixed-step-off path remains on the legacy wall-clock cadence. Membership,
docking capture, and helm echo also remain paced by the legacy timer because they
do not determine the root physics sample.

## Verification

- Arbitrary poll groupings produce publications at simulation steps 12, 24, 36,
  and 48 and the same turn states as individual 20 ms calls.
- A 13-step grouping cannot publish all 13 steps as one 240 ms point.
- Delayed phase-locked stamps stay 240 ms apart; the legacy caller retains its
  prior wall-clock spacing.
- Mutation from a 12-step to 13-step boundary fails all three boundary tests.
- Full multiplayer suite: 4,896 passed before the final documentation-only pass.

## Live acceptance

One combined real-eyes run is still required after deployment:

1. Board the same equipped ship and enter the helm.
2. Move forward at full throttle for at least 15 seconds.
3. Wind A to a steady turn, release A, observe five seconds, then use D to centre
   the latched rudder and observe another five seconds.
4. Return throttle to neutral, furl both sails, leave the helm, and watch through
   final rest.
5. Report turning vibration separately from slowdown correction. Normal retained
   steering after releasing A is not a failure; A/D author the latched rudder.
