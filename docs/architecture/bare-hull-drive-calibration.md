# Bare-hull drive calibration

Status: implemented as an explicit server tuning seam; production rollout target
is `WAREBORN_FLIGHT_BARE_HULL_MULTIPLIER=2` after automated verification.

The 2026-08-23 Pack C/B follow-up measured hull 3639 (3094 kg, zero sails,
zero engines) at a stable 0.73 m/s under full forward throttle. That is the exact
result of the current recovered mass attenuation applied to the client fallback
wind magnitude of 2.236 m/s. The trace therefore showed no missing input, stuck
helm, or failed force integration. The player acceptance verdict was that the
result was visibly smooth after the pilot-anchor client fix, but too slow compared
with remembered retail behavior.

Retail ordinary-weather magnitudes are unavailable. Raising
`WAREBORN_FLIGHT_WIND_SPEED` would also strengthen sails and make server wind
disagree with the unchanged client weather presentation. The new multiplier is
therefore deliberately narrower and labelled WAReborn balance tuning. It applies
only when positive throttle requests baseline motion and no sail is producing
force. It does not change:

- mounted engine thrust;
- unfurled sail force or sail wind carry;
- drag or residual settling;
- wind-wall influence;
- the wind field or visible client wind.

The default is `1`, preserving previous behavior. Inputs are finite and clamped
to 0..4. The proposed production value `2` moves this exact heavy hull from about
0.73 to 1.46 m/s (1.4 to 2.8 kn), still below the client's 5-knot helm-wind VFX
threshold and well below sailed flight.

Automated acceptance pins default parity, clamping, the exact 2x bare-hull change,
and unchanged canvas force/carry. The remaining `REAL EYES` check is one continuous
boot: full throttle with both sails furled should feel clearly faster than the
recorded 0.73 m/s run, remain smooth, and remain obviously slower than one sail.
Rollback is setting the variable to `1` and restarting the game server.
