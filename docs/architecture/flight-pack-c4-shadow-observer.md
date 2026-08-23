# Pack C4 — live flight shadow observer

Status: implemented behind `WAREBORN_FLIGHT_SHADOW_OBSERVE=1`; observation only.

First production observation found a required dependency: an unfurled sail's
retail force uses the live `YawJoint` angle, while the server currently owns only
its static packed mount rotation. The static adapter reported zero vector force
beside 1852.81 N scalar force. Schema 19 therefore fails closed with
`vectorAvailable=false` and `live-sail-yaw-state-unavailable` whenever any sail
is unfurled. This is the first useful C4 result, not a pass: authoritative,
durable sail-yaw state is required before sail force/torque comparison can pass.

The adapter feeds the existing pure `VectorRigidBodyShadow` with the live hull's
measured dimensions, authoritative mounted-part offsets and packed rotations,
current sail states, engine availability, throttle, and the exact retained wind
sample used by scalar flight. It publishes scalar forward force beside vector
force, raw `r × F` torque, retail-filtered torque, accepted/rejected propulsors,
and explicit approximation provenance in authenticated ship-domain telemetry.

It cannot write `FlightSession`, persistence, authority, components, or network
packets. `WAREBORN_FLIGHT_SHADOW_OBSERVE` defaults off. The enabled path is called
only while the existing stats snapshot is assembled, not from the simulation tick.

Collision reporting is intentionally incomplete. The current adapter evaluates a
server-authored conservative AABB for the selected hull, but terrain proxies and
multi-hull batching are not wired. Telemetry therefore says
`collision-hull-only; terrain-proxies-unwired` and `terrainAvailable=false`.
This is C4 force/torque observation evidence, not permission to enable collision
response, damage, docking, or vector motion.

## One-boot acceptance

1. Open Admin → Simulation and select the player's ship.
2. Confirm `C4 flight shadow` says `observing` and no input is rejected.
3. With all propulsion neutral, record scalar/vector force and torque.
4. Unfurl one sail, then both sails; record force and torque after each change.
5. Man the helm and test forward, neutral, reverse, left and right while watching
   the values update. Gameplay must remain indistinguishable from Pack C0.
6. Confirm Collision shadow explicitly says terrain `UNWIRED`; any claim of live
   collision or terrain availability is a failure.
7. Copy the incident bundle. Mark visual smoothness `REAL EYES` if the unattended
   bridge cannot establish it confidently.

Rollback is setting `WAREBORN_FLIGHT_SHADOW_OBSERVE=0` and performing an approved
restart. No client patch or world-state migration is involved.
