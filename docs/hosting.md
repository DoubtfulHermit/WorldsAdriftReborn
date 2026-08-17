# Hosting Wareborn

**Current as of 2026-08-14.** Production runs native Linux x64 services on the
VPS. The old Windows/Wine game deployment is rollback-only; its former mixed
instructions are archived under `docs/archive/2026-08/`.

## Production map

| Service | Endpoint | Live directory | Unit |
| --- | --- | --- | --- |
| Game/ENet | UDP `62.171.161.19:7779` | `/opt/wareborn/WorldsAdriftRebornGameServer-native` | `wareborn-game` |
| Login/REST | TCP `62.171.161.19:8085` | `/opt/wareborn/WorldsAdriftServer-linux` | `wareborn-login` |
| Public web/patch | `https://wareborn.ratlabs.cc` | Caddy proxies login and serves patch files | Avatar-stack Caddy |
| PostgreSQL | loopback `127.0.0.1:5434` | Docker volume `wareborn-pgdata` | `wareborn-postgres` |

The game server is self-contained and loads `libCoreSdkDll.so` beside its
executable. The login server is also self-contained. PostgreSQL is deliberately
not exposed through the firewall.

The public connection settings are written by WAPatch. Players joining the
public server should not manually edit `BepInEx/config/WorldsAdriftReborn.cfg`.

## Read-only production checks

```bash
ssh root@62.171.161.19 \
  'systemctl is-active wareborn-game wareborn-login'
ssh root@62.171.161.19 \
  "journalctl -u wareborn-game -n 100 --no-pager -o cat"
curl -fsS https://wareborn.ratlabs.cc/patch/manifest.json \
  | jq '{version,build}'
```

The game server has no HTTP health endpoint. Confirm UDP `7779` locally on the
VPS with `ss -ulnp`; the login server exposes `/deploymentStatus` on port 8085.

## Validation before deployment

From the selected integration worktree:

```bash
dotnet test WorldsAdriftRebornGameServer.Multiplayer.Tests -c Release
dotnet test WorldsAdriftServer.Tests -c Release
dotnet build WorldsAdriftRebornGameServer -c Release
dotnet build WorldsAdriftReborn -c Release
git diff --check
```

Do not run the Multiplayer test build and game-server build concurrently because
they share output files. Never restart while players are connected unless they
explicitly accept a session-ending restart.

## Native game-server deployment

Publish into a fresh staging directory:

```bash
cd /home/ttanurhan/Games/wareborn-loading
game_stage=$(mktemp -d /tmp/wareborn-game-native.XXXXXX)
dotnet publish WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.csproj \
  -c Release -r linux-x64 --self-contained true -o "$game_stage"
```

If `WorldsAdriftRebornCoreSdk` changed, do **not** copy a development-host `.so`.
The development machine links newer protobuf/Abseil libraries than Ubuntu 24.04
production. Copy the current SDK sources and
`tools/relaybot/build-coresdk-native.sh` to an isolated source tree on the VPS,
build there, then verify:

```bash
ldd libCoreSdkDll.so
nm -D --defined-only libCoreSdkDll.so | grep ENet_EXP_PeerChannelCount
```

The build script also checks the complete game-server export surface. Put the
verified `libCoreSdkDll.so` in the publish stage beside the executable.

After backing up the live deployment/state and confirming all players are out:

```bash
rsync -a "$game_stage"/ \
  root@62.171.161.19:/opt/wareborn/WorldsAdriftRebornGameServer-native/
ssh root@62.171.161.19 'systemctl restart wareborn-game'
ssh root@62.171.161.19 \
  "systemctl is-active wareborn-game && journalctl -u wareborn-game -n 100 --no-pager -o cat"
```

Never use `--delete` against the live directory: persistent `data/` is not
produced by publish. Verify restore counts, resource-interest configuration,
island registration and the first real connection before declaring success.

## Native login-server deployment

```bash
login_stage=$(mktemp -d /tmp/wareborn-login-native.XXXXXX)
dotnet publish WorldsAdriftServer/WorldsAdriftServer.csproj \
  -c Release -r linux-x64 --self-contained true -o "$login_stage"
rsync -a "$login_stage"/ \
  root@62.171.161.19:/opt/wareborn/WorldsAdriftServer-linux/
ssh root@62.171.161.19 'systemctl restart wareborn-login'
```

The same no-`--delete` rule applies because live state/configuration is not all
produced by publish. The database connection string belongs in the root-only
`/etc/wareborn/login.env`, never documentation, chat output or a repository.

## Client release boundary

Server-only changes do not require a patch release. A managed client change or a
native export/path used by the Windows client requires a new manifest version.
Follow [`../tools/patcher/README.md`](../tools/patcher/README.md), publish the
generated patch directory, then fetch the public manifest and compare every
payload SHA-256.

The current public patcher writes the public REST/game endpoints itself. Do not
ship personal credentials, local paths, or private config values.

## Runtime configuration

Important game-server variables include:

| Variable | Purpose |
| --- | --- |
| `WAREBORN_GAME_PORT` | ENet listen port; production uses `7779` |
| `WAREBORN_DATA_DIR` | persistent world-state root |
| `WAREBORN_INTEREST_RADIUS_M` | live resource load radius |
| `WAREBORN_INTEREST_INITIAL_RADIUS_M` | bounded connect-time resource bubble; live default `45` m |
| `WAREBORN_INTEREST_SETTLE_MS` | delay before continuous additions; live default `5000` ms |
| `WAREBORN_INTEREST_UNLOAD_RADIUS_M` | unload hysteresis radius |
| `WAREBORN_SHIP_INTEREST_RADIUS_M` | built-ship domain load radius; default `800` m and also used at connect |
| `WAREBORN_SHIP_INTEREST_UNLOAD_RADIUS_M` | built-ship unload hysteresis radius; default `1000` m |
| `WAREBORN_SPAWN_ACK_TIMEOUT_MS` | bounded per-step spawn acknowledgement timeout |
| `WAREBORN_FIRST_REGION_TERRAIN_COUNT` | bounded 0..12 tier-1 B3 terrain prefix; test only one island at a time |
| `WAREBORN_TERRAIN_INTEREST_ENABLED` | enables per-peer optional-terrain checkout when resource interest is also enabled |
| `WAREBORN_TERRAIN_LOAD_RADIUS_M` | optional-terrain load radius; default `1200` m |
| `WAREBORN_TERRAIN_UNLOAD_RADIUS_M` | optional-terrain unload hysteresis; default `1600` m |
| `WAREBORN_TERRAIN_ASSET_ACK_TIMEOUT_MS` | exact cold-bundle acknowledgement timeout; default `30000` ms |
| `WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED` | opt-in client-only retail low-LOD island silhouettes; requires the matching managed client patch |
| `WAREBORN_RELEASE_WORLD_DISTRICTS` | local-only rollout: `all` or exact MapFile cells such as `B3,C6`; ignored unless resource and terrain interest are both enabled |
| `WAREBORN_DEPOSIT_VARIANT` | optional global diagnostic override for the normal stable 01/02/03 deposit-shape cycle |
| `WAREBORN_WORLD_ADMIN_FILE` | authenticated admin command bridge; default `/tmp/wareborn-world-admin` |
| `WAREBORN_WORLD_ADMIN_RESULT_FILE` | atomic game-completion receipt consumed by `/admin`; default `/tmp/wareborn-world-admin.result` |

Resources remain authoritative in the world registry. The loading barrier gets
only the initial nearby resource bubble; after client activation, movement-driven
interest expands through the paced live radius. Terrain, players, global biome
data and player-made structures are governed by their explicit policies. Built
ships use whole-domain interest: a joining peer receives only ships within the
ship load radius, and later checkout adds the hull before its deck and mounted
members.

The authenticated `/admin` console has four sections: World, Simulation,
Operations and System. Mutating operations are a strict allowlist, never a shell
or arbitrary-coordinate interface. It can reset all harvest nodes, recall an
exact uncrewed hull beside an exact connected player, and permanently delete an
exact uncrewed hull. Delete requires both typed `DELETE` confirmation and a
browser confirmation. The login server revalidates fresh player/domain IDs and
writes a one-shot command; the game server consumes it on its authoritative poll
loop and atomically writes a separate completion receipt. Queue acceptance and
gameplay completion are deliberately shown as different events.

## Operational cautions

- A game-server restart remains session-ending; clients do not safely resume
  authority after peer state is lost.
- Channel-5 `RemoveEntity` is capability-gated by negotiated ENet channel count.
  Old clients retain visited entities rather than receiving an invalid packet.
- Build the native shim against production's ABI and check `ldd` every time.
- Keep secrets in root-only environment files. Do not print complete systemd
  environments into logs or chat.
- Preserve the old Wine directory only as rollback evidence. Do not deploy new
  work into it.
