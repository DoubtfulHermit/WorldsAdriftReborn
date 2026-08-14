# Wareborn Engineering Handover

**Canonical entry point for a new maintainer or coding agent**

**Snapshot:** 2026-08-14, Europe/Berlin

**Repository:** `DoubtfulHermit/WorldsAdriftReborn`

**Active integration worktree:** `/home/ttanurhan/Games/wareborn-resource-hotfix`

**Active branch at this snapshot:** `fix/resource-stream-authority`

**Code baseline at this snapshot:** `355d842` (`Offset ship panels beyond the exterior skin`)

This file is the current operational and architectural handover. Start here,
then follow the narrower documents it links. Do not treat old roadmap entries,
downloaded design briefs, branch names, or chat summaries as proof that code is
implemented.

## 1. First 15 minutes

1. Work in `/home/ttanurhan/Games/wareborn-resource-hotfix` unless the user
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

   At this snapshot the Multiplayer suite passes **2201/2201**, and both server
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

`docs/roadmap.md` contains valuable historical research but is materially stale:
it still describes entity removal, inventory, ships, resources, and other work
as absent even though later commits implemented them. Update it before using it
as a planning board.

## 3. Repository and runtime map

| Area | Purpose | Primary entry points |
| --- | --- | --- |
| `WorldsAdriftReborn/` | BepInEx client mod, Harmony patches, client diagnostics | `Plugin.cs`, `Patching/` |
| `WorldsAdriftRebornCoreSdk/` | Native client/server protocol shim and ENet transport | `Connection.cpp`, `Dispatcher.cpp`, `enetLayer.cpp`, `OpList.h` |
| `WorldsAdriftRebornGameServer/` | Authoritative game server and main poll loop | `WorldsAdriftRebornGameServer.cs`, `Game/`, `Networking/` |
| `WorldsAdriftRebornGameServer.Multiplayer/` | Engine-free policies, ledgers, catalogues, geometry | resource, inventory, placement, ship and flight types |
| `WorldsAdriftRebornGameServer.Multiplayer.Tests/` | Fast native regression suite | 2201 tests at this snapshot |
| `WorldsAdriftServer/` | Login, accounts, roster and patch-file HTTP service | request handlers, storage integration |
| `WorldsAdriftReborn.Storage/` | PostgreSQL models/repositories/migrations | storage tests require `WAREBORN_DB` for integration cases |
| `tools/patcher/` | WAPatch and manifest release pipeline | `README.md`, `build-manifest.sh` |
| `tools/relaybot/` | Native shim builder and protocol/load diagnostics | `build-coresdk-native.sh`, relay bot |
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

- **Game server:** deployed/restarted from `f837c5a` at 2026-08-14 12:31 CEST.
  Commits after it in this handover are client-only panel-preview work.
- **Public client manifest:** `2026.08.14-7`, build label
  `panel exterior half-thickness + geometry trace (355d842)`.
- **Client DLL SHA-256:**
  `99ebb821bf165cf36f51dbf6c9c114c31ad9f28828f5c1a939abe6e9e57970bc`.
- **Server state:** active at the time this handover was written.

Do not put database passwords, session tokens, account records, or private
connection strings in documentation, commits, commands whose output is pasted
into chat, or issue reports. In particular, avoid printing the full systemd
`Environment=` property. A production database credential currently exists in
a systemd drop-in; rotate it and move it to a root-only `EnvironmentFile` when
doing the next security/operations pass.

### Deployment warning

The top of `docs/hosting.md` correctly describes the native Linux game server,
but its game-server deployment command still shows the former Wine/win-x64
recipe. Do not copy that stale block for production. The safe native flow is:

1. build/test the selected commit;
2. publish the game server for `linux-x64` self-contained into a fresh staging
   directory;
3. build and include `libCoreSdkDll.so` if native shim sources changed;
4. preserve/backup the remote `data/` directory and current native deployment;
5. sync staged files without deleting persistent data;
6. restart only after the user confirms every player is disconnected;
7. inspect boot restore counts, resource-interest banner, hull metrics, and
   first connection logs.

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

**Current compatibility limitation:** `ResourceInterestService.PeerState`
initializes `RemoveSupported` false. Therefore production currently loads
nearby resources and retains every resource that peer has visited; it does not
actively unload them. This was deliberate after legacy-client uncertainty, but
it means memory/entity load grows as a player explores. Before enabling removal,
add a real protocol/client capability signal or prove all supported clients run
the channel-5 ABI-fixed shim; do not merely flip the default and hope.

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
- there is no implemented `IslandId`, `IslandDefinition`, or `IslandRegistry`;
- branch/worktree `feat/island-identity` at
  `/home/ttanurhan/Games/wareborn-loading` still points to base `e2e88bf` and
  contains no PR1 implementation.

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

### Correct next architecture PR

Do **PR1 island identity**, not `SimulationCore`:

1. audit every one-island assumption into
   `docs/research/findings-island-pipeline.md`;
2. add stable `IslandId`;
3. add minimal evidenced `IslandDefinition` and deterministic `IslandRegistry`;
4. represent Haven through it;
5. add narrow island-local/global coordinate conversion;
6. prove every current Haven global resource position and origin is unchanged;
7. manually verify login, spawn, terrain, streaming, harvest, structures,
   ships, flight and reconnect behavior.

Non-goals for PR1: second island, WAMap import, regions, Wind Walls,
`SimulationCore`, workers, domains, migration, generic interest, Postgres world
state, and protocol changes.

After PR1, follow the world expansion sequence:

1. research-only WAMap importer with provenance and proven coordinates;
2. one second island with proven asset/context/transform;
3. multi-island resource interest preserving current semantics;
4. concrete region topology;
5. deterministic Wind Wall geometry;
6. deterministic Wind Wall flight disturbance;
7. only then extract generic runtime concepts demonstrated by both islands and
   ships.

## 10. Known risks and unfinished work

- **Panel placement:** WAPatch `2026.08.14-7` is awaiting visual acceptance.
- **Resource unload:** capability is implemented in transport, but runtime is
  load-near/retain-visited compatibility mode as described above.
- **Loading/crash scalability:** one remote friend crashed during loading and
  earlier lagged/crashed after spawn; only one log was available and no final
  machine-specific cause was proven.
- **Sail fidelity:** functional scalar propulsion, not retail wind physics.
- **Crafted-part sweep:** catalogue contracts are tested, but every visual,
  attach surface and functional interaction has not been manually exercised.
- **Multiple players:** major paths are N-way by design, but most live gameplay
  validation used one or two players.
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
