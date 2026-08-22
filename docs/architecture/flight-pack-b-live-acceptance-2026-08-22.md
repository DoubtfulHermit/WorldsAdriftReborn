# Flight rollout Pack B — live acceptance, 2026-08-22

Production build `e17cc53` was tested continuously by Hermit on hull `3639`, a
3,094 kg two-sail ship. The run used the recovered 0.007 / 2.5 drag law, the
retail residual-drag correction, 840 N/(m/s) per sail, 2.236 m/s ambient wind,
and zero engine throttle. No client patch was involved.

## Results

- At rest with both sails furled and the helm neutral, the ship did not drift.
- One sail at yaw -119.5 degrees accelerated smoothly from 0.51 to 4.84 m/s.
- Unfurling the second sail increased speed smoothly to 6.33 m/s (12.3 kn) at
  the same heading.
- During the heading sweep the ship reached 7.97 m/s (15.5 kn) near 164 degrees
  and fell toward 3.02 m/s near -56.5 degrees. Throttle stayed exactly zero.
- Furling one sail reduced speed smoothly from 2.95 to 2.28 m/s while the other
  sail remained open.
- The final sail was furled at 1.99 m/s. Speed reached 0.03 m/s after about 51
  seconds and the domain then entered resting cadence. The measured displacement
  was approximately 44 metres.
- The player reported smooth motion, no hull snapping, no mounted-part lag and
  no component separation throughout the straight runs, heading sweep and coast.
- The final coast was judged acceptable by the player.

## Verdict

Pack B passes. The sail calibration stays at 840 and the retail residual-drag
transcription stays enabled. This acceptance does not validate passenger
coherence because no second client participated.
