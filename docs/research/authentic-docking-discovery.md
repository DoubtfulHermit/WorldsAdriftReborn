# Authentic docking: discovery, preparation, review and test record

Status: pure preparation on `feat/authentic-docking`; no live service wiring,
push, merge, deployment, restart or client-manifest change.

## Period 1 — discovery

### Recovered retail evidence

The stock component contract already defines the lifecycle the current server
does not model. Decompiled `DockableStateData` (component 1114) contains
`dockEntityId`, `dockLocation`, `docked` and `approachingDock`; none is a guessed
extension. On the authoritative Unity worker, `ShipControlVisualizer`:

- calls a ship “docking” only while `approachingDock` is true and squared driving
  force is below `1`;
- enables its `DifferentialMovement` position controller and targets
  `dockLocation` while approaching;
- disables wind physics while `approachingDock` is true;
- zeros both linear and angular velocity when `docked` becomes true;
- while docked, moves position with `Lerp(..., 5 * fixedDeltaTime)`, rotates by
  shortest-arc `Slerp(..., 5 * fixedDeltaTime)`, and zeros both velocities every
  fixed update;
- enables self-righting during approach.

The separate recovered `Docking.Approach` routine removes lateral velocity,
computes a braking-distance decision on target distance, and applies attraction
outside a threshold. Its acceleration, target speed, attraction and threshold
are parameters; their serialized values did not survive in the source dump.

The client `Shipyard` default `ImpactRadius` is 35 metres. The archived retail
Shipyard guide says to manoeuvre close, cut all propulsion and let the yard drag
the ship into position. Update 30 says a ship asks permission before approach,
the yard may refuse, and docking is limited to one's own or abandoned yards.
Update 29 says the optimized search may take up to three seconds; this is search
cadence evidence, not permission to invent a three-second capture animation.

### Current Wareborn inventory

- `ShipFlightService.TryCaptureAtEmptyShipyard` searches every yard in entity-id
  order, requires exact owner equality, a neutral input, an at-rest hull and a
  nine-metre radius, then teleports with `FlightSession.DockAt`.
- `ShipDockRegistry` is already bidirectional, but `SetDocked` evicts the previous
  pairing on either side. That is useful for legacy repair but unsafe as a live
  concurrent-claim primitive.
- First non-neutral helm input or sail wake marks departure. The yard remains
  occupied until the hull crosses the 18-metre rearm radius; then runtime and
  persisted links and components 1114/1205 are cleared.
- Build/re-capture, station pickup, salvage and admin recall all mutate the same
  dock ledger through separate call paths.
- Ownership is a character UID. A shipyard has an owner and per-player visitor/
  build-access state, but crew authorization needs an adapter to the social
  source of truth; it must not be inferred from a client claim.
- Persistence stores the owning built-ship list index and the shipyard's exact
  Q52.12 position. Runtime entity ids are newly allocated on restore, and the
  yard is resolved by that exact position. A durable docking DTO must therefore
  never persist runtime hull/yard entity ids.
- Disconnect neutralizes transient pilot authority but must not itself undock a
  ship. Deletion/salvage and yard pickup/destruction must release the exact pair.

### Evidence classification

| Item | Classification |
|---|---|
| 1114 fields and 1205 singular docked ship | recovered protocol |
| explicit approach then docked lifecycle | recovered structure |
| zero linear/angular velocity when docked | recovered behavior |
| docked pose/yaw convergence rate `5/s` | recovered client code |
| 35 m yard impact radius default | recovered client default; prefab override still possible |
| owner/abandoned permission handshake | recovered patch-note behavior |
| approach acceleration/speed/attraction values | lost serialized tuning |
| capture radius, angular tolerance and snap epsilon | lost; Wareborn tuning |
| 18 m departure release radius | existing Wareborn tuning |
| collision-clearance representation/order | Track 5 dependency, not yet recovered |
| abandonment timeout and claim semantics | lost server policy |

## Period 2 — coding

`AuthenticDockingLifecycle` is an engine-free aggregate with explicit
`Undocked`, `Approaching`, `Captured`, `Docked` and `Departing` states. It:

- validates yard existence, neutral propulsion, owner/crew/abandoned permission,
  approach range/speed and an authoritative collision-clearance input;
- reserves one yard/hull pair before approach;
- freezes linear and angular velocity at capture;
- converges pose and shortest-arc yaw deterministically at the recovered rate;
- lets sail, engine or both begin departure while retaining occupancy until the
  release envelope is clear;
- fails closed on non-finite data, stale claims and destroyed yards;
- releases only the exact expected pair, so stale cleanup cannot clear a newer
  occupant.

`ShipDockRegistry.TryClaim` adds first-writer-wins, idempotent live claiming;
legacy `SetDocked` remains for existing build/repair paths until integration.

`DockingSnapshotV1` is a settable-property, additive JSON DTO. It deliberately
contains no runtime entity ids. The owning `BuiltShipRecord` supplies stable ship
identity, while its existing exact shipyard-position link resolves fresh runtime
ids before `TryRestore` reacquires the pair. Captured/docked restore always
freezes motion. Unknown versions, invalid enums and non-finite fields fail closed.

No game-assembly adapter, 1114/1205 publication, `ShipFlightService` mutation,
collision bypass or persistence-schema mutation is included. That boundary is
intentional.

## Period 3 — review

The adversarial review covered:

- **double capture/concurrent claims:** `TryClaim` never evicts a live pair;
  retrying the same pair is idempotent;
- **stale links:** every step verifies both registry directions; stale lifecycle
  cleanup resets itself without touching a newer occupant;
- **authorization:** owner, server-confirmed crew or abandoned yard are the only
  policy inputs; foreign private yards fail;
- **disconnect:** peer/pilot identity is absent from lifecycle and snapshot, so a
  disconnect cannot grant, release or resurrect authority;
- **restart:** no ephemeral entity id is durable; restore requires adapter-resolved
  ids and atomically reacquires occupancy;
- **delete/destroy:** hull deletion and destroyed-yard frames release both maps
  idempotently;
- **departure:** sail and engine use one transition and do not free the yard until
  authoritative clearance;
- **collision ordering:** clearance is an explicit required input. It must come from
  Track 5 after contact resolution for the same fixed step, never from a client;
- **rollback:** no live path references the new lifecycle, so current radius docking
  is unchanged and removal is a source-only rollback.

Remaining integration hazards are explicit: existing `SetDocked` writers can still
overwrite reservations until migrated; yard-active/ownership mutation must be one
transaction with 1114/1205 updates and persistence; and collision response must not
run after a captured velocity freeze and re-inject energy.

## Period 4 — testing

Native policy coverage includes five approach headings, owner/crew/abandoned
permission, active propulsion, fast approach, range and collision refusal,
capture freeze, shortest-yaw interpolation, deterministic 20 ms replay, occupied
yards, two concurrent claimants, idempotent claims, exact-pair stale cleanup,
JSON capture/restore, unsupported/non-finite/conflicting snapshots, sail/engine/
mixed departure, destroyed yard, hull deletion, stale claim and disconnect-neutral
continuation.

Live two-client interpolation and collision-clearance acceptance are deliberately
blocked on integration. They are not claimed by the native suite.

## Exact integration dependencies

1. **Track 2 (`53d3b56`)**: add nullable `DockingSnapshotV1` to the owning
   `BuiltShipRecord`; capture it in the same atomic replacement as flight state;
   resolve the existing shipyard-position link before `TryRestore`. Rotate pilot
   authority as Track 2 already does.
2. **Track 5 collision branch**: provide same-step `collisionClear` during approach
   and `outsideReleaseEnvelope` after resolved contacts. Capture/freeze must precede
   force integration; collision must validate the swept path to the target rather
   than authorize a teleport.
3. **Track 3 (`5dcd7d2`)**: adapt `DockingPose` to vector/quaternion state and zero
   both linear and angular accumulators. Retain 1114's level-yaw presentation until
   full orientation behavior is accepted.
4. Migrate every live `SetDocked` writer (build, restore, recapture, salvage, pickup,
   recall) to transactional claim/release semantics before enabling the lifecycle.
5. Publish 1114/1205 and persistence only after the transition commits; on failure,
   roll back the exact claim and do not expose half-docked client truth.

Only after those dependencies, source review and two-client tests should an
opt-in live docking flag exist.
