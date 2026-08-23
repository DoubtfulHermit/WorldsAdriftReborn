# Durable aboard login

As of 2026-08-24, logout-position rows retain both the last authoritative world
position and, when the server knows the player is aboard a persisted built ship,
the ship's durable index plus a hull-local Q52.12 position.

On login the ship index is resolved to the new boot's hull entity and the local
point is transformed through the hull's current position and rotation. The normal
ship checkout barrier then materializes the hull and deck before teleporting the
player. The restore grants no helm authority and does not synthesize an aboard
claim; those still come from ordinary client contact and interaction.

Failure is conservative. Legacy rows, deleted ships, missing live hulls, partial
anchors, and offsets outside 256 metres use the stored absolute world position.
Every periodic 20-second save and the final disconnect save refreshes or clears
the anchor, so walking ashore cannot leave a stale ship relationship behind.

This replaces the v5 limitation that only absolute XYZ could be stored because
ships had no durable identity at the time. Schema v10 is additive and old rows
remain valid.

The temporary bare-hull throttle experiment is also default-off. A hull with no
engine and no unfurled sail receives no throttle-requested drive. The explicit
`WAREBORN_FLIGHT_BARE_HULL_MULTIPLIER` switch remains available only for isolated
experiments.
