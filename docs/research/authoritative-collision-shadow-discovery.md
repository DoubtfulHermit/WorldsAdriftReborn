# Authoritative collision shadow: discovery, implementation, review and test record

Status: Track 5 shadow slice, 2026-08-22. Branch
`feat/authoritative-collision-shadow`, based on `origin/main` at `11931bd`.

This is deliberately **not an authoritative collision response**. It produces
bounded, deterministic contact observations and nothing else. It does not alter
`FlightSession`, an 1130 control point, damage, mounted parts, or aboard players.

## Period 1 — discovery

### Terrain facts actually available

- `docs/research/world-data/island-surfaces/` contains 255 extracted,
  TRS-composed LOD0 collision-surface JSON files (~45 MiB). They are offline
  evidence, not runtime collision meshes embedded in the multiplayer assembly.
- `IslandTerrainEnvelope` and `release-runtime-catalog.json` retain a measured
  island-local AABB for the complete active 254-island release catalogue, with
  Haven handled by its pinned special record. `IslandTerrainEnvelopes` also has
  explicit pinned records for the first-region islands.
- The release runtime catalogue has sparse `IslandShellPoint` samples for distant
  silhouettes and a reviewed landing point. The shell is a visual/radial outline,
  not a closed solid and not sufficient for collision response.
- Client-loaded island bundles contain the real colliders and have passed visual
  landing tests. The server cannot raycast those Unity colliders and currently
  has no heightfield or triangle query.

Conclusion: a world-space AABB translated from the extracted local envelope is
the only complete runtime terrain representation currently available. It is
conservative and will report empty air inside concave envelopes. Exact terrain
contacts require a reviewed, compressed server-side surface/heightfield artifact
in a later Track 5 slice.

### Ship and part facts actually available

- `CustomShipHullState.hullData` is decoded into `ShipPlan`; the client generates
  the visible mesh and real colliders. `ShipHullMetrics` recovers +Z bow, +Y up,
  2x world scale, beam, keel, section/deck extents and top deck plane.
- The server owns mounted-part parentage and local transforms in
  `ShipPartTransform`; mounted engines/sails therefore have usable local points.
- The current scalar `FlightState` owns global position, yaw and linear velocity,
  but no authoritative hull quaternion/angular velocity. Track 3 commit `5dcd7d2`
  introduces the required shadow quaternion/vector state, inertia and
  force-at-position model, but is intentionally not integrated here.
- A ship AABB can be supplied from server-owned hull measurements today. A
  rotation-aware conservative world AABB and mounted-part sub-proxies must be
  adapted from Track 3 after integration. Guessing vertical hull thickness or
  part prefab bounds in this branch would turn missing evidence into fake physics.

### Client reports and trust boundary

- 1130 ship control points are server-authored in the current flight path and
  carry pose/velocity used by clients for interpolation.
- Inbound 1073 `relativeTo`, `relativeBias`, and `isRelativeToShip` describe what a
  client says its avatar is standing on. The server already corroborates ship
  membership, but contact can flicker for 0.09–0.79 s and has a one-second grace.
- There is no authenticated inbound hull-impact manifold, impulse, or damage
  packet that can serve as collision authority. A client `relativeTo` report is
  useful later as comparison telemetry only; it must never create a hull/terrain
  proxy, a response impulse, damage, or boarding permission.
- Proxy geometry, velocity and identity in this slice are explicitly
  server-owned. Non-finite, excessive, duplicate and wrong-kind values are
  rejected before pair work.

Threat model: a malicious client can lie about being grounded, spam component
deltas, name another entity, or send invalid transforms elsewhere. Later wiring
must resolve entity ids through domain ownership, use the authoritative Track 2/3
state, rate-limit comparison reports and treat every client manifold as untrusted.

### Retail and lost behavior

The preserved client proves that retail generated hull mesh colliders
(`CustomShipFrameVisualizer` / `MeshGenerator`) and ran rigidbody collision in a
UnityWorker. Visualizers also consume velocities and parent/relative-ground state.
The lost GSim/UnityWorker owned authoritative collision resolution, crushing,
impact damage and part damage/detachment. This repository has no ship damage
service; recovered material resilience has no mechanism to attach to. Restitution,
friction, impulse caps, hull HP, damage thresholds and passenger inertial response
are therefore not recoverable values and are excluded.

### Chosen representation and broadphase

The first shadow representation is a conservative world AABB swept linearly over
one bounded step:

1. Sort accepted proxies by stable ordinal id.
2. Terrain first: swept hull AABB against static island-envelope AABB.
3. Hull-hull second: swept-AABB broadphase, then Minkowski-expanded slab test in
   relative motion.
4. Record time of impact in `[0,1]`, deterministic normal, conservative contact
   point, closing speed and initial-overlap flag.
5. Sort records by time, kind and stable ids.

Sweeping prevents a 60 m/s hull crossing a thin proxy between endpoints. AABBs are
deliberately conservative and orientation-free; they are safe for shadow
comparison, not acceptable for live impulses.

## Period 2 — coding

`CollisionShadow.cs` adds an engine-free module with:

- finite `CollisionVector3` and inclusive `CollisionAabb` primitives;
- typed server-owned hull/terrain proxies;
- deterministic terrain and moving-hull swept tests;
- stable, immutable contact records;
- supplied/accepted/rejected, broadphase/narrowphase, per-kind contact and
  current-versus-shadow comparison telemetry;
- hard limits: 4,096 supplied per class, 256 hulls, 512 terrain proxies, 16,384
  narrowphase candidates, 1,024 contacts, 250 m/s proxy speed, 0.25 s maximum
  step, 512 m half-extent, 100 km coordinate and 96-character id.

No production service constructs these proxies yet. The result has no response
API by design, making accidental live authority impossible in this commit.

## Period 3 — review

### Tunnelling and seams

Continuous linear sweeps cover endpoint tunnelling for translation, including the
required 60 m/s case. Inclusive slabs make exact face/edge grazing deterministic.
Adjacent terrain envelopes can emit two seam contacts; stable ids make their order
repeatable. Angular sweep is absent until Track 3 and can still tunnel a long,
fast-rotating hull.

### False positives and resting stability

Island and hull AABBs include empty corners and concavities. Shadow counts will
therefore over-report; this is explicit telemetry, not a blocking response. Resting
touch currently reports as initial overlap every sample. A live solver will need a
persistent manifold, separation slop, hysteresis and sleeping policy rather than
reapplying an impulse each 20 ms.

### Ordering and numerics

Ordinal ids make input order irrelevant. Axis ties retain X, then Y, then Z; exact
central overlap selects the negative X face. All positions, extents, velocities,
steps and derived magnitudes are finite/bounded before arithmetic. The slab uses a
fixed epsilon only for ordering/contact inclusivity, not a frame-dependent value.

### Energy and griefing

There is no impulse, restitution, friction or positional correction, so this slice
cannot inject energy. Those are the highest-risk future additions: restitution
above one, repeated initial-overlap impulses, correction against a moving body and
normal-sign errors can create energy. Client reports remain outside the evaluator.
Per-domain server ownership, response budgets and authority generations are
mandatory before responses become live.

### Complexity

Accepted cardinalities bound broadphase traversal; narrowphase and contact output
have lower explicit caps. Oversized batches are rejected before sorting. Capping
after stable sorting makes selected valid proxies deterministic. This all-pairs
shadow broadphase is suitable for bounded evaluation, not scale: Track 9 must add
a deterministic spatial index before hundreds of live bodies/islands interact.

## Period 4 — testing

`CollisionShadowTests` covers:

- 60 m/s terrain sweep and exact time/normal/contact point;
- inclusive grazing and duplicate island-seam contacts;
- stable initial overlap;
- two moving hulls in relative motion;
- permutation-independent contact order;
- separating/parallel false-positive control;
- non-finite, inverted, oversized, over-speed, wrong-kind and duplicate geometry;
- invalid/non-finite/oversized steps;
- dynamic, contact and hard-input performance caps;
- explicit current-authority versus shadow telemetry.

Executed results:

- collision-focused: 16/16 passed;
- complete multiplayer suite: 4,587/4,587 passed;
- login/admin suite: 1,227 passed, 26 intentional database-dependent skips;
- Release game-server build: 0 errors (62 pre-existing warnings);
- Release login/admin build: 0 errors (24 pre-existing warnings).

## Dependencies and next integration gates

- **Track 2:** required for the authoritative 50 Hz step timestamp, durable
  position/velocity input, replay identity and restart semantics. Do not evaluate
  live contacts from poll cadence.
- **Track 3 (`5dcd7d2`):** required to adapt `ShadowVector3`,
  `ShadowQuaternion`, centre/inertia, angular velocity and rotation-aware hull/part
  bounds without maintaining a second vector state. This branch intentionally
  duplicates only tiny engine-free math so it can remain based on `origin/main`;
  integration should add an adapter or consolidate the primitives.
- **Track 4:** required before vertical collision responses because lift, gravity,
  overload and core capacity determine whether a hull is resting, descending or
  intentionally sinking. Collision must not fake lift by holding an overloaded
  hull against an envelope.

After Tracks 2–4 integrate, the next Track 5 slice is still shadow-only: build
server-owned proxies from real runtime island/hull state, add rotation-aware
conservative bounds and publish comparison telemetry. Exact terrain artifacts,
then a reviewed response solver, come after measured false-positive/tunnelling
data. Damage, parts and aboard-player responses remain later gates.
