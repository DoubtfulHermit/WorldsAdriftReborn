# Sail-speed calibration — 2026-08-22

## Verdict

The live ship was slow because the configured model was doing exactly what its
numbers predict, not because a sail was missing. Production had no
`WAREBORN_FLIGHT_SAIL_POWER` override, both `Sail01` entities were unfurled, and
the 3,094 kg total flight mass included the hull plus all 21 mounted parts. From
14:00:41 to 14:01:19 two sails accelerated it from 0.98 to 5.43 m/s while its
heading changed from -87 to -139 degrees. The 420-power model predicts 5.93 m/s
settled at -139 degrees once retail's residual drag term is included.

This change raises only the lost, server-authored `SailState.Power` analogue
from **420 to 840 N/(m/s) per sail**. It does not alter wind, mass, drag,
settling, engines, the speed clamp, or the number/state of sails.

## Why 840

- Recovered and unchanged: sail trimming/keel geometry, minimum efficiency
  0.3, wind `(1,0,-2)` (2.236 m/s), drag `0.007 * |u|^2.5`, and mass attenuation.
- Lost: retail's actual `1303 SailState.Power`; it was authored by GSim and is
  absent from the client assets.
- Preserved official patch evidence gives one exact historical bracket:
  Update 27 build 989 says, “Halved wind power, which functionally halves
  thrust from sails.” Thus 420 and 840 represent two known retail balance eras.
- The lower member was live-tested and rejected as too slow. Selecting 840 is
  therefore an evidence-linked balance change, not a change to recovered
  physics.
- At 800 kg, four sails settle near 38 knots: above the shipped helm VFX's
  30-knot “fast” mark and well below the 70-knot (36 m/s) airspeed dial.
- A community report says a 69-sail ship approached 70 knots. With our
  deliberately conservative placeholder of 50 kg for every mounted part, 840
  predicts about 57 knots. That approaches the observation without fitting an
  unknown retail part mass as though it were exact.

The stronger 1,400–1,500 candidate can be made to fit that 69-sail anecdote
exactly under our placeholder mass. It was rejected: it would overfit two lost
values at once (sail power and sail mass), make one sail stronger than an engine
by a wide margin, and push an 800 kg/four-sail rig to roughly 46 knots before a
live intermediate test.

## Matrix

These are continuous-heading minimum/maximum settled speeds in m/s. They use
the exact recovered residual correction (`+0.03 m/s²` every physics step), not
the old gated stopping approximation. Exact downwind is the minimum because the
retail keel subtraction removes almost all lateral sail force there.

| total mass | 1 sail | 2 sails | 3 sails | 4 sails |
|---:|---:|---:|---:|---:|
| 595 kg | 1.99–13.42 | 1.99–17.11 | 1.99–19.78 | 1.99–21.95 |
| 800 kg | 1.90–12.05 | 1.90–15.32 | 1.90–17.70 | 1.90–19.63 |
| 3,094 kg | 0.94–6.75 | 0.94–8.69 | 0.94–10.09 | 0.94–11.22 |
| 4,000 kg | 0.56–5.78 | 0.56–7.54 | 0.56–8.80 | 0.56–9.83 |

Maximum sail-only propulsion acceleration in m/s² across headings:

| total mass | 1 sail | 2 sails | 3 sails | 4 sails |
|---:|---:|---:|---:|---:|
| 595 kg | 3.125 | 6.251 | 9.376 | 12.502 |
| 800 kg | 2.325 | 4.649 | 6.974 | 9.298 |
| 3,094 kg | 0.601 | 1.202 | 1.803 | 2.404 |
| 4,000 kg | 0.465 | 0.930 | 1.395 | 1.860 |

The user's two-sail hull across eight headings, showing acceleration and
settled speed after the change:

| yaw | acceleration | settled speed |
|---:|---:|---:|
| 0° | 0.083 m/s² | 3.19 m/s |
| 45° | 0.415 m/s² | 5.91 m/s |
| 90° | 0.878 m/s² | 7.75 m/s |
| 135° | 1.182 m/s² | 8.64 m/s |
| 180° | 1.150 m/s² | 8.55 m/s |
| 225° | 0.799 m/s² | 7.49 m/s |
| 270° | 0.336 m/s² | 5.47 m/s |
| 315° | 0.058 m/s² | 2.68 m/s |

At the observed final heading of -139 degrees, the exact before/after is:

| | 420 | 840 |
|---|---:|---:|
| sail force | 1,297.46 N | 2,594.91 N |
| propulsion acceleration | 0.41935 m/s² | 0.83869 m/s² |
| settled speed | 5.92881 m/s (11.53 kn) | 7.62345 m/s (14.82 kn) |

## Risks and acceptance

The power remains WAReborn tuning. Heading matters materially; comparing two
runs on different points of sail can obscure the change. The current scalar
integrator reproduces recovered longitudinal geometry but still omits
mount-position torque and full vector rigidbody motion.

For live acceptance, use the same hull and no engines: start at rest, unfurl
one sail for ten seconds, unfurl the second, hold approximately -139 degrees for
at least 25 seconds, and record the five-second flight lines. Expect roughly
double the initial sail acceleration and a settled speed near 7.6 m/s. Then turn
toward 135 degrees and expect the ship to approach 8.6 m/s. Furl both sails and
evaluate stopping separately; this commit intentionally cannot change it.
