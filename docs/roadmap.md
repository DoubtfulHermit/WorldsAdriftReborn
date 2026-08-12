# Roadmap

State as of 2026-08-08. Done means verified in a live two-client session, unless
the entry says otherwise.

Seven research reports landed on 2026-08-08 and corrected several entries below;
they are indexed in `docs/research/README.md`. All of it is static analysis —
nothing in those reports was executed — so where an entry cites them it says so.

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
- [x] Real appearance relayed (`AppearanceStore` records each owner's published
      1088 and the serializer seeds mirrors from it, falling back to the hardcoded
      look only for owners who never published)
- [x] Remote walk/idle animation (synced via 1073)
- [x] Remote play over the internet — the server runs on the VPS, see `docs/hosting.md`
- [x] **Character roster persistence** (2026-08-08). Real GUID uids, the save
      endpoint returns the full roster, and the roster lives in
      `<data>/characters/roster.json` (`WAREBORN_DATA_DIR`). Rules the client
      imposes are in the pure `RosterPolicy` with 19 tests. Verified by curl
      against a running server, not in a two-client session. **Only the roster.**
      Inventory, wearables, schematics and position are all still unpersisted —
      see below.

      Note for whoever builds on it: **durable player identity needs no new wire
      field.** The mod already publishes the selected character's JSON (uid +
      name) inside the 1088 update and `PlayerPropertiesState_Handler` records
      every key, so `Appearances.Get(entityId)["bossaNetCharacterData"]` carries
      the uid today. (1086 `PlayerName` also has a `characterUid` field, but the
      server fabricates that value.) The connect-metadata route is a dead end —
      `Connection.cpp:3-23` never reads `parameters->Metadata`. That the 1088
      identity lands at runtime is line-by-line verified but **not observed**;
      prove it log-only before building on it. And one roster serves the whole
      deployment, because the client hardcodes `/steam/1234` and Steam auth is
      stubbed — two clients can pick the same character.
      (`findings-persistence.md`, `findings-robustness.md` Q4.)

## Remote player fidelity (phased plan, 2026-08-07)

User-observed gaps after animation shipped, grouped by mechanism. Each phase uses
the proven loop: research subagent -> grant authority + seed component (relay is
automatic) -> pre-deploy review -> two-client test.

- [x] **Phase 1a - movement smoothing** DONE. Adopted PlayerVisualizer's
      interpolator for remote rigs (global branch only, reflected interpolators);
      RemoteRigMover reduced to a kinematic-enforcer that yields to it. Confirmed
      smooth + no fall.
- [ ] **Phase 1b - action delay** DEFERRED (optional). Move the high-rate 190602/1073
      relay off the RELIABLE ordered channel to unreliable to cut head-of-line
      latency; touches the C++ ENet_Send flag forwarding (unverified). Do only if
      the residual ~100ms interpolation delay bothers.
- [x] **Phase 2 - equipped clothes/gear** DONE. Worn gear renders from 1081
      InventoryState slotType via the already-seeded CharacterCustomisationVisualizer;
      the equip handler now fans the worn 1081 out to other peers. Server-only, no
      new seed. (Late-joiner store-and-seed still a follow-up.)
- [x] **Phase 3 - glider** DONE. Grant + seed 6910 UtilitySlotActivatedState +
      serializer default branch; wings open/close relay to remotes. 1109 PilotState
      deliberately NOT seeded (steals PilotVisualizer singleton, pokes LocalPlayer).
- [ ] **Phase 4 - grapple line & action VFX** (HARD FRONTIER, researching mechanism).
      Breaks the seed recipe: the Default rig has NO rope visualizer, so 1098 has no
      consumer and a reader can't be injected at runtime; boost puff is local-input-
      only and needs a relayed trigger carrier + mod emitter. Mechanism research in
      progress (phase4b-findings).

## Next (ranked)

- [ ] **Inventory persistence.** Blocked, and not on storage.
      `InventoryModificationState_Handler` only **logs** the 1082 events; it acts on
      exactly one (`equipWearable`) and even that mutates a copy that is never
      written back. 1082 is a pure event bus with no data fields — there is
      currently no inventory state to persist. The handler has to mutate the
      server-owned 1081 first. Then: persist `InventoryStateData` with all 14
      per-item fields (hotbar, stash, worn slot, colours and item health are all
      inside 1081), plus 1280 `WearableUtilsState` for worn-utility durability.
      1081 is full-state and rebuilds the whole UI on every update, so a push at
      any time restores an inventory with no ordering constraints.
      `slotType` must be the exact enum name or the client throws.
      (`findings-persistence.md`, static analysis.)
- [ ] **Reconnect after a server restart.** ~40 lines of C++ in our shim; the game
      already ships the whole RETRY/QUIT reconnect UX and our layer prevents it
      firing. No server change, no new wire message. Full mechanism, the ordered
      plan and the two traps are in `docs/hosting.md` and
      `findings-robustness.md`. Ship this before entity removal — it does not
      depend on it.
- [ ] **Parse asset-load acks.** The two-phase mirror flushes on a peer's next ack
      without matching the asset; a joining client's own spawn acks can race it. Parse
      the ack payload (add to the C++ layer if absent) and flush only on the plain
      Traveller ack.
- [ ] **Despawn on disconnect.** No wire message exists: no ENet channel, no proto, and
      `RegisterRemoveEntityCallback` is an unimplemented TODO in `Exports.cpp:51-54`.
      Needs: new proto + channel (C++), send path (`SendOPHelper`), and client
      dispatcher wiring. Until then departed players leave a frozen avatar.
      Smaller than it looks: **the client-side removal path already exists and
      works end to end** (`DispatchEventHandler.RemoveEntity` → `DestroyEntity` →
      `DelayDespawnCoroutine`), the managed SDK registers the native callback
      unconditionally in its constructor, and `RemoteRigMover.OnLeave` already
      emits `MirrorOp.RemoveEntity` — no policy change needed. `RemoveEntityOp`
      already exists native and managed and is blittable. Exactly one link is
      broken: our DLL discards the callback. Estimate ~1 day. Use
      `repeated int64 EntityId` (one op per frame otherwise caps removals at
      one), and note that the **real** risk is not the dispatcher wiring but the
      channel-count cap of **5**, hardcoded in three places, with no version
      check anywhere and `ENet_Send` ignoring `enet_peer_send`'s return — a
      channel-5 packet to an old client is silently dropped. Bump to 16, and log
      on a negative send. (`findings-entity-removal.md`, static analysis.)
      Removal is **not** a prerequisite for resource nodes: depletion there is
      state-based (`findings-resources.md`).
- [ ] **Weather `WeatherCellCoordsC` error spam (upstream #34).** Corrected
      2026-08-08 — the previous description of this entry was wrong in every
      particular and is kept here only so nobody re-derives it. It is **not** an
      NRE, it does **not** unwind the ECS tree, and it is **not** the biggest perf
      win available.

      What it actually is: a plain `WALogger.Error` line from
      `AddToIdComponentToEntityMapS.cs:63`, the one branch of four that logs a
      duplicate id and then **forgets to mark the entity**, so the filter matches
      it again next tick, forever. The ids collide because our own server
      fabricates `WeatherCellState` for any entity that asks for 1139 and seeds
      every entity at the same default transform, so several land in weather cell
      (0,0) whose Cantor pair id is 0 (see rule 15 in `docs/multiplayer.md`).
      Nothing is aborted, proven two ways: every composite child is individually
      `TryExecute`-guarded and the bound handler swallows rather than rethrows
      (its `"Dear QA"` banner appears **0 times** in both logs, so no ECS system
      has ever been silently force-disabled), and the weather system is the
      **last** entry in the FixedUpdate config anyway. Working two-client movement
      is itself proof — the transform publisher runs after the weather systems.
      The 16,012 NREs in the logs are a **separate, unrelated bug** that was
      conflated with this one because both print as `[Error : Unity Log]`:
      `ChararacterDrunk.SetDrunkLevel` (11,318), `ChararacterDrunk.Update` (1,381),
      `PlayerExternalDataVisualizer` (2,630), `PlayerMove` (681). Zero of them
      contain a `BossaECS` or `Weather` frame.

      Real cost, measured: one error per colliding entity per FixedUpdate tick,
      each paying an unconditional 17-frame stack capture plus synchronous log
      I/O — 68% of a single-client log and 93% of a two-client one (333 MB). The
      tick rate stays at ~99% of nominal, which argues against a large frame-time
      win. **Sell it as a diagnosability fix with a modest perf bonus**: it makes
      `grep NullReferenceException` readable, which is how the real
      `ChararacterDrunk` bug gets found. Recommended fix is a Harmony prefix on
      `AddWeatherCellCoordsS.Execute` returning `false` (public, non-generic,
      matches the repo's attribute convention; drives the count to zero by
      construction, and the only behavioural change is that a discontinuity at
      the origin disappears). Marking the entity in `AddToIdComponentToEntityMapS`
      is the more correct fix but the type is internal **and generic**, and
      whether Harmony can patch a closed generic over value types on this Mono
      runtime is **untested** — test that before committing to it. Verify with
      `grep -c 'Attempting to add existing id'`: baseline 10,280 / 212,214,
      target 0, and `grep -c 'Dear QA'` must stay 0.
      (`findings-weather.md`.)
- [ ] **The 16,012 NREs.** Split out of the weather entry above. `ChararacterDrunk.SetDrunkLevel`
      alone throws 11,318 times — also once per tick, paying the same exception +
      stack-trace cost. Unexplained, and at least as likely as weather to be the
      real per-frame cost. Probably the same family as the
      `PlayerExternalDataVisualizer` NREs the server already works around by
      injecting 1109.
- [ ] **Third-player test.** Everything is written N-way (registry, mirror, seed) but
      only ever tested with two clients. A third local client (another reflink copy +
      cloned prefix) exercises the N-way paths.
- [ ] **Upstream the crash fix.** The `ComponentUpdateManager` one-liner fixes a
      server-killing crash for everyone. PR only on explicit request (user's rule),
      ideally after a Windows sanity check.

## Researched 2026-08-08, not started

Each of these has a findings report in `docs/research/` with an exact plan. All
static analysis; none of it has been run.

- [ ] **More islands / world layout.** No world layout ships with the game — all
      255 island bundles hold a single prefab at `localPos=(0,0,0)`, and there is
      no JSON/CSV/XML anywhere. But the authoring format survives in the
      decompile (`MapFile { WorldInfo, Haven, Islands, Biomes, Walls }`, 12 km
      square, ±600 m altitude band), so we hand-author `islands.json` in Bossa's
      own shape. **Parenting is not the blocker** — the blocker is that
      `ComponentsSerializer.InitAndSerialize` switches on component id alone, so
      every island would be seeded at the same point, stacked. Bundle loading is
      synchronous and unthrottled (2.04 GiB across 255 bundles), so a
      three-island slice, not all of them; the existing ack-gated SyncStep chain
      already staggers loads. Do **not** introduce Parent in that slice.
      Worth one search of community archives for a preserved real `islands.json`
      before hand-authoring. (`findings-world.md`.)
- [ ] **Ships.** Viable on this client: ship motion is gated by **authority, not
      worker type**, so the pilot can hold 1130 + 190602 and publish while
      everyone else dead-reckons — the receive half already works in the stock
      client. Cost: three Harmony patches, one authority rule, and scalar
      (non-physics) state synthesis server-side. Stage A (a static ship you can
      see and stand on) needs **zero** client patches and nothing from
      persistence, resources or removal. Check the `fsimIdHash` echo-suppression
      risk **first**: receivers drop control points stamped with their own
      `WorkerId.GetHashCode()`, so if our clients share a `WorkerId` every client
      silently drops every ship update. Also: players ride ships via 1073
      `ClientAuthoritativePlayerState`, **not** `TransformState.Parent`.
      (`findings-ships.md`.)
- [ ] **Resources / harvesting.** Nodes are real SpatialOS entities, not baked
      decoration — islands carry 465,571 baked props but **zero** harvestables.
      The first blocker is not spawning, it is that the multitool beam cannot
      fire at all: 2105, 2106, 2002 and 1231 all `[Require]` **writers** that are
      neither seeded nor in `authoritativeComponents` (rule 14). The tree MVP is
      **retracted** — `TreeFsimVisualizer` is FSim-only, so a client-only server
      cannot harvest a tree without reimplementing the cutting sim; use a generic
      salvageable material node (1099 + 1016 + 190602) instead. Positions come
      from the **client**: the server fires a `SpawnResources` count on 1010 and
      the client raycasts its own island meshes and replies on 1011 — grant
      exactly one client that authority or every client duplicates the world.
      The damage→yield formula is unrecoverable (the GSim was Scala) and must be
      invented. (`findings-resources.md`.)
- [ ] **Interaction relay beyond movement** (the 3-handler gap: only inventory,
      crafting, reference-data have server handlers). Note 1010/1011 have **no**
      serializer handler at all and are silently dropped, so there is no resource
      respawning today (`findings-world.md`).

## Later / ideas

- [ ] Schematics: `defaultSchematics ∪ learnedSchematics` are both seeded empty and
      crafting has no server implementation. A static non-empty `defaultSchematics`
      makes crafting usable with zero persistence (`findings-persistence.md`).
- [ ] Position persistence — deliberately deferred. 190602 is client-authoritative
      and published immediately while identity arrives late, so re-seeding it is
      exactly the transform-reseed bug that `PlayerVisualizer_Patch` exists to
      suppress.
