# Live flight settling trace — 2026-08-22

Status: **root cause proved; recovered correction implemented locally, not deployed**
Production build: `96fd0c2`  
Hull: entity `3639`, reported flight mass `3094 kg`  
Player: entity `3790`

## Reproduction

The player unfurled both sails, manned the helm and commanded full throttle. They
then returned the throttle to idle, left the helm, and furled both sails. The ship
was left untouched so the server's unforced settling path could be observed.

This is not a stale sail or throttle report:

- `13:05:31`: server input was `throttle=0`, with two sails still unfurled.
- `13:05:37`: sail `3680` became `FURLED`.
- `13:05:42`: sail `3679` became `FURLED`.
- Every subsequent sample reported `unfurled sails 0`, `1111 rx 0`, and
  `settling`. No propulsion command remained.

## Authoritative speed trace

| Time (CEST) | Horizontal speed | State |
|---|---:|---|
| 13:05:46 | 2.95 m/s | throttle idle, 0 sails |
| 13:05:51 | 2.52 m/s | settling |
| 13:06:01 | 1.99 m/s | settling |
| 13:06:22 | 1.46 m/s | settling |
| 13:06:42 | 1.18 m/s | settling |
| 13:06:52 | 1.08 m/s | settling |
| 13:07:12 | 0.68 m/s | settling |
| 13:07:27 | 0.21 m/s | settling |
| 13:07:32 | 0.06 m/s | final emitted settling sample |

The last sail furled at 13:05:42. The final emitted settling sample arrived 110
seconds later; the hull travelled approximately 150 m along X over that interval
and was down to 0.06 m/s. This independently reproduces the long low-speed tail
seen in the preceding acceptance run.

## Initial conclusion before the complete decompile comparison

The movement is real retained momentum under the current drag model, not hidden
engine force, a stale throttle, or an unfurled-sail ledger bug. That does **not**
establish that the behavior is retail-correct. The open question is whether the
retail Unity/GSim rigidbody also applied linear drag, sleep thresholds, contact
resistance, docking resistance, or another low-speed term that the scalar server
model lacks.

Do not tune the recovered `0.007 / 2.5` aerodynamic drag constants merely to make
this trace stop sooner. First recover or bound retail low-speed/sleep behavior,
then add a separately classified policy with replay and live acceptance evidence.
That comparison is completed below; it found an implementation discrepancy, not
a need for a new tuning constant.

## Second production reproduction

The later hull `3639` run independently produced the same curve:

- final sail furled at `14:01:33`;
- throttle `0`, unfurled sails `0` throughout the coast;
- `4.11 m/s` at approximately `14:01:35`;
- still `0.13 m/s` at approximately `14:03:25`;
- approximately `112 s` and `160 m` before rest.

The scalar model's pre-fix prediction from exactly `4.11 m/s` at the server's
`0.24 s` cadence is `114.96 s / 159.10 m`. That agreement is strong evidence
that the long tail is the force equation itself, not a hidden sail, engine,
throttle, replication or mass-ledger defect.

## Recovered discrepancy

The open retail question above is now settled by reading the complete shipped
method, not by tuning against the live complaint:

`WAReborn-decompiled/acs/Assets.Visualizers.Weather/WindPhysicsVisualizer.cs:72-90`
does the following on **every** `GetDrag` call:

1. calculate `0.007 * |relativeWind|^2.5` (with the primary direction zero at
   or below `0.1 m/s`);
2. clamp that acceleration to `|relativeWind| / dt`;
3. subtract its one-step velocity delta from the relative-wind vector;
4. add the remaining vector, capped to `0.03 * dt`;
5. divide that residual by `dt` and return both terms together.

There is no `< 1 m/s` condition and no throttle/sail/undriven condition. The
only nearby velocity branches are the unrelated non-floating-body sleep early
out and docking behavior.

WAReborn had inserted both gates. Its comment then claimed the final metre per
second would take "roughly six seconds". At `0.03 m/s^2`, even without considering
the power law, that interval is `1 / 0.03 = 33.3 s`. The implementation therefore
matched the long production tail and not the shipped method.

The local fix transcribes the recovered order exactly. It does **not** alter sail
power, engine power, the recovered `0.007 / 2.5` pair, the 0.03 magnitude, or the
wire cadence.

### Before/after coast matrix

Zero wind target, zero propulsion, trapezoidal distance over the same `0.24 s`
step used by production:

| Start | Previous gated model | Recovered every-step residual |
|---:|---:|---:|
| 0.50 m/s | 16.56 s / 4.09 m | 16.56 s / 4.09 m |
| 1.00 m/s | 31.68 s / 15.34 m | 31.44 s / 15.14 m |
| 2.00 m/s | 92.88 s / 98.59 m | 51.84 s / 44.61 m |
| 4.11 m/s | 114.96 s / 159.10 m | 67.68 s / 89.49 m |
| 8.00 m/s | 121.92 s / 197.79 m | 74.40 s / 125.92 m |
| 12.00 m/s | 123.84 s / 215.19 m | 76.08 s / 142.97 m |
| 30.00 m/s | 125.04 s / 239.28 m | 77.52 s / 167.12 m |

At retail's `0.02 s` physics cadence the `4.11 m/s` result is `67.88 s /
90.06 m`; the server-step reduction differs by only `0.20 s / 0.57 m`.

### Classification and remaining expectation

- **RECOVERED:** coefficient `0.007`, exponent `2.5`, residual acceleration
  `0.03 m/s^2`, primary-direction threshold `0.1 m/s`, every-step application,
  relative-wind direction, both anti-overshoot clamps.
- **MODEL REDUCTION:** projecting retail's vector rigidbody force onto the current
  longitudinal scalar axis and integrating it at the `0.24 s` control cadence.
- **WAREBORN OPERATIONAL POLICY, unchanged:** `ShipForceEvaluator` withholds
  ordinary ambient-wind carry from an abandoned hull so it can become exactly
  quiet instead of emitting forever.

This correction makes the observed coast about 47 seconds and 70 metres shorter,
but it does **not** create an arcade stop. Retail wing airbrakes activate only
when `dot(throttle * forward, velocity) < 0`: the pilot must command against
travel. Idle throttle with furled sails still coasts. A stronger idle brake would
be new WAReborn feel tuning and must not be mislabeled as recovered behavior.

### Rollout checklist

1. Merge this residual correction independently of any sail-power calibration.
2. Run the full Multiplayer suite and game-server Release build after combining
   branches; sail calibration consumes the corrected settled-speed prediction.
3. Deploy server-only with the guarded deploy script after confirming zero peers.
4. Reproduce from approximately `4 m/s`: idle throttle, leave helm, furl every
   sail, touch no controls.
5. Verify logs retain `throttle=0`, `unfurled sails=0`, `settling` and reach rest
   near `68 s / 90 m`, allowing small path/velocity-smoothing variance.
6. Separately test reverse-command airbraking once authentic wing force is live;
   do not treat that as part of this idle-coast correction.

## Separate visual replication defect observed during the run

The player also reported that individual ship components stayed behind the moving
hull and snapped forward on the next update. Wire counters corroborated the cadence
split: the hull published 1130 at about 4.2 Hz (`20–21` points per five seconds),
while the mounted-member 190602 bundle was deliberately limited to about 2 Hz.
There were no relay drops, duplicate drops, bad timestamp pairs, or pressure skips.

The assumption that an awake `"~"` follower would compose smoothly against every
intermediate hull point is therefore disproven in the live client. Moving domains
must publish root and mounted-member transforms in the same domain frame. The cheap
member heartbeat remains appropriate only after the complete ship is at rest.
