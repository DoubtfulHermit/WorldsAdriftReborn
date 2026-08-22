# Live flight settling trace — 2026-08-22

Status: **reproduced; open for retail comparison**  
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

## Current conclusion

The movement is real retained momentum under the current drag model, not hidden
engine force, a stale throttle, or an unfurled-sail ledger bug. That does **not**
establish that the behavior is retail-correct. The open question is whether the
retail Unity/GSim rigidbody also applied linear drag, sleep thresholds, contact
resistance, docking resistance, or another low-speed term that the scalar server
model lacks.

Do not tune the recovered `0.007 / 2.5` aerodynamic drag constants merely to make
this trace stop sooner. First recover or bound retail low-speed/sleep behavior,
then add a separately classified policy with replay and live acceptance evidence.

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
