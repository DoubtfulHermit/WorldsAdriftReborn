# Retail flight world bounds

Status: implemented behind `WAREBORN_FLIGHT_WORLD_BOUNDS=1`; disabled by
default; not enabled or deployed by this changeset.

## Preserved evidence

The recovered client class
`/home/ttanurhan/Games/WAReborn-decompiled/acs/WorldEdgePushback.cs` applies the
following rule from Unity `FixedUpdate`:

- horizontal hard limit: `edgeLength / 2 - 300 m`;
- horizontal pushback begins 100 m inside that hard limit;
- vertical pushback begins above global `Y=800 m`;
- vertical hard limit: global `Y=1000 m`;
- normalized penetration: `t = clamp01((position - push) / (hard - push))`;
- after `t > 0.25`, outward velocity is multiplied by
  `1 - (t - 0.25) / 0.75`;
- inward velocity change per fixed evaluation is `50 * t² m/s`.

Those numbers and equations are recovered behavior, not server tuning.

The preserved release MapFile at
`docs/research/world-data/wamap-islands.json` says
`WorldInfo.WorldEdgeLength = 36000`. The release extent is therefore
`-18000..+18000 m`, with horizontal pushback at `±17600 m` and hard clamping at
`±17700 m`. The 36 km value is preserved world data rather than a recovered
constant of the pushback component.

## Revival integration

The current server emits ship control points every 0.24 seconds. Retail applied
world pushback through Unity's fixed-physics loop. When the feature is enabled,
the existing flight integrator and the pure `RetailWorldBoundsPolicy` run in
deterministic 0.02-second reference slices (normally 12 per control point). This
keeps the recovered per-fixed-step velocity-change equation meaningful without
claiming the rest of the reconstructed kinematic flight model is retail's 3D
rigidbody stack.

With the feature disabled, flight takes the exact pre-existing single-step path.
There are no extra substeps, sanitation changes, clamps, packets or client
components.

`WAREBORN_FLIGHT_WORLD_EDGE_LENGTH` may override the authored 36 km extent for a
different map. It is deployment configuration, not a feel-tuning knob. Missing,
non-finite or too-small values fall back to the release extent.

## Safety and observation

While enabled, any non-finite flight candidate is rejected. The hull is
quarantined at rest at its previous finite authoritative pose; if that pose is
also corrupt, the deterministic fallback is origin. This is deliberately gated
with the policy so default-off parity is exact.

Stats schema v17 adds one `worldBounds` block per ship domain. It reports:

- whether the policy is enabled and its exact configured thresholds;
- final signed distance to the nearest enforced hard boundary;
- the actual velocity delta applied during the latest cadence interval;
- whether any reference slice hard-clamped or quarantined invalid state;
- how many 20 ms reference slices were evaluated.

The login server rebuilds that block through an allowlist, and the authenticated
Simulation/Infrastructure ship detail renders it. It is observation, not a
control surface.

This slice does not add component 1250, client changes, collision, quaternion or
6DOF physics, a general fixed-step accumulator, live configuration, deployment,
or authority migration.
