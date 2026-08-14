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

## Phase 1 — stable region topology

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

## Phase 2 — read-only world directory

Add a single-process directory that classifies registered world entities by
island, region, ship or global scope. Existing services may query it for
diagnostics, but their current behavior remains authoritative.

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

## Phase 4 — local simulation domains

Introduce `SimulationDomainId`, a domain registry and a local domain host. All
domains still tick sequentially in the existing poll loop. Begin with island
domains; add whole-ship domains without moving game logic between processes.

Acceptance: domain ownership is complete and unique, global services are
explicit, and disabling the abstraction produces no gameplay difference.

## Phase 5 — snapshot proof with one ship

Define a versioned ship-domain snapshot and prove capture, destroy, restore and
resume flight in the same process. Include hull pose, control state, mounted and
loose parts, sails, dock state, aboard relationships and persistence identity.

Acceptance: a test ship resumes without entity duplication, lost attachments,
control reset or client-visible teleport beyond the normal update tolerance.

## Phase 6 — authority generations and in-process gateway seam

Attach monotonically increasing authority generations to internal commands and
state writes. Route client input through an in-process gateway interface and
reject stale-generation work. The real ENet socket remains where it is.

Acceptance: forced handoff tests prove old-authority writes cannot overwrite the
new owner, while the client remains connected to the same endpoint.

## Phase 7 — experimental second worker process

Replace one local domain host with a remote host behind the same interface.
Start with an empty/test island, then a non-crewed ship. Do not migrate live
player sockets; the gateway owns them permanently.

Acceptance: worker loss is observable and recoverable from a committed snapshot,
and migration does not disconnect the client or create dual authority.
