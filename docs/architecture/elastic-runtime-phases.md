# Wareborn Elastic Runtime — phased delivery plan

This plan evolves the existing native Linux game server without changing the
legacy client's one-server view. Every phase must work in the current process
and poll loop before a process boundary is introduced.

## Invariants

- The client connects to one endpoint and speaks the existing protocol.
- An island or whole ship is the smallest future authority unit. A ship's hull,
  parts, cargo and aboard players are never split across authorities.
- Existing Haven and Trades Challenge coordinates, persistence keys and wire
  entity ids remain unchanged unless a separately reviewed migration says so.
- No phase is complete from unit tests alone: runtime-facing phases require a
  live acceptance pass and useful diagnostics.

## Phase 1 — stable region topology (implemented)

Introduce dependency-free `RegionId`, `RegionDefinition` and `RegionRegistry`.
Map every evidenced island to exactly one stable region. Do not change spawning,
interest, persistence or networking yet.

Acceptance:

- deterministic region enumeration;
- unknown islands, duplicate region ids and duplicate island ownership fail at
  startup;
- Haven and Trades Challenge retain their exact current island definitions and
  global origins;
- the full Multiplayer test suite and server build pass.

## Phase 2 — read-only world directory (implemented and live-verified)

Add a single-process directory that classifies registered world entities by
island, region, ship or global scope. Existing services may query it for
diagnostics, but their current behavior remains authoritative.

Implementation: `WorldDirectory` classifies explicit global data, region-owned
terrain/resources/structures and static or built whole-ship membership. The
server supplies mounted loose-part-to-hull overrides from its existing ledger
after restore and spawn-plan binding, then prints one `[world-directory]
summary. At Phase 2 completion no gameplay path consumed the result; Phase 3 now
uses its region ownership for resource candidate selection only.

Acceptance: every boot entity is classified once, classifications are stable
across registration order, and production logs show zero unclassified entities
outside an explicit global allowlist.

## Phase 3 — region-backed interest routing

Move candidate selection behind a general interest query while preserving the
current resource checkout policy byte-for-byte. Extend it to terrain, ships,
structures and loose parts only through separate, measured switches.

Acceptance: Haven/Trades transitions add and remove the same resources as the
current implementation; distant terrain/entity visibility is explicitly
defined; login packet and frame-time budgets do not regress.

Current status: resource candidate selection now passes through a pure,
directory-backed region query. The existing island-frame selection, spatial
radii, hysteresis, queue ordering, pacing and unload compatibility behavior are
unchanged. Previously loaded resources from another region remain candidates
for one final reconciliation so they can be removed. Runtime resource
registrations extend the query explicitly. Terrain, structures, loose parts and
ships have not been moved onto this route.

## Phase 4 — local simulation domains (whole-ship slice deployed; acceptance failed)

Introduce `SimulationDomainId`, a domain registry and a local domain host. All
domains still tick sequentially in the existing poll loop. Begin with island
domains; add whole-ship domains without moving game logic between processes.

Acceptance: domain ownership is complete and unique, global services are
explicit, and disabling the abstraction produces no gameplay difference.

Current status: an ownership-only `LocalDomainHost` now contains one domain per
known island and every whole-ship domain. Region-owned static entities carry a
stable island affinity resolved from explicit terrain identity or nearest known
island origin. The host assigns each bound boot entity to
exactly one domain or to the explicit global set, fails startup on incomplete or
duplicate ownership, and logs a truthful `local-single-process` summary. The
host intentionally has no `Tick`: gameplay services retain their proven poll-loop
order. Runtime resource/deployable/loose-part creation, ship creation/retirement,
and part mount/detach update the same ownership index transactionally. A ship
domain owns flight/control state, pilot authority, structural
membership and aboard affinity. Runtime and restored built hulls register into
it, while ferry/nudge/static probes use the same replication-generation seam.
Unmanned/uncrewed domains now have paced, per-viewer whole-domain checkout
hysteresis: members unload before the root and reload after it, with
late-interest guards. Checkout controls only what one peer sees. It must not
park the simulation, affect another nearby peer, migrate authority or delete
the domain: the local `ShipFlightService` ticks its active-domain set independently
of every peer's checkout ledger, and replication evaluates each recipient after
the authoritative step. Ship visibility uses separate island-scale radii rather
than the resource radii.
Optional island visibility now also has two deliberately separate lifecycles.
After the normal connect plan completes, the server can prefetch each managed
island bundle and the patched client constructs a non-colliding shell from its
last retail terrain LOD. The shell remains hidden until
`GenerateDynamicMaterial` finishes, so an uninitialised magenta material is
never presented. It hides while the authoritative terrain entity is checked
out and returns after that entity is removed. Collision, resources, databanks,
static prefabs and island authority remain exclusively on the existing
1200/1600 m physical checkout lifecycle; a shell is visual evidence only.
Crewed/piloted ships remain globally checked out as a compatibility bridge while
remote player entities are still globally relayed outside domain lifecycle.
The local runtime now provides one bounded coherence bridge for that legacy
split: avatars retain their independent 20 Hz stream, while a successfully
delivered authoritative ship frame forces the latest aboard avatar sample to
follow its hull on the same poll-loop turn for that recipient. This is ordered
same-frame delivery, not cross-entity packet atomicity and not multi-host
handoff. Schema-v3 runtime telemetry exposes the complete hosted domain inventory,
host identity, ownership totals, island anchors and ship-to-island spatial
affinity, while retaining ship generation, replication sequence/frame age,
authoritative pose, crew, structural membership and per-peer checkout count.
The admin Simulation Fabric renders this as host clusters, island topology,
capped ship nodes, a searchable/filterable inventory and one-domain drill-down;
the shape scales to later hosts without claiming workers or migrations that do
not exist. It still labels the current host `local-single-process` and does not
invent compute scores or client-rendered offsets the runtime cannot observe.
Moving actual simulation services behind the host and a live acceptance pass
remain outstanding. The first live two-player pass also exposed remote-avatar/ship coordinate-frame
divergence, a five-second client spline wake after manned-idle stream starvation,
and a failed return checkout. All three now have local fixes: the relay holds raw
Invalid/bias-zero collider gaps while canonical aboard state remains on the ship;
idle flight continues a legal root stream; and reconciliation preserves a valid
in-flight asset request while send-time guards suppress stale Add/Remove actions.
Successful removal also clears only that peer's AddEntity/component ledgers.
They still require a two-client visual acceptance pass. The phase as a whole is
not complete until that pass succeeds.

## Phase 5 — snapshot proof with one ship (pure proof implemented locally)

Define a versioned ship-domain snapshot and prove capture, destroy, restore and
resume flight in the same process. Include hull pose, control state, mounted and
loose parts, sails, dock state, aboard relationships and persistence identity.

Acceptance: a test ship resumes without entity duplication, lost attachments,
control reset or client-visible teleport beyond the normal update tolerance.

Current status: the versioned pure snapshot preserves pose, input, flight
timeline, pilot binding, authority generation, persistence identity, decks,
mounted members and aboard peers, and resumes the flight state machine in tests.
The destructive live capture/destroy/restore acceptance test, sails/dock/loose-
object completeness audit and visual no-teleport proof remain outstanding.

## Phase 6 — authority generations and in-process gateway seam (started)

Attach monotonically increasing authority generations to internal commands and
state writes. Route client input through an in-process gateway interface and
reject stale-generation work. The real ENet socket remains where it is.

Acceptance: forced handoff tests prove old-authority writes cannot overwrite the
new owner, while the client remains connected to the same endpoint.

Current status: helm acquire/release/disconnect increments a ship's generation;
input carries a generation-stamped token and stale pilot input is rejected. The
replication cursor also rejects stale-generation ship frames. Client commands do
not yet pass through a general in-process gateway interface, so Phase 6 is not
complete.

## Phase 7 — experimental second worker process

Replace one local domain host with a remote host behind the same interface.
Start with an empty/test island, then a non-crewed ship. Do not migrate live
player sockets; the gateway owns them permanently.

Acceptance: worker loss is observable and recoverable from a committed snapshot,
and migration does not disconnect the client or create dual authority.
