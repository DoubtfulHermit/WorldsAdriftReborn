# Pack C4 — live flight shadow observer

Status: implemented behind `WAREBORN_FLIGHT_SHADOW_OBSERVE=1`; observation only.

The first production observation found an adapter mismatch: an unfurled sail's
retail force uses its trimmed `YawJoint`, while the vector adapter used only its
static packed mount rotation. That reported zero vector force beside 1852.81 N
scalar force. The recovered scalar model already evaluates the equilibrium trim
as an explicit approximation. Retail actually used
`Slerp(current,target,6*deltaTime)` on render steps, and the server does not own
that transient state. The corrected vector adapter now derives that same recovered
equilibrium joint target from the exact mount quaternion and the
retained runtime wind sample. Telemetry labels this
`vector-equilibrium-trim-shadow; dynamic-sail-yaw-unavailable`; it does not claim to own
render-frame flutter or a durable per-frame joint angle.

The adapter feeds the existing pure `VectorRigidBodyShadow` with the live hull's
measured dimensions, authoritative mounted-part offsets and packed rotations,
current sail states, engine availability, throttle, recovered equilibrium sail trim,
and the exact retained wind
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
2. Confirm `C4 flight shadow` says `observing`, includes
   `vector-equilibrium-trim-shadow`, and no input is rejected.
3. With all propulsion neutral, record scalar/vector force and torque.
4. Unfurl one sail, then both sails; record force and torque after each change.
5. Man the helm and test forward, neutral, reverse, left and right while watching
   the values update. Gameplay must remain indistinguishable from Pack C0.
6. Confirm Collision shadow explicitly says terrain `UNWIRED`; any claim of live
   collision or terrain availability is a failure.
7. Copy the incident bundle. Mark visual smoothness `REAL EYES` if the unattended
   bridge cannot establish it confidently.

The observer may show a real diagonal-upwind force delta. Retail floors the force
after keel projection; the older scalar 2-D shortcut floors efficiency before
projection. Automated tests pin both the matching cardinal cases and this known
upwind difference so it is measured rather than silently normalized away. C4 is
a pass when telemetry remains finite and causally correct through the sequence;
it is not a requirement that an acknowledged scalar approximation equal the more
literal vector transcription at every heading.

Rollback is setting `WAREBORN_FLIGHT_SHADOW_OBSERVE=0` and performing an approved
restart. No client patch or world-state migration is involved.
