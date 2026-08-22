# Track 9: performance, multiplayer hardening and worker foundations

Status: prerequisite-only implementation complete on an isolated branch,
2026-08-22. No remote worker, transport, migration or production change exists.

This track was deliberately executed against `origin/main` without merging the
other flight tracks. Their discovery reports were inspected read-only. Their
shadow evaluators add predictable per-ship work and state, but they do not alter
the conclusion below: actual worker wiring remains downstream of fixed-clock,
snapshot, rigidbody, lift, collision, docking, fuel and wall integration.

## Period 1: discovery

### Current cost and ownership topology

- `LocalDomainHost` already has an entity-to-domain index, so `OwnerOf` is O(1).
  `Synchronize` and `RemoveDomain`, however, found a domain's old members by
  scanning the complete entity index. That made one changed ship O(world
  entities). The new inverse index makes those paths O(changed-domain members).
- The flight heartbeat copies `_activeHullIds` and advances each active ship at
  the 240 ms publication cadence. On a wake it also builds and publishes all
  mounted-part wakes. This is O(active ships + relevant peers times awake
  members); future 50 Hz physics must be budgeted separately from the 4.17 Hz
  client publication path.
- `ShipPublisher.BroadcastDomainMotion` walks every player for every emitting
  hull, then every awake member for each relevant player. Domain frames have a
  generation and sequence, but no transport envelope, retry identity, commit or
  remote authority owner.
- `RemotePlayerMirror` mirrors joins, component updates and leaves to every other
  player. High-rate avatar relay therefore has an O(players squared) worst case.
  It is not spatially indexed yet.
- Resource interest is bounded to 512 queued actions per peer and one send each
  120 ms, but its 500 ms reconcile builds retained sets, queries candidates and
  scans every island envelope per peer. `TryCenterFor(peerId)` also scans all peer
  states. Terrain, fauna and ship interest each maintain separate peer ledgers.
- Ship interest reconciles every live ship for every peer every 500 ms. Its
  `MayServe` member classification can walk every ship, and stats compute
  subscriber counts by walking all players for every ship.
- The three-second inspector snapshot materializes players, ships, domains,
  per-peer interest and a full ownership audit. `DomainHost.Inspect` is correctly
  an audit rather than a tick operation, but it still scales with the entire
  expected world and must remain off the physics path.
- `ShipDomainSnapshot` is a useful process-independent logical snapshot, but no
  committed byte format, digest, size cap, storage transaction or recovery
  coordinator exists. Aboard peer ids and a live pilot binding are session state;
  they cannot simply become valid authority on another worker after restart.
- Cross-domain interactions currently include aboard/player containment,
  pilot/control, spatial ship/island affinity and player/island interest in the
  observer model. Runtime gameplay also crosses domain seams through mounting,
  detach/remount, ship checkout, terrain/resource ordering, docking/yard links,
  collisions, storms/walls, operator recall/stop, persistence and client relay.
  None is routed through a universal gateway yet.

### Deterministic scale baseline (operation counts, not latency claims)

Assumptions: 50 physics steps/s, 32 members/ship, two players/ship, future
spatial relay capped to 32 neighbours and a provisional 4 KiB logical snapshot
per ship. The model is executable in `RuntimeScaleBaseline`.

| Active ships | Physics steps/s | Membership refresh before/after inverse index | Full relay directed pairs | 32-neighbour cap | Provisional snapshot bytes |
|---:|---:|---:|---:|---:|---:|
| 5 | 250 | 160 / 32 | 90 | 90 | 20,480 |
| 20 | 1,000 | 640 / 32 | 1,560 | 1,280 | 81,920 |
| 50 | 2,500 | 1,600 / 32 | 9,900 | 3,200 | 204,800 |
| 100 | 5,000 | 3,200 / 32 | 39,800 | 6,400 | 409,600 |

These numbers expose complexity only. A later controlled harness must record
p50/p95/p99 step, relay, reconcile, snapshot serialization and GC time on the
actual VPS before selecting budgets.

### Threat model

- **Stale generation:** a delayed or healed old worker writes after takeover.
- **Dual authority/split brain:** source and destination both believe they are
  active during a partition.
- **Retries and replay:** a caller repeats a successful command after losing its
  response, or replays an old valid command id with a changed payload.
- **Ordering:** command N+1 arrives before N, or one replication sequence is
  committed with two different states.
- **Snapshot tampering/truncation:** bytes change in storage/transit, an attacker
  supplies an oversized snapshot, or session pilot/peer handles are restored as
  if still live.
- **Epoch exhaustion/rollover:** generation increment reaches `long.MaxValue`.
- **Overload isolation:** one huge ship, malicious part count, replay flood or
  catch-up storm consumes the single poll loop or worker memory.
- **Worker/coordinator loss:** worker loss is modelled here; coordinator loss and
  quorum are not. A single in-process coordinator cannot resolve a real network
  partition safely without an external lease/consensus authority.
- **Malicious clients:** client commands must terminate at the authoritative
  game gateway; clients must never select worker, generation or replication
  sequence.

## Period 2: coding

Safe prerequisites only:

1. `LocalDomainHost` now maintains `_membersByDomain` alongside
   `_ownerByEntity`. Register, assign, move, unassign, synchronize and remove
   update both indexes. The completeness audit cross-checks live domain state,
   the forward index and reverse index bidirectionally.
2. `DomainWorkerProtocol` defines transport-neutral worker ids, authority stamps,
   capped immutable committed snapshots, ordered/idempotent command admission and
   a deterministic revoke/restore/ready/promote recovery model.
3. Snapshot payloads are capped at 8 MiB, copied at ingress and egress and
   SHA-256 verified. One generation/sequence cannot commit two different states.
4. Command ids and replay memory are bounded. The gate rejects stale generation,
   wrong worker, gaps/reordering and same-id/different-payload conflicts. Transfer
   advances exactly one generation and clears the old replay window.
5. `BoundedRuntimeTelemetry` provides a fixed-memory ring for later live timings,
   budgets and replay/catch-up markers. It does not read a clock or alter runtime.
6. `RuntimeScaleBaseline` encodes the 5/20/50/100 operation-count tiers above.

There is intentionally no socket, serializer, worker executable, heartbeat,
lease, production flag, live gateway integration or claim that migration works.

## Period 3: review

Review findings and dispositions:

- **Dual authority:** old stamps are rejected after promotion. The model depends
  on one authoritative coordinator; real deployment is blocked on a lease or
  consensus decision. Open, deployment-blocking.
- **Idempotency:** exact command-id, sequence and payload-digest retries within
  the bounded window are duplicates; changing either sequence or payload under
  the same command id conflicts. Retries older than the window are rejected as
  out of order. A production gateway must durably cache outcomes or declare its
  retry horizon. Open design choice.
- **Ordering:** only the next sequence is admitted; a transfer must restore a
  declared sequence. There is no gap buffer, avoiding attacker-controlled memory.
- **Epoch rollover:** `AuthorityGeneration.Next()` fails closed at max value.
- **Snapshot tampering:** digest, defensive copies, size cap and same-sequence
  conflict checks are present. Authentication/signatures and canonical wire
  serialization do not exist. Open before remote transport.
- **Repeated worker loss:** a second recovery cannot use a snapshot from the
  previous generation. The newly promoted worker must commit before it is
  recoverable. Safe but availability-limiting by design.
- **Overload isolation:** new replay, telemetry and snapshot structures are
  capped. Live ship members, active ships, per-peer ship queues and publication
  work still need caps/budgets after the physics tracks merge.
- **Local fallback:** no automatic fallback was implemented. A future fallback is
  another authority transfer and must obey the same revoke/commit/generation
  gates; silently executing locally is forbidden.
- **Session state:** aboard peer ids and pilot authority must be revoked/rebound on
  restore, not trusted from snapshot. Existing `ShipDomainSnapshot` does not yet
  impose that policy. Open prerequisite for live migration.

## Period 4: testing

Automated coverage includes:

- inverse-index synchronize, move, unassign and domain removal;
- duplicate live-domain membership rejection before forward/reverse mutation;
- 5/20/50/100 deterministic load-tier estimates;
- bounded telemetry overwrite, summary, over-budget and hostile values;
- ordered admission, exact duplicate retry, changed-sequence/digest idempotency
  conflict, sequence gaps,
  bounded replay eviction, wrong worker and stale authority;
- snapshot defensive copy, digest, 8 MiB cap and conflicting same-sequence state;
- pure kill/partition, revoke, restore, candidate readiness, takeover, healed-old-
  worker rejection, repeated failure and generation advancement.

### Later real acceptance (not performed)

1. Merge Tracks 2-8 and route all domain writes through one in-process gateway.
2. Capture a repeatable 5/20/50/100-ship harness with players distributed across
   islands and ships. Record p50/p95/p99 physics, relay, interest, gateway,
   serialization, snapshot bytes, allocations, GC, bandwidth and disconnects.
3. Run the candidate worker on the same host first. Migrate an empty island,
   verify generations/commands/snapshots in the inspector, then return it local.
4. Move the empty island to a separate VPS behind authenticated transport and a
   real lease/consensus authority. Inject delay, duplication, reordering,
   corruption and partitions.
5. `kill -9 worker-b` only after the above. Expected sequence: active authority
   revoked; commands fail closed; committed snapshot restored; candidate digest
   agrees; generation advances exactly once; old worker heal remains rejected;
   no duplicate entity/authority; clients remain connected; discontinuity stays
   within the declared snapshot age plus detection/recovery bound.
6. Repeat with a non-crewed disposable ship. A crewed production ship is the last
   acceptance tier, never the first.

## Gate before a real worker

- Tracks 2-8 integrated and green, including durable moving-flight snapshots.
- Universal in-process domain command gateway; no direct mutating bypasses.
- Canonical authenticated worker protocol and durable command-result horizon.
- Coordinator/lease or consensus design that resolves partitions without dual
  authority; coordinator high availability and clock assumptions documented.
- Snapshot schema compatibility, pilot/peer revocation, ownership transaction and
  rollback to local host proven.
- Spatial relay and interest indexes plus explicit per-domain physics/part/queue/
  bandwidth/catch-up caps.
- Inspector exposes worker heartbeat, authority, generation, snapshot age,
  recovery phase, rejected stale writes and overload state truthfully.

Until every gate is met, `local:primary` remains the only authority host.
