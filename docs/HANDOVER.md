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

- **Game server:** `b652034`, deployed and restarted at 2026-08-18 08:05 CEST.
  **Login/admin server:** `b652034`, same pass. **Client manifest
  `2026.08.18-2`**, 54/54 public payloads verified, one payload changed.
  This deploy carries: asynchronous island bundle loading (the real cause of the
  approach stutter - our own offline-asset patch had made retail's async loader
  blocking), the reconstructed Bossa social/crew HTTP API, spawn terrain
  preloading, tree felling, material-driven ship mass, tier-1 world activation,
  inferred island metals (354 -> 1930 deposits), 13,266 trees, the Wilderness
  shrine, the pure fauna core, and stock knowledge values.
  It **migrated the production database from v6 to v7**, adding `social_invites`;
  verified after restart as `version = 7` with the table present and the other ten
  unchanged. Dumped first to `pre-b652034-20260818T060351Z/wareborn-db-pre-v7.sql`.
  Boot reports all four per-character stores ON, restore unchanged at 4/4
  deployables, 5/7 hulls, 16/16 mounted and 3/3 loose, `owned=543 unowned=0
  duplicates=0`, and zero errors.
  **The relay soak gate was repaired in this pass and passes FLAT** - see
  `tools/relaybot/run-soak.sh`. It had been unable to measure anything since the
  native migration because it ran the server under Wine against a Windows shim
  predating `ENet_EXP_PeerChannelCount`, so it reported "setup failed" rather than
  a verdict. First green run: 21,606 sends, 100% delivered, drift 0 ms, trend
  -0.01 ms, zero disconnects or timeline violations.
  Production still runs the temporary `WAREBORN_RELEASE_WORLD_DISTRICTS=C6`
  config, so **the Wilderness is CLOSED and the shrine refuses with a message**
  rather than moving anyone. Set `tier1` to open it.
  Rollback: `/opt/wareborn/backups/pre-b652034-20260818T060351Z/{game,login,patch,live-data}`
  plus the SQL dump. v7 is additive; rolling the binary back needs no database action.
  The previous deployment was `958c8e1` at 2026-08-18 00:07 CEST.
  **Login/admin server:** `958c8e1`, same pass. This deploy carries the solid
  hazed compact island shell, the revived CREW system, and the client authority
  grant without which crews are silently unreachable. It **migrated the
  production database from schema v5 to v6**, adding `crews` and `crew_members`;
  verified after restart as `version = 6` with both tables present and the other
  eight unchanged, and the database was `pg_dump`ed first to
  `pre-958c8e1-20260817T220636Z/wareborn-db-pre-v6.sql`. Boot reports all four
  per-character stores ON (inventory, knowledge, logout position, crew), restore
  counts unchanged at 4/4 deployables, 5/7 hulls, 16/16 mounted and 3/3 loose,
  `owned=349 unowned=0 duplicates=0`, terrain 4000 m load / 4800 m unload, and
  zero errors in the first four minutes.
  Crews are **deployed but never exercised against a live client**: the rules and
  persistence are tested exhaustively, but whether the retail crew UI renders and
  drives them is unproven. The panel is the **CREW tab of the Social Sheet**
  (`InputButtons.OpenSocial`, "Open Social Sheet" in the controls list); the
  default key is not recoverable from the decompile or from PlayerPrefs, so read
  it off the in-game controls screen. Silence in the log on a crew click means
  the event never arrived.
  Rollback: `/opt/wareborn/backups/pre-958c8e1-20260817T220636Z/{game,login,patch,live-data}`
  plus the SQL dump. The v6 tables are additive, so rolling the binary back needs
  no database action.
  The previous deployment was `c31e8be` at 2026-08-17 21:55 CEST.
  **Login/admin server:** `c31e8be`, deployed and restarted in the same pass.
  This deploy carries the ownership-bootstrap crash fix, the corrected compact
  island shell, and logout-position persistence. It also **migrated the
  production database from schema v4 to v5**, adding `character_positions`;
  verified after restart as `version = 5` with the table present and the other
  seven unchanged. The database was `pg_dump`ed first to
  `pre-c31e8be-20260817T195325Z/wareborn-db-pre-v5.sql` (68 KB). Boot reported
  all three per-character stores ON (inventory, knowledge, logout position),
  restore counts unchanged at 4/4 deployables, 5/7 hulls, 16/16 mounted and 3/3
  loose, `owned=349 unowned=0 duplicates=0`, and zero errors in the first five
  minutes.
  **Production is still running the temporary `WAREBORN_RELEASE_WORLD_DISTRICTS=C6`
  visual-acceptance config** from the drop-in
  `/etc/systemd/system/wareborn-game.service.d/release-world.conf`: 16 terrains,
  compact-outline shell fidelity, Mental Facility NOT registered. Remove that
  file and restart to return to the bounded one-terrain topology.
  Rollback: `/opt/wareborn/backups/pre-c31e8be-20260817T195325Z/{game,login,patch,live-data}`
  plus the SQL dump above. Note the v5 table is additive, so rolling the binary
  back does not require touching the database.
  The previous deployment was `ccfb138` at 19:10 CEST.
  This is the merged retail-LOD shell preference plus the admin map provenance
  labelling. The rollout switch `WAREBORN_RELEASE_WORLD_DISTRICTS` is NOT set,
  so production remains the bounded one-terrain topology; the deploy is a
  behaviour-preserving baseline, not the release-world rollout. Boot proved the
  fix directly: the startup line reads `[island-shell] distant non-physical
  island visuals: ON; fidelity=retail LOD (v1 preferred: the managed terrain
  set is bounded, so the island bundle prefetch is affordable)`, which is the
  branch that would have silently downgraded Mental Facility and The Trades
  Challenge to compact outlines had the fidelity stayed keyed on catalogue
  membership. Restore counts matched the previous boot: 4/4 placed deployables,
  5/7 hulls with the two salvaged tombstones correctly skipped, mounted and
  loose parts intact. `[world-directory]` classified 256 registrations
  (global=1, region=181, ship=74 across 5 hull roots). Terrain reported schema
  6, mode `on`, 3 islands, enabled, zero warnings and zero errors. The
  deployed game managed DLL is SHA-256
  `ff0d69007465253818c66159484d93f79b02ca9a7228e896a9c73a692117aae2` and the
  login/admin managed DLL is
  `58098d8af571c23511aa19dcdecb4a40bfa0239ef7ceddebacf4f51ded5fdc16`; both
  match the staged publish exactly. `WorldsAdriftRebornCoreSdk` did not change,
  so no native shim was rebuilt and the production `libCoreSdkDll.so` remains
  `0121219a138a07f345103f83cc5647f993ecb0282a0172c7bf19a54b78a252f7`.
  Coordinated rollback:
  `/opt/wareborn/backups/pre-ccfb138-20260817T170732Z/{game,login,patch,live-data}`.
  NOTE for future deploys: `WorldsAdriftRebornGameServer-native/data` is a
  SYMLINK to `../WorldsAdriftRebornGameServer/data`, which is where the live
  `world-state.json` actually lives. Back up that real directory (the backup
  above keeps it as `live-data`), and never rsync a staging tree that contains
  its own `data/` entry without excluding it, or the symlink is replaced and
  persistence is orphaned. Restarted with zero players connected and zero
  connects recorded for the whole prior uptime. Not visually accepted.
  The previous deployment was game `3d64a7f` at 13:52 CEST and login
  `2994db3` at 2026-08-17
  15:25 CEST. Stats schema 6 reports terrain checkout, runtime
  topology and authoritative player world positions, and the admin
  console exposes its one-island acceptance run. Production remains bounded to
  `WAREBORN_FIRST_REGION_TERRAIN_COUNT=1` and reports 3 island domains, 5 ship
  domains, 255 owned entities, one explicit global, zero unowned entities and
  zero ownership inconsistencies. Terrain checkout is enabled with the existing
  120 m resource-interest prerequisite; its defaults are 1200 m load / 1600 m
  unload. Opt-in distant island shells are also enabled: after login the server
  prefetches managed optional-island bundles and the matching client builds a
  non-physical last-retail-LOD silhouette, reveals it only after its generated
  material is ready, hides it while full terrain is checked out, and restores it
  after full terrain removal. Collision, resources and databanks remain
  exclusively on physical checkout. This retail-LOD (v1) shell is the preferred
  fidelity and remains what the bounded configuration requests: shell fidelity is
  chosen by `IslandShellFidelityPolicy` from whether the complete release-world
  rollout is active, not from release-catalogue membership, so embedding the
  254-island catalogue does not change what production sends. Live visual
  acceptance is still required.
  The authenticated Simulation Fabric also embeds an allowlisted projection of
  the preserved release MapFile (266 islands, 20 tier/biome cells and 44 typed
  weather-wall segments). Eighteen cells retain their authored district IDs;
  the two Tier-4 cells whose district is explicitly null are visibly signed as
  unassigned rather than invented as E1/E2 or merged into E3. Its layered SVG
  cartography shows the exact 36 km
  world boundary and the authored x=15,943.6523 m separator, explicitly shades
  the corridor containing all 12 preserved Haven placements, and overlays the
  current ship and player positions without permanent marker labels. The
  operator can inspect live authoritative XYZ values on selection. The browser
  refreshes on a four-second cadence over the game server's three-second stats
  snapshots. Missing player position is shown as
  unknown, never placed at a fabricated origin. The panel is now labelled for
  its provenance, because a 266-island map beside a 3-island table reads as a
  broken panel when it is only a configured one: the map is titled preserved
  release-world map and signed as static embedded MapFile evidence whose
  geometry, tier cells, walls and boundary are not read from the running game
  server, while the ship, player and simulated-island-domain marks are signed
  as the live overlay with their refresh cadence stated there. The Terrain
  checkout island inventory is signed as the authoritative live set of islands
  the game server is actually simulating. Both panels print one shared
  reconciliation line, "N islands on the preserved release map / M currently
  simulated", where N comes from the embedded projection and M is read from the
  live terrain section; if the stats file is missing, stale or predates terrain
  telemetry the line states that condition instead of a count, so a degraded
  snapshot can never render as a real zero. Which individual map glyphs are
  simulated is NOT claimed: the live ring already drawn at each simulated
  island domain's reported position is legended as the live mark, and no static
  glyph is restyled, because no exact island-id-to-MapFile-record mapping is
  available on the page and name matching would be a guess. Validation passed 2,586/2,586
  Multiplayer tests and 183/183 admin/login tests;
  game, login and client Release builds had zero errors. The coordinated
  rollback copy is
  `/opt/wareborn/backups/pre-069a372-20260817T093253Z/{game,login,patch}`; the
  immediate pre-fix game rollback is
  `/opt/wareborn/backups/pre-b52f504-20260817T100136Z`; the immediate
  pre-`1aa9fe4` rollback is
  `/opt/wareborn/backups/pre-1aa9fe4-20260817T104346Z`; the immediate
  pre-`7fab2e2` rollback is
  `/opt/wareborn/backups/pre-7fab2e2-20260817T111125Z`; the coordinated
  pre-`7c99dac` game/patch rollback is
  `/opt/wareborn/backups/pre-7c99dac-20260817T113001Z`; the coordinated
  pre-live-map game/login rollback is
  `/opt/wareborn/backups/pre-3d64a7f-20260817T115238Z`; the immediate
  pre-SVG-map login rollback is
  `/opt/wareborn/backups/login-before-svg-map-20260817T130929Z`; the immediate
  pre-zone-signage login rollback is
  `/opt/wareborn/backups/login-before-zone-signage-20260817T132536Z`.
- **Public client manifest:** `2026.08.17-7`, build label
  `solid hazed island shells + crews (958c8e1)`. Managed client DLL SHA-256
  `09c14ad11a43e8126e1a2e2802fd5ef60dcc245c74d97fd101ceec430fc1742e`. Exactly one
  payload changed against `-6`; all 54 public payloads matched their published
  hashes. It carries the shell fixes: side walls and keel now wind OUTWARD (both
  were inverted, so the flanks and the underside were backface-culled and the
  shell read as blown glass), hard rim edges, and self-hazing by distance -
  necessary because the client reports `scene fog=False`, so Unity's built-in fog
  is not what makes retail's distance haze and a fog-aware shader bought nothing.
  NOTE: distant shells are currently DISABLED in production
  (`WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED=0`), because the 4 km terrain radius
  makes real islands the near-field answer, so these fixes ship but do not render.
  The previous manifest was `2026.08.17-6`, build label
  `island shell underside fix + logout position (c31e8be)`. It carries the keel
  winding fix: the compact shell's keel fan copied the top cap's winding, but it
  faces DOWN and so must wind the opposite way. The whole underside of every
  island was therefore backface-culled, the silhouette read as a shape with a
  piece cut out, and what remained looked like a flat plate. `-5` shipped that
  defect, so `-6` repairs a live regression rather than iterating on taste.
  Managed client DLL SHA-256
  `58de4eb816f7e24ee04cf3f83163bb80f0c3cf539efeff4235d9ee3208c2851d`. Exactly
  one payload changed against `-5`, the other 53 were byte-identical, and all 54
  public payloads matched their published hashes. NOT visually accepted; the
  taper profile and the two colours remain judgement calls.
  Manifest `2026.08.17-5` shipped the first shell shape/outline/material pass.
  Before it, `2026.08.17-4` carried build label
  `retail-LOD island shell preference (ccfb138)`. Cut from a freshly assembled
  pack (3 plugin + 51 gameroot = 54 files). Before shipping, every file was
  diffed against the live `-3` manifest: exactly ONE payload changed,
  `BepInEx/plugins/WorldsAdriftReborn/WorldsAdriftReborn.dll`, and the other 53
  were byte-identical, so this release carries only the rebuilt plugin. All 54
  public payloads were then re-fetched over HTTPS and matched their published
  hashes. `tools/patcher/build-manifest.sh` had a dead `DEFAULT_PACK` pointing
  at a deleted session scratchpad, which failed with a confusing "no plugin/
  under pack"; `--pack` is now required and the error prints the assembly
  recipe.
  The previous manifest was `2026.08.17-3`, build label
  `activate distant island shell waiter (bede97e)`. The first `-2` live pass
  proved both bundles cached but exposed an activation-order defect: Unity
  rejected the material-ready coroutine while its shell object was inactive,
  leaving all renderers hidden. `bede97e` activates the already-inert object
  before arming the waiter. It ships the marked,
  correlated asset-loaded acknowledgement required for safe optional-terrain
  unload/re-entry plus the non-physical low-LOD shell lifecycle. All 54 public
  payloads matched their published hashes.
- **Managed client DLL SHA-256:**
  `1c6f278ee886ad09805fa30807b1f510fde5fa0ba4b8cf97b8bf15e572c92863`
  (the public payload was re-downloaded and matched this exactly).
- **Windows CoreSDK DLL SHA-256:**
  `26b5ce1568abec2ca06d488e3aadaaf725c92a89e1e2482571e27ad31986c354`.
- **Server state:** active on native Linux, UDP 7779. Boot restored 4/4 placed
  deployables, 5/7 ships (two tombstones), 16/16 mounted parts and 3/3 loose
  parts. Stats report schema 6, build `3d64a7f`, host mode
  `local-single-process`, and terrain mode
  `on`. Staged/live game, login and production-built Linux CoreSDK hashes match;
  the Linux shim is SHA-256
  `0121219a138a07f345103f83cc5647f993ecb0282a0172c7bf19a54b78a252f7`.
  The deployed game managed DLL is SHA-256
  `f2c9c288c2266f08448e2f50664abb1e693b11ac3cc0585e2c42914de9602973`;
  the login/admin managed DLL is
  `a4277318cd097e44142a344adc675be02b9381b766b9036ddd779c5e0523f80c`.

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

### Complete release-world rollout (local, off by default)

Steps 1–5 of the release-world expansion are implemented locally behind
`WAREBORN_RELEASE_WORLD_DISTRICTS`. `all` selects all 254 ordinary MapFile
islands; an exact comma-separated cell list such as `B3,C6` enables a staged
district rollout. Startup refuses the rollout unless both
`WAREBORN_INTEREST_RADIUS_M` and `WAREBORN_TERRAIN_INTEREST_ENABLED=1` are also
valid, preventing an accidental all-world connect plan. Haven remains its one
active #5 placement; the other eleven preserved Haven positions remain map
evidence only.

The embedded generated catalogue contains 254 unique definitions, all 254
collision AABBs, one 16-point compact shell outline per island, the exact survey
profiles, 1,930 surface-derived deposits, and all 1,233 surveyed databanks.
The full registry is 255 terrains grouped into the exact 20 MapFile cells plus
Haven. The two null Tier-4 cells retain stable `unassigned-t4-*` internal ids;
no E1/E2 labels are invented. Holy Ruins deliberately retains both conflicting
facts: Tier 3 in the final community survey and location in Bossa's Tier-2 A4
cell. The source generator is
`tools/world-import/generate-release-runtime-catalog.py`.

The v2 shell's shape and data were corrected on 2026-08-17; the fixes are local
and **not visually accepted**.

- **It was drawn in the wrong place.** The mesh spanned `MinY` to
  `MinY + 45%` of the envelope - the BOTTOM 45% - so it showed the island's
  underside and omitted the plateau its own outline was sampled from. The
  silhouette sat a median **121 m** (up to **411 m**) below the terrain it stood
  in for, so an island read as hanging too low and then jumped when the physical
  terrain replaced it. The mesh is now a plateau cap at the measured `MaxY` with
  the underside tapering to a keel at the measured `MinY`. Only the taper profile
  (a ring at 45% height inset to 72%) is invented; rim radius, rim height and
  keel depth are all measured.
- **12 islands were pinched into spikes.** An empty angular bin in
  `shell()` emitted a UNIT vector, placing a 1 m radius point between neighbours
  hundreds of metres out - 83 points, the worst 1 m against a real 599 m extent.
  The first repair reused a neighbour's RADIUS at the missing angle and overshot
  the other way, putting 66 points outside their own island, the worst by 383 m.
  The shipped fix interpolates the POSITION along the chord between the two
  nearest measured samples, which is inside their convex hull by construction and
  is the same rule the deposit/databank filler already used. The regenerated
  catalogue has zero degenerate points and zero points outside their island, with
  the 254/1233 counts unchanged (deposits were 354 at the time; see below).
- **It read as a flat cut-out.** `Unlit/Color` ignores scene lighting AND
  distance fog, so the shell was pasted over the sky exactly where atmosphere
  should dissolve it. It is now a lit, fog-aware material in two submeshes so the
  plateau and the rock beneath it read differently, which at this distance is
  most of the shape cue. The two colours are a judgement call and are the part
  most likely to need adjusting on sight.

A per-angle top height would follow the real skyline instead of a flat rim, but
that needs a v3 marker carrying a height per outline point; it is not done.

Under the full rollout distant visuals use the v2 procedural shell: the server
sends the compact outline only for islands within 9 km, and the client builds a
non-physical mesh without loading the terrain bundle. At the 1.2 km physical
radius the existing correlated asset checkout replaces that shell with full
terrain, collision and nearby resources. The v2 shell is a **scalability
fallback, not a preference**: it exists because 254 island-bundle prefetches per
peer are not affordable. `IslandShellFidelityPolicy` makes that an explicit
decision keyed on whether the release-world rollout is active, so the bounded
configuration keeps requesting the v1 retail-LOD shell even though its islands
are also records in the embedded 254-island catalogue. Catalogue membership
alone never selects v2, and v2 can never be selected for an island that has no
outline to encode. A near-band fidelity upgrade (replacing a placed v2 shell
with a v1 mesh as a viewer approaches) is deferred: the client dedups shells by
terrain entity id and both entry points re-acknowledge instead of rebuilding, so
an upgrade needs a client teardown path that does not exist yet. The full
rollout is **not deployed and not visually accepted**.
Trees, revival chambers, turrets and weather-wall gameplay are not spawned by
this milestone; their survey facts are retained for later systems.

### Tier 1 (Wilderness): the complete A2/A3/B2/B3 region (local, off by default)

Resources on release-world islands ALREADY WORK. The two statements that looked
contradictory describe two different flags:
`WAREBORN_FIRST_REGION_TERRAIN_COUNT` registers terrain roots only (that is the
"no new dynamic resource population" sentence above), while
`WAREBORN_RELEASE_WORLD_DISTRICTS` registers terrain PLUS every catalogued
deposit and databank for the selected cells (that is the 1930/1233 assertion in
`ReleaseWorldCatalogTests`). Both are true.

Tier 1 is exactly map cells A2, A3, B2 and B3: 46 islands, all tier 1, and those
four cells contain nothing else. `WAREBORN_RELEASE_WORLD_DISTRICTS=tier1` (or
`t1`/`wilderness`) now names that from the catalogue's own `cellTier` so it
cannot drift; the explicit cell list still works and the selectors compose.

Its content is **328 deposits, 215 databanks, 328 atlas shards**, 12 islands
with surveyed revival chambers and 14 with surveyed tree species. Every one of
the 46 islands now has metal.

It was 46 deposits on FOUR islands until 2026-08-18. The catalogue applied its
density rule only where the Cardinal survey recorded a PvE metal table, and it
recorded one for just 38 of the 254 islands. That turned out to be a coverage gap
in a player-submitted survey, not a barren world: the survey visited all 254
islands (every one has a surveyor name and an exact databank count), its own map
UI renders an empty list as "No metals data", and it had five weeks between
Update 31's new map and shutdown. 216 islands are now populated from a labelled
three-rung provenance ladder - 38 `survey-pve`, 23 `survey-pvp` (no PvE table but
the same island WAS read on the PvP shard), 193 `inferred-tier`. The inference is
NOT Bossa data, it is stamped as such in the catalogue and in
`IslandSurveyProfile.MetalSource`, and the raw survey arrays are preserved
verbatim beside it. Full evidence, the derivation and the load numbers:
`docs/research/findings-island-resource-population.md`.

The one real gap, now closed: release-world deposits registered no atlas shard,
so a tier-1 deposit yielded metal but never the shard that is the mining loop's
payoff (Haven and Trades deposits both had one). Each release deposit now
registers its shard immediately after itself, gated by the existing
`WAREBORN_SPAWN_ATLAS` and `WAREBORN_ATLAS_RATE`, with the rate applied to each
island's own deposit index so every island with metal reliably has at least one.

Headless boot at `tier1` against a throwaway data directory: **terrains=47,
regions=5, 481 registrations classified (global=1, region=480, unclassified=0),
`[domain-host] islands=47 ships=0 owned=480 globals=1 unowned=0 duplicates=0`,
433 boot resource activations (215+46+46 release + 81 trees + 24 fuel pods + 21
metal nodes), spawn plan 964 steps, zero warnings/errors.** The 964-step plan is
process-wide, not per-peer: the nearest tier-1 island is 9.33 km from the Haven
spawn and production loads terrain at 4 km, so a fresh Haven connect streams zero
tier-1 terrains and zero tier-1 resources. Connect (45 m), live resource (120 m)
and terrain (4000/4400 m) radii stay separate; nothing here widens resource
interest. At 4 km a median of 9 terrains are physically loaded (min 5, max 12).

Trees and revival chambers are explicitly DEFERRED, each with its cost, in
`docs/research/findings-tier-one-world.md`. Trees are blocked on there being no
evidenced density (deposits use 0.05/cell, databanks have an exact surveyed
count; trees have a species list and nothing else), revival chambers on there
being no server system of any kind. NOTE: 0.05/cell was previously called "the
recovered retail figure" - it is not. The decompile has the field names
(`metalDepositDensity`, `minMetalRockDeposits`) and confirms the island reports
its LOD0 mesh count to the spawner, but the formula lived in the lost Scala
worker. The SHAPE is retail; the value 0.05 is ours.
Distant island shells still need `WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED=1`
separately and remain visually unaccepted. Nothing here is deployed or proved
with a real client.

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
2,570 Multiplayer tests and the Release server build pass. It is deployed as
`7fab2e2`; post-restart verification reported matching hashes, the count-one B3
topology, 255 owned entities, zero ownership issues and zero terrain
warnings/errors. No client patch changed.

The player then departed Trades toward Haven. Resource checkout drained all 15
Trades entities to zero before terrain teardown, `remove-ok` succeeded, and the
island reached `ABSENT` with no pending action, warning, error or legacy
retention. This completes the one-client ship-proximity add, on-foot resource and
databank interaction, departure drain and terrain-unload lifecycle. It does not
accept visual presentation: the approach showed a brief magenta material state
before the normal island shader appeared, and the island still visibly pops in
and disappears because there is no persistent distant visual shell yet.

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
