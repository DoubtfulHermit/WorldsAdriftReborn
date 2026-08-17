# Wareborn roadmap

**Current as of 2026-08-14.** This is the planning board. Historical plans and
superseded assumptions live under [`archive/`](archive/README.md); focused
evidence remains under [`research/`](research/README.md).

## Running in production

- Native Linux x64 game and login servers, systemd-managed on the VPS.
- Public account signup, PostgreSQL-backed accounts and character rosters.
- N-player presence, movement, appearance and basic equipment replication.
- Stable island identity, preserved WAMap import, Haven and The Trades
  Challenge at evidenced release coordinates.
- Deterministic biome-specific island resources with authoritative harvesting.
- Per-player, island-aware resource interest with staged login, pacing,
  hysteresis, removal and re-checkout.
- Persistent deployables, crafting stations, built ships, mounted/loose parts
  and server-authoritative scalar ship flight.
- Ship building, placement, salvage and material refunds across the recovered
  craftable part catalogue.
- Public patch manifest and self-configuring WAPatch client installer.
- Authenticated admin console with bounded, allowlisted operator actions.

Exact deployed revisions and operational caveats belong in
[`HANDOVER.md`](HANDOVER.md), not duplicated here.

## Active architecture track

The long-term goal is one legacy-client gateway backed eventually by movable
island and whole-ship simulation domains. Multi-process meshing is deliberately
deferred until the boundaries work in one process.

1. **Stable region topology — implemented and consumed.** `RegionId`,
   `RegionDefinition` and configured registries feed the directory and interest.
2. **World directory — implemented and live.** Every registration is classified
   as region, whole ship or explicit global scope.
3. **Region-backed resource routing — implemented.** Existing resource semantics
   are preserved while candidate selection consumes canonical ownership.
4. **Local ownership domains — implemented.** Island and whole-ship membership is
   complete and unique; gameplay scheduling intentionally remains in the proven
   poll-loop order.
5. **Ship snapshot proof.** Capture, destroy, restore and resume one live ship.
6. **Authority generations and gateway seam.** Reject stale-authority writes
   before introducing a process boundary.
7. **Experimental remote worker.** Start with an empty island/test domain; the
   gateway permanently retains client sockets.

Detailed gates and invariants are in
[`architecture/elastic-runtime-phases.md`](architecture/elastic-runtime-phases.md).

## Near-term gameplay and operations

- Complete a clean Haven → Trades Challenge → Haven acceptance run covering
  resource removal, re-checkout, harvesting and scanning.
- Visually accept the deployed, one-island continuous per-peer terrain checkout: approach,
  destination-before-source handoff, resource drain, leave/re-entry, teleport,
  reconnect and two-client independence. It remains off by default; production
  has a runtime-only opt-in for the bounded Mental Facility acceptance run.
- Visually accept the opt-in tier-1 B3 terrain prefix one island at a time; see
  [`research/findings-first-region-terrain.md`](research/findings-first-region-terrain.md).
- Finish retail-faithful wind, Wind Wall and sail behavior; current sails use a
  documented scalar propulsion approximation.
- Add safe reconnect after a game-server restart without broadening authority.
- Continue persistence coverage for remaining player inventory/utility state.
- Exercise three simultaneous real clients and retain packet/performance traces.
- Move the production database secret fully into a root-only environment file
  and keep credentials out of logs and documentation.

## Hard frontiers

- Grapples, ship collisions and other cross-ship constraints require temporary
  domain affinity or domain merging before distributed execution is safe.
- Full rigid-body ship physics and weather workers are not reconstructed.
- NPC/ecology simulation and most combat authority remain future systems.
- A worker process must never own the legacy client's ENet socket.

## Release gates

No architecture phase is complete only because unit tests pass. Runtime-facing
changes require:

1. focused policy tests and the full Multiplayer suite;
2. Release server/client builds as applicable;
3. unchanged persistence restore counts;
4. a live transition or gameplay acceptance pass;
5. packet/frame-time comparison against the previous deployment;
6. updated `HANDOVER.md`, this roadmap and patch manifest when client files
   actually change.
