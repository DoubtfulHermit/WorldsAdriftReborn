# Roadmap

State as of 2026-08-07. Done means verified in a live two-client session.

## Done

- [x] Single-player restored on Linux (Proton client, Wine servers, mingw CoreSdkDll)
- [x] Fatal CLR crash on first component update fixed (`ComponentUpdateManager` assembly scan)
- [x] Sender identity on packets (C++ wrapper + explicit C# layout) and peer cap 1 → 8
- [x] Per-peer mirror with pure, unit-tested policy (`PlayerRegistry`, `RemotePlayerMirror`, 26 tests)
- [x] Position publishing (TransformState authority grant) + verbatim relay
- [x] Remote players spawn as the game's own remote rig (prefab context "Default")
- [x] Two-phase mirror (asset preload → parked ops → flush on ack)
- [x] Shared island entity id (cross-client parent references resolve)
- [x] `RemoteRigMover`: remote avatars placed and tracked from the relayed stream
- [x] Camera/identity keep-first guards; local-rig anchor via camera claim
- [x] Observability: rig inventories, remote-rig diagnostics, transform sample decoding, component id map (443 ids)

## Next (ranked)

- [ ] **Relay real appearance data.** Both avatars render the local fallback look.
      Serve/relay the source player's actual `PlayerPropertiesState` (1088) — and its
      updates — instead of default-initialized data, so each avatar shows its owner's
      character. (Server-side; the client visualizer path already consumes updates.)
- [ ] **Walk animation.** Remote avatars are T-posed statues. The plain rig has
      `BoneAnimationReader`; find its component id in `docs/component-ids.md`, check
      what the sender publishes for it (extend `TransformSampleLogger`), seed + relay.
- [ ] **Movement smoothing.** Relay uses the RELIABLE flag on the ordered update
      channel → head-of-line blocking and visible snapping. Switch transform relays to
      unreliable delivery and add client-side interpolation in `RemoteRigMover`
      (lerp toward target instead of teleporting each frame).
- [ ] **Parse asset-load acks.** The two-phase mirror flushes on a peer's next ack
      without matching the asset; a joining client's own spawn acks can race it. Parse
      the ack payload (add to the C++ layer if absent) and flush only on the plain
      Traveller ack.
- [ ] **Despawn on disconnect.** No wire message exists: no ENet channel, no proto, and
      `RegisterRemoveEntityCallback` is an unimplemented TODO in `Exports.cpp`. Needs:
      new proto + channel (C++), send path (`SendOPHelper`), and client dispatcher
      wiring. Until then departed players leave a frozen avatar.
- [ ] **Weather ECS NRE storm (upstream #34).** `WeatherCellCoordsC` duplicate-id NRE
      unwinds the whole BossaECS tree every FixedUpdate (thousands/session, both
      clients, also single-player). Fix candidate: Harmony patch
      `AddToIdComponentToEntityMapS` to mark the entity on the duplicate branch so it
      stops re-matching. Biggest single perf win available.
- [ ] **Third-player test.** Everything is written N-way (registry, mirror, seed) but
      only ever tested with two clients. A third local client (another reflink copy +
      cloned prefix) exercises the N-way paths.
- [ ] **Upstream the crash fix.** The `ComponentUpdateManager` one-liner fixes a
      server-killing crash for everyone. PR only on explicit request (user's rule),
      ideally after a Windows sanity check.

## Later / ideas

- [ ] Remote play over the internet (frp tunnel exists for other projects; needs the
      REST url + gameserver host made configurable per client — mod cfg already has
      `GameServer_Host` and `REST_ServerUrl`)
- [ ] Persistence (characters, inventory — `CharacterSaveHandler` still discards saves)
- [ ] More islands / world layout (server hardcodes island `949069116` at one position)
- [ ] Interaction relay beyond movement (the 3-handler gap: only inventory, crafting,
      reference-data have server handlers)
