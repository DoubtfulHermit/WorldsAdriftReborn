# Wareborn Engineering Handover

**Canonical entry point for a new maintainer or coding agent**

**Snapshot:** 2026-08-15, Europe/Berlin

**Repository:** `DoubtfulHermit/WorldsAdriftReborn`

**Active integration worktree:** `/home/ttanurhan/Games/wareborn-loading`

**Active branch at this snapshot:** `feat/island-identity`

**Deployed code baseline at this snapshot:** `489517f` (`Fix ship steering,
passenger coherence, and re-entry`). Production and the checked-out gameplay
code match at this revision; later documentation-only commits do not imply a
different production binary.

This file is the current operational and architectural handover. Start here,
then follow the narrower documents it links. Do not treat old roadmap entries,
downloaded design briefs, branch names, or chat summaries as proof that code is
implemented.

## 1. First 15 minutes

1. Work in `/home/ttanurhan/Games/wareborn-loading` unless the user
   explicitly selects another worktree.
2. Run `git status --short`, `git branch --show-current`, and
   `git log -10 --oneline` before editing. There are many historical worktrees;
   changes in one do not appear in another.
3. Read this file, then [hosting.md](hosting.md), [testing.md](testing.md), and
   [the research index](research/README.md).
4. For client behavior, inspect the retail decompile at
   `/home/ttanurhan/Games/WAReborn-decompiled/acs` before inventing behavior.
5. Run the baseline gate:

   ```bash
   dotnet test WorldsAdriftRebornGameServer.Multiplayer.Tests -c Release
   dotnet build WorldsAdriftRebornGameServer -c Release
   dotnet build WorldsAdriftReborn -c Release
   ```

   At this snapshot the Multiplayer suite passes **2342/2342**, and both server
   and client builds succeed. Existing nullable/obsolete/net6-EOL warnings are
   known. Do not run the test and server builds concurrently: both write the
   Multiplayer output and can cause a harmless file-lock retry.
6. Check production read-only before any deployment:

   ```bash
   ssh root@62.171.161.19 'systemctl is-active wareborn-game wareborn-login'
   ssh root@62.171.161.19 \
     "journalctl -u wareborn-game -o cat --no-pager --since '10 min ago' | tail -100"
   curl -fsS https://wareborn.ratlabs.cc/patch/manifest.json | jq '{version,build}'
   ```

7. Never restart the game server while players are connected unless the user
   explicitly says they have disconnected. A restart is still session-ending.

## 2. Source-of-truth hierarchy

When sources disagree, use this order:

1. current checked-out code and tests;
2. a current live log or packet trace;
3. the shipped retail decompile and asset census;
4. this handover;
5. focused findings under `docs/research/`;
6. `README.md`, `docs/hosting.md`, and `docs/roadmap.md`;
7. downloaded handover/roadmap proposals and old chat summaries.

Superseded planning documents live under `docs/archive/`. They remain useful for
historical citations but must not override the current roadmap or handover.

## 3. Repository and runtime map

The worktree cleanup on 2026-08-14 removed 94 clean historical checkouts while
retaining every branch and commit. Thirteen worktrees remain: this integration
tree, `wareborn-main`, the original dirty checkout, and dirty research/diagnostic
trees. Do not remove those remaining trees until their uncommitted files are
reconciled explicitly.

| Area | Purpose | Primary entry points |
| --- | --- | --- |
| `WorldsAdriftReborn/` | BepInEx client mod, Harmony patches, client diagnostics | `Plugin.cs`, `Patching/` |
| `WorldsAdriftRebornCoreSdk/` | Native client/server protocol shim and ENet transport | `Connection.cpp`, `Dispatcher.cpp`, `enetLayer.cpp`, `OpList.h` |
| `WorldsAdriftRebornGameServer/` | Authoritative game server and main poll loop | `WorldsAdriftRebornGameServer.cs`, `Game/`, `Networking/` |
| `WorldsAdriftRebornGameServer.Multiplayer/` | Engine-free policies, ledgers, catalogues, geometry | resource, inventory, placement, ship and flight types |
| `WorldsAdriftRebornGameServer.Multiplayer.Tests/` | Fast native regression suite | 2295 tests at this snapshot |
| `WorldsAdriftServer/` | Login, accounts, roster and patch-file HTTP service | request handlers, storage integration |
| `WorldsAdriftReborn.Storage/` | PostgreSQL models/repositories/migrations | storage tests require `WAREBORN_DB` for integration cases |
| `tools/patcher/` | WAPatch and manifest release pipeline | `README.md`, `build-manifest.sh` |
| `tools/relaybot/` | Native shim builder, protocol/load diagnostics and isolated two-peer ship wire acceptance | `build-coresdk-native.sh`, `run-ship-acceptance.sh` |
| `docs/research/` | Evidence and protocol reconstruction | `README.md` index |

Important local external inputs:

- Retail decompile: `/home/ttanurhan/Games/WAReborn-decompiled`
- Local game/client: `/home/ttanurhan/Games/WorldsAdrift`
- Extracted Haven surface data:
  `docs/research/world-data/island-surfaces/1431299145.json` when present in the
  relevant research checkout, plus the generated `Resources/HavenSurface.cs`.

## 4. Production snapshot

### Services and endpoints

- VPS: `62.171.161.19`
- Game: native Linux x64, UDP `7779`, systemd unit `wareborn-game`
- Login/REST: native Linux x64, TCP `8085`, systemd unit `wareborn-login`
- Public signup/patch host: `https://wareborn.ratlabs.cc`
- PostgreSQL: loopback-only on VPS port `5434`
- Live native game directory:
  `/opt/wareborn/WorldsAdriftRebornGameServer-native`
- Live patch directory: `/opt/wareborn/patch`
- Old Wine game deployment remains rollback-only.

The native game unit is represented by
`deploy/wareborn-game-native.service`. It loads `libCoreSdkDll.so`; build that
shim with `tools/relaybot/build-coresdk-native.sh` whenever native protocol code
changes.

### Exact deployed revisions

- **Game server:** `1aa9fe4`, deployed and restarted at 2026-08-17 12:44 CEST;
  **login/admin server:** the `069a372` build deployed at 11:32 CEST. Stats schema
  5 reports terrain checkout and the admin
  console exposes its one-island acceptance run. Production remains bounded to
  `WAREBORN_FIRST_REGION_TERRAIN_COUNT=1` and reports 3 island domains, 5 ship
  domains, 255 owned entities, one explicit global, zero unowned entities and
  zero ownership inconsistencies. Terrain checkout is enabled with the existing
  120 m resource-interest prerequisite; its defaults are 1200 m load / 1600 m
  unload. Validation passed 2,556/2,556 Multiplayer tests and 181/181 admin/login
  tests; game, login and client Release builds had zero errors. The coordinated
  coordinated rollback copy is
  `/opt/wareborn/backups/pre-069a372-20260817T093253Z/{game,login,patch}`; the
  immediate pre-fix game rollback is
  `/opt/wareborn/backups/pre-b52f504-20260817T100136Z`; the immediate
  pre-`1aa9fe4` rollback is
  `/opt/wareborn/backups/pre-1aa9fe4-20260817T104346Z`.
- **Public client manifest:** `2026.08.17-1`, build label
  `terrain checkout and B3 visual acceptance (069a372)`. It ships the marked,
  correlated asset-loaded acknowledgement required for safe optional-terrain
  unload/re-entry. All 54 public payloads matched their published hashes.
- **Managed client DLL SHA-256:**
  `9759a005fb1efe2cab39bbeafc66be4da7a095fed3399b24390f75c819d7cf6b`.
- **Windows CoreSDK DLL SHA-256:**
  `26b5ce1568abec2ca06d488e3aadaaf725c92a89e1e2482571e27ad31986c354`.
- **Server state:** active on native Linux, UDP 7779. Boot restored 4/4 placed
  deployables, 5/7 ships (two tombstones), 16/16 mounted parts and 3/3 loose
  parts. Stats report schema 5, host mode `local-single-process`, and terrain mode
  `on`. Staged/live game, login and production-built Linux CoreSDK hashes match;
  the Linux shim is SHA-256
  `0121219a138a07f345103f83cc5647f993ecb0282a0172c7bf19a54b78a252f7`.

### Latest multiplayer incident

- **Server revision:** `ab9bc94`, building on the replication corrections in
  `489517f`. The 2026-08-14 two-player session proved three related failures:
  runtime-created yards/ships were absent from the boot-frozen plan on relog;
  distant ships and mounted parts broadcast motion globally; and absolute ship
  control points bypassed the unreliable-stream policy, building a reliable
  retransmit queue (observed peak 49 KB in flight and 6.8 s RTT).
- The revision adds paced runtime-entity catch-up, a per-peer AddEntity ledger,
  distance/checkout-gated ship motion with pilot/passenger overrides, current-pose
  registry relocation, superseding/unreliable 1130 delivery, idempotent duplicate
  helm Man events, serializer-buffer cleanup, and removal of per-update log spam.
- Validation: Multiplayer tests `2311/2311`; Release game-server build succeeded;
  deployed managed binary hashes matched the local publish exactly. A two-player
  relog/ship-flight/re-entry acceptance test remains required.
- Local follow-up `a5bed13` keeps the normal 20 Hz avatar relay but, after every
  accepted 240 ms ship-domain root frame, immediately relays the latest aboard
  avatar sample to each peer that actually received that root. This gives the
  legacy protocol hull-first ordering on the same server-loop turn without
  pretending cross-entity packets are atomic or reducing avatar movement to
  4.17 Hz. The admin page now reads schema-v2 runtime telemetry for real local
  ship domains: authority generation, replication sequence/frame age, pose,
  pilot/aboard membership, structural counts and checkout subscribers. It is
  explicitly labelled local single-process and exposes no fictional workers,
  migrations or authority controls. Validation: Multiplayer `2322/2322`,
  admin/login `155/155`, both Release builds zero errors.
- `18d89b3` expands `/admin` into World / Simulation / Operations / System.
  Existing player recovery tools remain, and three exact allowlisted world
  operations now complete on the game-server poll loop: reset all damaged
  trees/metal/fuel, recall an uncrewed hull beside a selected connected player,
  and permanently delete an uncrewed exact hull plus its persisted structure.
  Recall/delete reject a piloted or occupied hull. Delete durably tombstones the
  ship before runtime removal and requires typed `DELETE` plus browser
  confirmation. Session-derived CSRF now protects commands, logout and server
  naming. The UI separately renders login-server queue acceptance and the game
  server's atomic completion receipt; it never calls dispatch a successful
  gameplay action. Validation: Multiplayer `2336/2336`, admin/login `168/168`,
  both Release builds zero errors. This is server/admin-only, needs no patcher
  manifest change, and is deployed in `ab9bc94`.

Do not put database passwords, session tokens, account records, or private
connection strings in documentation, commits, commands whose output is pasted
into chat, or issue reports. In particular, avoid printing the full systemd
`Environment=` property. A production database credential currently exists in
a systemd drop-in; rotate it and move it to a root-only `EnvironmentFile` when
doing the next security/operations pass.

### Deployment discipline

`docs/hosting.md` now contains only the current native Linux deployment flow;
the mixed Wine/native instructions are preserved under `docs/archive/` for
rollback history. The safe native flow is:

1. build/test the selected commit;
2. publish the game server for `linux-x64` self-contained into a fresh staging
   directory;
3. build and include `libCoreSdkDll.so` if native shim sources changed;
4. preserve/backup the remote `data/` directory and current native deployment;
5. sync staged files without deleting persistent data;
6. restart only after the user confirms every player is disconnected;
7. inspect boot restore counts, resource-interest banner, hull metrics, and
   first connection logs.

The production VPS is Ubuntu 24.04 with protobuf 3.21 (`libprotobuf.so.32`).
Do not deploy a native shim built on the Arch/CachyOS development host: that
currently links protobuf 35 plus its newer Abseil libraries and will fail to
load on production. The proven PR4 rollout copied the shim sources to an
isolated VPS build directory and ran `tools/relaybot/build-coresdk-native.sh`
there, then verified `ldd` and `ENet_EXP_PeerChannelCount` before installation.

Update `docs/hosting.md` with the exact proven command during the next server
deployment rather than guessing it here.

## 5. Client release flow

Building `WorldsAdriftReborn` normally copies the DLL directly into the local
game plugin folder. A running game keeps the already-loaded assembly, so every
client change requires a full game close/relaunch.

Public release procedure is documented in `tools/patcher/README.md`:

1. assemble a pack containing `plugin/` and `gameroot/`;
2. put the newly built `WorldsAdriftReborn.dll` in `plugin/`;
3. run `tools/patcher/build-manifest.sh` with a new version/build label;
4. `rsync -av --delete tools/patcher/dist/` to
   `/opt/wareborn/patch/` (the patch directory is generated and may be deleted;
   this exception does **not** apply to server deployment directories);
5. fetch the public manifest and payload and compare SHA-256.

WAPatch now writes the four public connection keys itself. Players should not
be told to manually edit `BepInEx/config/WorldsAdriftReborn.cfg` for the public
server.

## 6. What this session shipped

The important integrated chain is `13a5303` through `f837c5a`, followed by the
client-only panel iterations through `7c3e6c4`.

### Whole-island Haven resources

- Haven is populated deterministically from extracted collision-surface data,
  not a tiny hand-authored spawn patch.
- The live starter-biome profile is intentionally birch/iron weighted rather
  than a random species/material assortment.
- Current full tables: 81 birches (including the proven starter anchor), 40
  iron deposits, fuel pods, databanks, and atlas shard companions.
- Full deposits now use all three shipped `MetalDepositVisuals` shapes in a
  deterministic 01/02/03 placement-index cycle. Variant 03 is the tall formation
  seen in historical footage; biome controls material while metal type/quality do
  not select shell geometry. `WAREBORN_DEPOSIT_VARIANT` remains a global test
  override. The adjacent boulder, nugget, scrap, tree, databank and fuel-pod
  boundaries are recorded in
  `docs/research/findings-resource-visual-variants.md` rather than guessed into
  the same contract.
- The old 1010/1011 idea is not viable with the player client: retail's island
  resource sampler lived in server-side Unity workers and is absent from the
  shipped player binary. Offline generation is the correct current fallback.
- Core files:
  `Resources/SurfacePlacementGenerator.cs`, `Resources/HavenSurface.cs`,
  `MetalDeposits.cs`, `Trees.cs`, `FuelPods.cs`, and
  `Game/Gathering/WorldResourceActivation.cs`.

### Resource interest and authority

- Nearby resources join the loading barrier; distant resources are not sent at
  login.
- Connect-time ship interest now applies the ship domain's 800 m load radius to
  the hull root and makes one decision for its hull, deck panels and mounted
  parts. Remote ships are not instantiated and immediately removed during login;
  live ship interest adds them root-first when approached. Free loose parts stay
  outside this rule so they cannot become unreachable.
- First live acceptance of `718d926` exposed a second visibility owner: generic
  runtime catch-up re-sent the remote built ships/mounted parts after the plan
  had correctly skipped them, producing a large send/remove burst. `9143c5a`
  excludes ship-managed entities from generic catch-up and adds a headless
  connect -> catch-up -> approach test. Validation is 2,316/2,316 tests and a
  zero-error Release server build; it is deployed as part of `ab9bc94`.
- Movement component 1073 drives a 500 ms per-peer reconciliation.
- Adds are nearest-first and paced at 120 ms with asset request then AddEntity.
- Runtime deposits/shards enter interest through explicit `RegisterRuntime`.
- Component-interest is guarded by per-peer checkout state so a late request
  cannot resurrect an unloaded entity.
- Dynamic resources are activated with the same authoritative harvest state as
  boot resources; this fixed the prior symptom where streamed trees/rocks were
  visible but yielded nothing.
- Native channel 5 carries `RemoveEntity`; the Windows x64 `long`/`int64_t` ABI
  mismatch was fixed in `fc4efec`.
- PR4 makes the 1073 coordinate frame island-aware. A terrain `relativeTo`
  selects the peer's stable `IslandId`; aboard-ship and teleport positions use
  global coordinates directly.
- Resources are assigned to an owning island. Reconciliation includes the
  active island plus old loaded entries long enough for hysteresis removal;
  never-visited distant-island resources remain unloaded.
- Remove capability is now explicit: the native shim exposes the peer's
  negotiated ENet channel count. Six-channel peers unload through channel 5;
  older peers retain visited resources without risking inert re-adds.
- The Trades Challenge carries only its recovered profile: five Aluminium Q4
  deposits and five databanks, with no invented trees, fuel or ore assortment.
  See `docs/research/findings-multi-island-resource-interest-pr4.md`.

This matters to the earlier report that one friend crashed during loading while
the host only lagged: initial radius gating reduces the boot burst, but retained
visited resources can still accumulate. No final Colin-specific crash diagnosis
was established from the one available log.

### Spawn/load reliability

- `SpawnAckTimeoutPolicy` prevents one lost acknowledgement parking the rest of
  the spawn plan forever.
- The connect-time interest boundary no longer sends the final gated AddEntity
  without its asset request.
- Global biome data, placed deployables, loose parts, hulls/decks and other
  load-bearing entities are in the initial barrier where appropriate.
- World prefabs are precached; client rescue now acknowledges the associated
  request.
- See `LoadBarrierPolicy`, `SpawnAckTimeoutPolicy`, `SpawnPlan`, and
  `Patching/SpatialOS/AssetLoadAck_Patch.cs`.
- The 2026-08-14 post-PR4 crash audit found no duplicate *world-resource*
  AddEntity and no post-activation server packet burst in the second failed
  run. The installed client DLL was byte-identical to the one used by an
  82-minute known-good run. The actual server-side regression was the coupling
  of the 120 m roaming radius to the unpaced loading-barrier initial set: after
  fixing the old producer race, more in-radius resources correctly moved into
  synchronous connect-time instantiation. Keep connect radius, live radius and
  the settle window separate; do not "fix" this by re-enabling concurrent spawn
  producers. A later real-wire two-peer audit did expose the separate remote
  avatar mirror retrying AddEntity three times and racing live 1073 ahead of its
  seed. Production mirror creation is now single-shot, movement is held until
  AddEntity plus both 1073/190602 seeds are served, and tier 2 fails on either a
  duplicate avatar or non-monotonic timestamp. Departed avatars are removed on
  channel 5 instead of being left as ghosts. Runtime rendering still requires a
  live client confirmation.

### Stations and placement

- Shipyard and assembly station placement is authoritative, persistent, shared,
  and restored before spawn-plan snapshotting.
- Station pickup returns the inventory item; the client hides the static
  placed root only after the authoritative interaction-enabled transition.
- Empty shipyards can capture an existing persisted ship again, enabling
  sequential builds once the prior hull has departed.
- The dock registry is bidirectional. First non-neutral piloted input undocks
  the hull, clears persistence, and updates yard/hull dock components.
- Core files: `Game/Placement/PlacementService.cs`, `PlacedShipyards.cs`,
  `PlacedCraftingStations.cs`, `ShipDockRegistry.cs`, and
  `Game/Crafting/BuiltShipSpawner.cs`.

### Flight, helm and sails

- The server owns ship flight integration and publishes 1130/190602 state.
- Voluntary helm release latches forward/reverse throttle and clears transient
  pitch/yaw/roll/vertical axes. Explicit zero settles the ship. Disconnect uses
  a separate emergency-neutral `Abandon()` path.
- Re-manning seeds the delta ledger from the latched state so an omitted field
  cannot reset the lever.
- Helm entry snaps the local body/camera to the authored `#PilotPosition` anchor.
- Sails are functional. The current reconstruction adds linear forward
  speed/acceleration per unfurled sail, bounded by flight speed policy. It is
  not yet retail wind/tacking/rigidbody torque simulation.
- Ship-part interaction holds are clamped to a short consistent duration.
- Core files: `Ship/Flight/`, `Game/ShipFlightService.cs`, `Sails.cs`,
  `Patching/Flight/PilotBodyAnchor_Patch.cs`, and
  `Patching/Flight/HelmInteractTime_Patch.cs`.

### Crafting and ship-part materialization

- Craft reservations return excess material instead of consuming the entire
  dragged stack.
- Every current loose-part catalogue row now has the component state required
  to materialize: panels/windows, decks, modular engine/wing parts, utility
  items, helm, sail, sky core, lights, storage, etc.
- Crafted loose outputs occupy deterministic non-overlapping slots; a persisted
  overlap migration separates old coincident outputs.
- Attachments use normalized placement/interaction policy rather than one-off
  fixes per lamp/helm/sail.
- Core files: `LoosePartCatalogue.cs`, `LoosePartDefinition.cs`,
  `LoosePartPlacement.cs`, `Game/Crafting/LoosePartSpawner.cs`, and component
  branches in `ComponentsSerializer.cs`.

### Ship persistence and salvage

- Built hulls, decks, mounted parts, loose parts and dock relationships persist.
- The deck restore path avoids applying hull rotation twice (`c7e71c8`).
- Shipyard UI frame salvage removes the docked frame transactionally, refunds
  recipe materials, and drops attached parts.
- The salvage weapon can dismantle mounted parts only inside an owned shipyard,
  refunding their recipe materials and removing them from world/persistence.
- The generic policy covers the complete catalogue rather than only helm/light.
- Core files: `ShipSalvageService.cs`, `MountedPartSalvageService.cs`,
  `ShipSalvagePolicy.cs`, `ShipPartSalvagePolicy.cs`, and
  `WorldStatePersistence.cs`.

## 7. Active issue at handover: panel exterior placement

This remains **visually unaccepted**. Do not report it as solved without a new
in-game screenshot and trace.

User expectation: a medium panel aimed at an upper frame rail should sit above
the visible outer frame, not intersect an inner member or hang beneath it.

History:

- `a224cd7`: first exterior recast, but panel detection missed inactive phantom
  children, so it never ran.
- `171a2e5`: detects inactive phantom/original panel correctly.
- `b2204c1`: probes six exterior directions, but the generated `ShipSideHull`
  has roof holes and could return no upward SRC hit.
- `7c3e6c4`: for a vertically struck rail, measures live rendered hull bounds,
  places the panel 6 cm above the hull envelope, forces ship-up normal, and logs
  successful projection or fallback.
- Live traces from that build proved the general side path still applied a
  `0.00 m` correction: the exterior recast found the same beam skin and left
  the pivot there, embedding the inner half of the 0.10 m panel thickness.
- `355d842`: moves the pivot 0.06 m along the sign-corrected sloped exterior
  normal (5 cm half-thickness plus 1 cm clearance), and logs actual rendered
  and collider projection ranges relative to the selected hull skin.
- Public client containing the last change: WAPatch `2026.08.14-7`.

Next acceptance steps:

1. fully close and relaunch the client (the running process cannot load a new
   assembly);
2. lift a fresh/recovered medium panel and aim at the same upper rail;
3. capture a screenshot before confirming;
4. inspect:

   ```bash
   rg '\[WAR\]\[ship-panel\]' \
     /home/ttanurhan/Games/WorldsAdrift/BepInEx/LogOutput.log | tail -30
   ```

5. For the pictured side rail, the expected trace contains
   `SRC exterior ... pivot clearance 0.06 m`, followed by a `[geometry]` line
   with `pivotFromSkin 0.060 m`. Renderer/collider minima should be zero or
   positive; a negative minimum is measured penetration.
6. If visually correct, place it, reconnect, and verify the persisted pose.
7. If wrong, use the logged skin, pivot, renderer/collider ranges, original
   local point/normal and result; do not add
   another blind constant.

## 8. Persistence model and safety

The game server is a single poll loop. Most ledgers intentionally are not
thread-safe. World state writes are atomic JSON transactions.

Key persistence entry points:

- `Game/Persistence/WorldStatePersistence.cs`
- `Multiplayer/Persistence/WorldStateSnapshot.cs`
- `Game/Inventory/InventoryPersistence.cs`
- `Game/Knowledge/ProgressionPersistence.cs`
- account/roster persistence in `WorldsAdriftServer` and
  `WorldsAdriftReborn.Storage`

Before a persistence migration or destructive gameplay test:

1. resolve the actual `WAREBORN_DATA_DIR`/live file;
2. copy the specific file to a timestamped backup;
3. verify restore counts after restart;
4. never delete the whole deployment or data directory;
5. record whether a rollback loses post-backup player progress.

## 9. Elastic Simulation Runtime and world expansion

Three external design documents informed discussion:

- `/home/ttanurhan/Downloads/Telegram Desktop/WAREBORN_ELASTIC_SIM_RUNTIME_CODEX_HANDOVER_V2.md`
- `/home/ttanurhan/Downloads/WAREBORN_CODEX_HANDOVER_PR1_ISLAND_IDENTITY.md`
- `/home/ttanurhan/Downloads/WAREBORN_WORLD_EXPANSION_ROADMAP.md`

They are **design inputs, not implementation status**. At this snapshot:

- there is no `SimulationCore` project;
- there is no `SimulationEntityId`, `SimulationDomainId`, domain scheduler,
  authority generation, gateway/worker split, or migration protocol;
- PR1 stable `IslandId`, `IslandDefinition` and `IslandRegistry` are implemented;
- the preserved WAMap importer, production Trades Challenge terrain and
  island-aware resource interest are implemented and deployed through the
  staged resource-login server revision;
- Phase 1 region topology now exists as dependency-free `RegionId`,
  `RegionDefinition` and `RegionRegistry`. It maps both proven islands exactly
  once but is deliberately not connected to runtime behavior yet.

### Agreed strategic direction

- The client must continue to see one server/gateway and the existing protocol.
- Do not build multi-process meshing now.
- First make boundaries describable inside one process and one poll loop.
- Natural future authority units are islands and whole ships. Never distribute
  a hull, helm, sails, mounted parts and aboard players across independent
  authorities.
- Strong physical interactions (later grapples/ship collisions) imply temporary
  domain affinity or merging.
- Authority generations are required before any migration so stale-worker
  writes can be rejected.
- A ship capture/destroy/restore/resume experiment is the best later proof of
  domain snapshot completeness.

### Current architecture sequence

The accepted phased plan is
[`architecture/elastic-runtime-phases.md`](architecture/elastic-runtime-phases.md).
Phase 1 stable region topology and Phase 2's read-only world directory are
implemented. The first whole-ship portion of Phase 4 is now implemented locally:
`ShipDomain` owns a hull's flight session, pilot authority, generation, deck and
mounted membership, aboard peers and a versioned resumable snapshot. Live helm
input carries an authority token and stale-generation input is rejected.

Replication now evaluates interest once for the whole ship and emits each
flight frame in root-first order: hull 1130, optional hull 190602 wake, then the
mounted-member 190602 wakes. The legacy ENet operations remain ordered rather
than atomic, because the shipped client protocol has no multi-entity update op.
The server logs sampled `[ship-domain]` generation/sequence/delivery counters.

Whole-ship checkout is per viewing peer and uses ship-specific island-scale
radii (800 m load / 1,000 m unload by default) plus channel-5 RemoveEntity.
These are deliberately separate from the much tighter resource radii. An empty
ship may unload for Colin while remaining checked out and moving for a nearby
observer; checkout never parks, freezes, migrates or deletes its `ShipDomain`.
Unmanned/uncrewed ships leave member-first/root-last
and return root-first/member-last on a 120 ms cadence. Pilot/aboard protection is
revalidated at send time. Because remote player entities are still globally
relayed, any crew or active pilot temporarily pins the complete ship globally;
otherwise a far observer could retain a floating avatar after its ship unloaded.
Older clients without RemoveEntity retain both the ship and its motion rather
than freezing a ghost. Late component-interest is rejected after unload.

Passenger carry keeps the exact raw contact entity required by the legacy
client while canonicalizing hull/deck/part membership to one ship root. A one-second
grace absorbs collider-seam `relativeTo=-1` flicker; real island/non-ship leaves
remain immediate. The first two-player production acceptance on `6a2273f`
failed in three bounded ways: remote avatars ran ahead of/behind their moving
ship, a small helm turn after an idle period took exactly five seconds to become
visible, and a removed ship did not reliably re-checkout on return. The exact
five-second delay is now proven to be the retail client's slow spline correction
after our manned-idle 1130 stream went quiet; the local fix keeps a 240 ms stream
while manned and primes it before enabling controls. The avatar divergence was
raw `relativeTo=-1`/bias-zero collider-seam churn being relayed before canonical
aboard state; the local fix holds only those coordinate-frame edges while the
canonical ship survives its measured grace. The re-checkout loop discarded an
in-flight asset request every 500 ms reconcile; the local fix carries a still-
valid head request and revalidates every Add/Remove at send time. All three fixes
are deployed in `ab9bc94` but are not yet visually accepted. Phase 4 is therefore
deployed as a foundation but is **not visually accepted**.

The protocol/state-machine portion now has a repeatable two-peer acceptance
gate at `tools/relaybot/run-ship-acceptance.sh`. It creates a disposable world
and alternate-port native server, then drives two real ENet peers through
flight, mounted-member wakes, passenger contact-seam suppression, authority
handoff with stale input, independent whole-domain removal, and legal re-entry.
The 2026-08-15 run passed every assertion. This replaces Colin as the first-line
server regression test; it does not run Unity visualizers, interpolation,
camera/IK or rendering, so the phase remains visually unaccepted until a short
two-client presentation check.

Phase 5 has a pure capture/restore/resume proof, but not yet the full live
destroy/recreate/no-visible-teleport acceptance test. Phase 6 has ship authority
generations, but no in-process gateway seam yet.

### First tier-1 B3 terrain expansion (local, off by default)

The next release-world terrain cluster joins the preserved Bossa MapFile to the
final Cardinal survey. The complete Saborian tier-1 B3 district contains twelve
islands; its first four staged entries remain Mental Facility, Betrayal of the
Copper King, Highlands Hills and The Land that Man Forgot.
`WAREBORN_FIRST_REGION_TERRAIN_COUNT=0..12` selects a bounded terrain-only
prefix; zero is the default. Geographically closer C6 islands are
tier 3 and are intentionally deferred. All runtime topology consumers share
the same configured island/region registries, so spawn, resource routing,
directory ownership, local domains, databank parent resolution and admin stats
cannot disagree about which islands exist. Build `069a372` is deployed with the
bounded rollout set to exactly one terrain (`Mental Facility`); it is not yet
visually accepted. Mental Facility has the first guarded named landing destination,
`mental-facility`, derived from its extracted top surface; both the game server
and admin page refuse it unless at least the first tier-1 terrain is registered.
Do not jump to the complete district at once. Continuous distance checkout is integrated into
`feat/island-identity` at `7cbb376`, with exact cold-asset ACKs,
terrain/resource ordering and safe teleport deferral. It is deployed and enabled
for the one-island run, but is not visually accepted. All twelve bundles total
roughly 116.5 MiB compressed; the
original four-island acceptance prefix is roughly 42.5 MiB. Release-map origins,
terrain envelopes and joined survey profiles (databanks, revival chambers,
trees, turret/danger flags and metal tables) are pinned for all twelve, but no
new dynamic resource population is enabled by terrain registration alone.
See `docs/research/findings-first-region-terrain.md`.

Production verification after the `069a372` restart: stats schema 5 reported
`firstRegionTerrainCount=1`; the directory classified Mental Facility into
`tier1-b3-region`; the local host reported 3 island domains, 5 ship domains,
255 owned entities, 0 unowned entities and 0 ownership issues. The count-one
setting is a runtime systemd test override and therefore intentionally disappears
on VPS reboot unless promoted after visual acceptance.

### Terrain checkout observability (integrated by `a4e135c`, deployed)

Stats schema **5** adds a `terrain` section to `/tmp/wareborn-stats.json` so the
one-island visual acceptance run above can be observed instead of guessed. The
game server reads it from `IslandTerrainInterestService` on the same
authoritative poll loop that already ticks the service, and exports immutable
copies only: the read allocates no entity id, sends nothing, and never asks the
resource-drain gate (asking would mutate the send queue), so it cannot become a
second authority. It reports requested-vs-actually-enabled (the resource-interest
prerequisite can hold the feature back), the radii/ack-timeout/settle
configuration, per-peer lifecycle state keyed by **player entity id**, per-island
registration/ownership truth with envelope-backed extents, and a bounded 64-entry
ring of recent lifecycle events. Peer handles, packet payloads and paths are
structurally unable to reach the file: events carry a closed enum and a
process-local slot ordinal.

`/admin` gains a **Terrain checkout** view: a status strip, a player x island
matrix with expandable per-peer detail, an island inventory, the event timeline,
and an acceptance-run panel that drives the EXISTING guarded Haven /
Mental Facility travel commands rather than adding a command path. The semantic
states are `ABSENT`, `REQUESTING`, `WAITING ACK`, `READY`, `DRAINING`,
`UNLOADING`, `RETAINED (LEGACY)` and `ERROR`, derived once in
`IslandTerrainStatePolicy` so the server, the JSON contract and the console
cannot disagree. A schema-4 game server, a disabled feature and a legacy client
each render as a stated condition rather than an empty page. The panel reports
lifecycle only; whether the terrain LOOKS right stays a human judgement and is
never asserted.

The runtime checkout milestone (`7cbb376`) and its admin observability milestone
(`fa83318`) are consolidated on `feat/island-identity` by merge commit
`a4e135c`. They were pushed, deployed and enabled for the bounded Mental Facility
run in `069a372`; public manifest `2026.08.17-1` supplies the correlated native
acknowledgement. Final validation passed all 2,554 Multiplayer tests, all 181
admin/login tests, all affected Release builds and `git diff --check`.

The first real Unity run on 2026-08-17 proved exact request/asset-ack/add ordering,
deferred teleport, correct Mental Facility rendering and collision, and correct
Haven resource removal/re-checkout. It also exposed one real lifecycle defect:
this client proves teleport arrival through the bounded authoritative-transform
route but omits the sparse 1073 relative-to island acknowledgement. Resource
interest advanced correctly, while terrain interest retained the old requested
destination and therefore kept Mental Facility `READY` after the proved return to
Haven. The follow-up makes a proved teleport landing one shared authority event:
both legitimate arrival proofs update terrain ground identity and clear the
destination pin, allowing normal drain/unload. It also reports a queued
`teleport-wait` as an accepted wait rather than a failed operation. Headless
regression coverage proves the return produces exactly one old-terrain removal;
The fix is deployed as `b52f504`.

The repeat production run on `b52f504` completed that gate. One current v1
client performed two full Haven → Mental Facility → Haven cycles in the same
session. Each outbound leg recorded `teleport-wait` (accepted), request, exact
asset ACK, add and teleport-ready; the player visually confirmed correct terrain
and collision after the second post-removal add. Each return advanced resource
interest to Haven, cleared the destination pin and recorded `remove-ok`. Final
telemetry showed both managed islands `ABSENT`, no pending action, zero ready,
zero retained, zero errors and no warning. The one-client teleport-driven
load/unload/re-entry lifecycle is therefore visually accepted. Distance approach
and independent two-client checkout remain separate acceptance gates.

The first proximity run then flew ship 176 from Haven to The Trades Challenge.
At roughly 1,153 m from the extracted terrain envelope, checkout recorded the
exact `request -> asset-ack -> add-ok` sequence and reached `READY` without a
teleport. The player landed, disembarked and visually confirmed stable terrain
and collision. This accepts one-client ship-approach terrain loading, but exposed
a separate on-foot spatial-interest defect: all 15 recovered Trades resources
(five Aluminium Q4 deposits, five Atlas shards and five databanks) remained
unchecked-out even at the island centre. Telemetry showed the interest position
frozen at the disembark point. The client continues sending ownership-gated
authoritative global 190602 transforms while walking, but the interest services
were fed only from sparse 1073 `positionRelative`/`relativeTo` fields that can
stop changing after disembark.

The local follow-up routes each unparented authoritative 190602 player pose into
both resource and terrain interest. It reuses `FallWatch`'s accumulated sparse
parent state so a parented local transform cannot be mistaken for a global
coordinate. Full Multiplayer validation remains 2,556/2,556 with a clean Release
server build. This server-only correction is deployed as `1aa9fe4`; no client
patch changed. Post-restart verification reported schema 5, terrain mode `on`,
the count-one B3 topology, 3 island domains, 5 ship domains, 255 owned entities,
zero ownership issues and zero terrain warnings/errors.

Live acceptance on `1aa9fe4` succeeded: after a ship approach and disembark, the
player walked across Trades and the authoritative interest centre continued to
move. Twelve of the island's 15 entities were concurrently checked out. Four
databanks rendered, accepted interaction and each durably awarded 10,000
knowledge (32,391 -> 72,391). Metal and Atlas-shard deposits also entered and
left the 120 m bubble as the player moved. Terrain remained `READY` with zero
terrain warnings/errors. This accepts one-client on-foot resource and databank
streaming.

The same run exposed a non-blocking approach-boundary conflict: while aboard,
the 1073 + hull-pose source classified interest by the ship's island affinity,
then each 190602 pose independently classified it by nearest island, alternating
Haven/Trades until the boundary crossing completed. The local follow-up gives
the canonical aboard tracker precedence: 190602 drives spatial interest only
when unparented and not aboard; ship-derived 1073 remains the sole aboard source.
The focused policy covers every fall verdict in both aboard/on-foot states; all
2,570 Multiplayer tests and the Release server build pass. Do not deploy that
follow-up until the acceptance player disconnects.

The release MapFile also proves wall geometry. The nearest Haven separator is a
type-5 WorldEndWall about 1.061 km west of active Haven; prior notes treating
exact release wall placement as missing are superseded. Wall behavior remains
unimplemented.

## 10. Known risks and unfinished work

- **Panel placement:** WAPatch `2026.08.14-7` is awaiting visual acceptance.
- **Resource unload:** capability is implemented in transport, but runtime is
  load-near/retain-visited compatibility mode as described above.
- **Loading/crash validation:** Colin's remote loading crash was identified as
  native heap corruption (`c0000374`) and fixed in `3a7cd31` / manifest
  `2026.08.14-10`; his subsequent join passed the former crash point. Extended
  play then exposed the separate server replication congestion and connect-time
  whole-fleet loading now addressed through deployed revision `ab9bc94`.
- **Sail fidelity:** functional scalar propulsion, not retail wind physics.
- **Crafted-part sweep:** catalogue contracts are tested, but every visual,
  attach surface and functional interaction has not been manually exercised.
- **Multiple players / moving ships:** `489517f` fixed late-join delivery,
  steering wake-up, passenger-frame coherence and the reliable congestion
  spiral. `ab9bc94` prevents remote domains from burdening login. `6a2273f`
  introduced canonical carry and coherent
  ShipDomain replication, but its first two-player visual pass exposed the
  timeline/re-checkout failures listed above. Do not claim local domains are a
  completed dynamic handoff system: all domains still run in one process and
  there is no gateway host, remote worker, authority transfer, or live snapshot
  restore seam yet.
- **Server restart reconnect:** still session-ending; separate gateway/worker
  architecture is not required to fix the existing shim reconnect path.
- **Hosting docs:** native runtime description is current, game deploy command
  is stale Wine-era text.
- **Roadmap:** historical and stale; reconcile it against this file/current code.
- **Security:** rotate the database credential exposed through the systemd
  environment/drop-in and use a root-only environment file. Never reproduce the
  old value.

## 11. Investigation playbooks

### Client visual/interaction bug

1. reproduce once and save screenshot;
2. inspect `BepInEx/LogOutput.log`, `UnityClient@Windows_Data/output_log.txt`,
   and `CoreSdk_OutputLog.txt`;
3. locate retail class/method in the decompile;
4. identify whether the failure is component state, asset load, transform,
   interaction timing, or server rejection;
5. add low-volume event diagnostics, not per-frame spam;
6. build client, fully restart, retest;
7. publish manifest and verify public SHA only after acceptance-quality build.

### Server gameplay transaction bug

1. find the inbound component/event handler;
2. identify the authoritative ledger and persistence transaction;
3. place validation in the pure Multiplayer project where possible;
4. test reject paths, duplicate/idempotent paths, restart restoration, and
   cross-player ownership;
5. build server and inspect `git diff --check`;
6. backup state and deploy only with all players disconnected.

### Resource bug

Follow the whole lifecycle:

```text
registration -> interest classification -> asset request -> AddEntity
-> NoteLoaded -> component-interest gate -> authoritative component seeds
-> damage/harvest handler -> yield transaction -> persistence/depletion
-> optional RemoveEntity -> component/ref cleanup -> clean re-add
```

Do not equate “the prefab renders” with “the resource is authoritative.” That
mistake caused the dynamically streamed visible-but-inert trees and rocks.

### Ship transform bug

Keep coordinate frames explicit:

- registry/global pose;
- live flight-session hull pose;
- hull-local mounted-part pose;
- parent marker (`~`, `deck`, etc.);
- packed quaternion composition.

Never apply hull rotation twice. Use `ShipPartTransform`,
`BuiltShipPlacement`, `ShipHullMetrics`, and the existing orientation probe.

## 12. Completion standard

A change is not complete merely because it compiles or a green phantom appears.
For this project, completion normally means:

- pure policy/regression tests pass;
- relevant server and/or client builds pass;
- persistence and ownership behavior are covered where applicable;
- live logs show the intended branch executed;
- visual behavior is inspected for client-facing changes;
- reconnect/restart behavior is checked for persistent changes;
- the exact built artifacts are the ones deployed;
- public patch manifest and payload hashes match for client changes;
- this handover's production snapshot and active issues are updated.

## 13. Upstream credit and project identity

Wareborn is a fork and continuation of the original WAReborn community work.
Preserve upstream copyright/license notices, keep the upstream repository and
community credits in `README.md`, and describe Wareborn additions as fork work
rather than erasing the original project's authorship.
