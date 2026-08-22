# Flight reconstruction wave 2: PURE/SHADOW integration review

Status: locally integrated and testable; **not approved for live authority**.

This review is based on reviewed wave-one commit `bda1f15` and combines Tracks
3–6 without referencing `FlightSession`, packets,
components, persistence writes, or a wall clock. Track 2 remains the sole outer
20 ms accumulator. Its durable flight snapshot stays version 1 and scalar; none
of these shadow tracks extend or silently reinterpret it.

## Integrated order

For each future fixed step, the required order is:

1. Evaluate recovered engine/sail geometry with Wareborn power tuning.
2. Feed the resulting vertical propulsion force into the lift policy.
3. Apply gravity exactly once inside the lift policy.
4. Integrate the next velocity and predicted position.
5. Sweep collision from the pre-step bounds using that integrated velocity.
6. Derive a cap-aware collision-clearance record.
7. Permit docking capture only from a matching stable-key clearance record.

`IntegratedFlightShadow` encodes steps 1–5 as a pure comparison seam. It rejects
a non-zero `LiftGravityInput.ExternalVerticalForceNewtons`, because the Track 3
vector result is the only external vertical force accepted at this boundary. This
closes the most likely double-gravity/double-force integration error.

## Reconciled representations

- `ShadowVector3` is the single vector primitive for rigidbody, collision, docking
  pose, and docking velocity policy.
- Collision no longer owns a parallel `CollisionVector3` implementation.
- Docking JSON stores stable hull/yard keys. Runtime entity IDs remain arguments
  to the in-memory registry and are never emitted in `DockingSnapshotV1`.
- `DockingSnapshotV1` must later be added as a nullable sibling of Track 2's
  `DurableShipFlightSnapshot`; it must not change that DTO's version-1 meaning.
- Old production records have no docking snapshot and therefore migrate as
  undocked. No synthetic dock association may be inferred from proximity.

## Collision and clearance limits

The evaluator is deliberately conservative and bounded:

- 256 accepted dynamic proxies;
- 512 accepted terrain proxies;
- 16,384 candidate pairs;
- 1,024 contacts;
- 4,096 hard input records per collection;
- 250 m/s proxy speed and 250 ms maximum comparison step.

A truncated, capped, or hard-rejected batch can never produce a clear docking
record. The expected shipyard *capture-volume* contact may be excluded; this must
not be wired to the shipyard's physical solid proxy. Conservative world AABBs can
produce false positives for rotated hulls and irregular terrain, so this track is
valid for shadow telemetry and fail-closed docking gates, not collision response.
Oriented hull proxies and extracted terrain geometry are prerequisites for live
collision authority.

## Provenance

- Force geometry, torque filtering, lift caps, abandonment behavior, and dock
  interpolation are labelled **RECOVERED** where supported by surviving code.
- Propulsor power, gravity magnitude, capture/release tolerances, collision proxy
  geometry, overload migration floors, and damage remain **WAREBORN tuning**.
- Lift-capacity audits are explicitly lower bounds where crafted core
  material/quality was not persisted.

## Transaction and authority hazards

`ShipDockRegistry` provides bidirectional exact-pair claim/release semantics on the
current single poll loop. It is not a distributed transaction. Before live wiring:

1. the domain gateway must serialize claim, capture, component publication, and
   snapshot mutation under one authority generation;
2. stale clear/release commands must carry the expected authority epoch;
3. a capture-volume key must be distinct from solid yard collision geometry;
4. restore must resolve stable keys to newly allocated runtime IDs before claiming;
5. claim success followed by publication/persistence failure needs rollback or an
   idempotent recovery journal;
6. worker handoff must transfer the registry association with the ship domain.

## Go / no-go

GO for continued PURE/SHADOW comparison and admin telemetry after the combined
test suite remains green.

NO-GO for live hull motion, collision response, damage, docking component writes,
or snapshot schema changes. Live enablement requires Track 2 fixed-step acceptance,
oriented/extracted collision geometry, domain-gateway transactions, durable DTO
review, and replay/two-client acceptance tests.
