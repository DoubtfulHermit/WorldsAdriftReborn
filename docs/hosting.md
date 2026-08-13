# Hosting the server (VPS)

The server runs on the VPS at **62.171.161.19**, installed under `/opt/wareborn`,
as two systemd units that start at boot.

## Ports

| Service | Port | Why not the default |
|---|---|---|
| Game (ENet) | **UDP 7779** | 7777 is permanently held by `elementbrawl` (godot), 7778 by the Dragonwilds `frps` tunnel |
| Login / REST | **TCP 8085** | 8080 is held by a docker-proxy |
| Postgres (accounts) | **127.0.0.1:5434** | 5432 and 5433 are held by the Avatar stack. Loopback only - never opened in `ufw` |

The first two are opened in `ufw` (the INPUT policy is DROP, so rules are
required). The database deliberately is not: nothing outside the box needs it.

Ports are configurable rather than hardcoded:

- Server: environment variables `WAREBORN_GAME_PORT` and `WAREBORN_REST_PORT`,
  set in the wrapper scripts.
- Client: `GameServer_Port` in `BepInEx/config/WorldsAdriftReborn.cfg`.
  **The client port cannot be passed by environment variable** - the native SDK's
  C runtime caches the environment when the DLL loads, so a value set later by
  .NET is invisible to `getenv()`. The mod calls the DLL's exported
  `WAR_SetGamePort(int)` instead. This cost a debugging session: the client kept
  connecting to 7777 while the config said 7779.

## Layout on the VPS

```
/opt/wareborn/
├── WorldsAdriftServer-linux/      login/REST server - NATIVE, self-contained
├── WorldsAdriftRebornGameServer-native/  game server - NATIVE, self-contained
├── WorldsAdriftRebornGameServer/  old Windows/Wine game deploy, rollback only
└── wineprefix/                    retained only for emergency rollback

/etc/wareborn/login.env            WAREBORN_DB - root-only, chmod 600
```

Both services run directly on Linux. The game server maps the legacy Worker SDK's
`msvcrt.dll!memcpy` import to glibc and loads the Linux compatibility shim
`libCoreSdkDll.so` beside the self-contained executable. The previous Wine unit
is retained as an exact rollback, but is not part of the live path.

## Accounts and the database

Accounts, sessions and character rosters live in Postgres 16 in its own docker
container, `wareborn-postgres`, published on loopback port 5434 with its data in
the `wareborn-pgdata` volume. It is deliberately **not** the Avatar stack's
`avatar-postgres-1`: sharing it would mean a restart of that stack takes WAReborn
logins down with it.

The connection string is the `WAREBORN_DB` environment variable, read from
`/etc/wareborn/login.env` by the unit so the password is not in a
world-readable file. There is no default with a password in it — the code's
fallback is passwordless loopback, because a shipped default credential is
everybody's credential.

The schema applies itself at startup and is a no-op when already current.

```bash
# what is in there
docker exec -e PGPASSWORD=<pw> wareborn-postgres \
    psql -U wareborn -d wareborn -c 'SELECT account_id, username FROM accounts;'
```

Players sign up at **https://wareborn.ratlabs.cc/signup** and then type the same
username and password into the login form on the game's own landing screen.
There is no Steam account involved at any point.

That hostname is served by the **Avatar stack's Caddy** (`/root/Avatar/Caddyfile`,
container `caddy`, host networking), which terminates TLS with a Let's Encrypt
certificate and proxies to `127.0.0.1:8085`. The A record already existed. The
block exposes **only** `/signup` and `/register`, redirects `/` to `/signup`, and
404s everything else — the game's own API is deliberately not on this host.
A backup of the previous config sits at `Caddyfile.bak.wareborn`; reload with
`docker exec caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile`
after validating with `caddy validate` first.

⚠ **The game's own login is still cleartext.** Sign-up is now behind TLS, but
`/authenticate` is not: the client is configured for `http://62.171.161.19:8085`
and posts the password unencrypted. Repointing it means changing
`REST_ServerUrl` in every player's `WorldsAdriftReborn.cfg` **and** confirming
BestHTTP validates the chain under Wine — neither is tested. Until then, tell
players to use a password they use nowhere else.

`WAREBORN_LEGACY_ROSTER_OWNER=<username>` hands the pre-accounts shared roster
(`data/characters/roster.json`) to one named account the first time that account's
roster is loaded. Set it before that account's first login, or not at all on a
fresh deployment.

## Operating it

```bash
systemctl status  wareborn-login wareborn-game
systemctl restart wareborn-game
journalctl -u wareborn-game -f
```

Health check from anywhere:

```bash
curl http://62.171.161.19:8085/deploymentStatus     # login server
```

The game server has no HTTP health endpoint; check the port and the log:

```bash
ss -ulnp | grep 7779
journalctl -u wareborn-game -n 50 --no-pager -o cat
```

### Gotchas

- **Wine cannot do the crypto Postgres authentication needs.** Npgsql's
  SCRAM-SHA-256 handshake derives a key with PBKDF2, .NET routes that through
  Windows CNG, and Wine's `bcrypt.dll` answers with
  `WindowsCryptographicException: Unknown error` before the first query runs. The
  fix is not a weaker `md5` auth method — it is that the login server never
  needed Wine. It runs natively now and the problem does not exist. Keep that in
  mind before moving anything else that talks to the database into the prefix.
- **The game server needs a pty**, because it ends in `Console.ReadKey()`, which
  throws on a redirected stdin. Its unit runs
  `sleep infinity | script -qfc <wrapper> /dev/null` to give it a tty whose stdin
  never delivers input. The login server no longer needs this: it waits on
  SIGTERM when `Console.IsInputRedirected`, and only reads a key when a person is
  running it by hand.
- **Restarting the game server orphans connected clients — today.** They keep
  rendering the world and look fine to the player, but the server has forgotten
  them: they are invisible to everyone and never reconnect. Players must restart
  the client. **Plan a restart as a session-ending event until the fix below
  ships.**

  This is a limitation of our shim, not of the game. `docs/research/findings-robustness.md`
  establishes that the game already ships a complete, working reconnect UX — a
  RETRY/QUIT dialog that returns the player to character select and opens a
  fresh ENet connection - and that four defects in our own layer stop it firing:
  `Connection.cpp:44` passes `NULL` for both ENet callbacks so the DISCONNECT
  event is consumed and discarded; `IsConnected()` returns `peer != NULL` and
  `peer` is only cleared in the destructor, so it is true forever;
  `WorkerProtocol_Dispatcher_RegisterDisconnectCallback` is an empty TODO
  (`Exports.cpp:26-29`); and the mod patches out the game's 65 s watchdog.
  ENet itself detects the dead server without any application traffic — an idle
  peer is pinged reliably, so timeout detection is armed regardless.

  The estimate is ~40 lines of C++, **no server change and no new wire message**.
  Once it lands a restart becomes a **~30 s recoverable interruption** rather
  than a session-ending one. That is a projection from static analysis: nothing
  was executed, the riskiest step (`ENet_Deinitialize` → `ENet_Initialize` on a
  second connect under Wine) is untested, and each reconnect will leave a stale
  avatar behind until entity removal lands. Do not plan operations around it
  until it is measured.

  Two traps recorded there, for whoever does the work: do **not** un-patch the
  65 s watchdog (`HeartbeatVisualiser` is `[Require]`-gated on components the
  server never seeds and refreshes only on traffic never sent — the patch was
  correct), and do **not** use the reason string `"Disconnect was called by the
  user."`, which routes to a silent lobby return instead of the dialog.
- **Log lines wrap and carry terminal escapes** (a side effect of `script`). When
  grepping, strip them: `tr -d '\r' | sed 's/\x1b\[[0-9;?]*[a-zA-Z]//g'`.
- Wine writes `CoreSdk_OutputLog.txt` into the process working directory.

## Deploying an update

Deploy with `dotnet publish -r win-x64 --self-contained false` into a clean
directory, then `rsync` that whole directory. Do **not** use `dotnet build` plus a
filename glob - see below.

Game server (no client change needed):

```bash
cd ~/Games/WAReborn-src
rm -rf /tmp/wa-pub-game
dotnet publish WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.csproj \
    -c Release -r win-x64 --self-contained false \
    -p:WorldsAdriftGameDir="$HOME/Games/WorldsAdrift" \
    -o /tmp/wa-pub-game
rsync -a /tmp/wa-pub-game/ \
    root@62.171.161.19:/opt/wareborn/WorldsAdriftRebornGameServer/
ssh root@62.171.161.19 systemctl restart wareborn-game
```

Login server — **native linux-x64 and self-contained**, a different recipe from
the game server's:

```bash
rm -rf /tmp/wa-pub-login
dotnet publish WorldsAdriftServer/WorldsAdriftServer.csproj \
    -c Release -r linux-x64 --self-contained true \
    -o /tmp/wa-pub-login
rsync -a /tmp/wa-pub-login/ \
    root@62.171.161.19:/opt/wareborn/WorldsAdriftServer-linux/
ssh root@62.171.161.19 systemctl restart wareborn-login
```

Self-contained here (~71 MB) buys independence from whatever .NET the VPS
happens to have, which is currently none — the box has Wine's private .NET 6
inside the prefix and nothing on the host.

⚠ **Never add `--delete` to these rsyncs.** Both deploy directories hold files
`publish` does not produce: the login server's `data/` symlink points at the
legacy roster the migration reads, and the game server keeps 55
separately-built native SDK libraries (`CoreSdkDll.dll`, `libabsl_*.dll`,
`zlib1.dll`) placed there by `build-mingw.sh` / `deploy-coresdk.sh`. `--delete`
destroys both.

For the **game server** do not use `--self-contained true` or
`PublishSingleFile`. The prefix already provides the runtime at `C:\dotnet6`, and
the wrapper launches via `dotnet.exe <dll>`, which cannot unpack a single-file
bundle. That constraint is Wine's, so it does not apply to the login server.

**Deploy the build you just made.** A whole debugging round was lost to uploading
a stale binary that still hardcoded 7777, because the fresh build was never copied
into the deploy folder first. Publishing into a freshly emptied directory also
stops a deleted file from lingering on the server forever.

### Why publish, and not build + a flat glob

`dotnet build` places native NuGet assets under `runtimes/<rid>/native/`, never
flat, so a filename glob silently leaves them behind. With
`Microsoft.Data.Sqlite` referenced that produces the worst failure shape there
is: a process that starts, answers `/deploymentStatus`, and throws the moment
anyone first touches the database.

```
System.DllNotFoundException: Unable to load DLL 'e_sqlite3'
    or one of its dependencies: Module not found. (0x8007007E)
```

Measured on the VPS under the live prefix (wine 9.0, `C:\dotnet6` = .NET 6.0.36):

| layout on disk | result |
|---|---|
| `e_sqlite3.dll` flat beside the managed DLLs (`publish -r win-x64`) | **works** |
| `runtimes/win-x64/native/e_sqlite3.dll` (`publish`, no RID) | **works** - Wine's host reads `deps.json` and probes the RID path correctly |
| managed DLLs present, native absent (`build` + flat glob) | `DllNotFoundException 0x8007007E` |

So Wine is not the constraint - both real layouts work, and the RID-less publish
is a valid fallback if the whole directory is copied. `-r win-x64` is preferred
because it emits 12 files and 3 MB instead of 26 MB of `runtimes/` for 21
platforms, and everything it emits is flat, so no deploy step depends on
remembering a subdirectory. On a VPS at 84% disk that difference is worth having.

If `CoreSdkDll.dll` changed, it must go to **both** the server and every client -
it is the same binary on both sides.

## Distributing a client

Two artefacts, both built from `~/Games/WorldsAdrift` + the current mod build:

- **Client pack** (~4 MB) - mod only, plus `SETUP.bat`, which downloads the
  correct pinned game build from the player's own Steam account via
  DepotDownloader and installs the mod over it. It does **not** contain game
  assets.
- **Update pack** (~3 MB) - `UPDATE.bat`, the two mod DLLs and all runtime
  libraries, for someone who already installed.

**Ship every runtime DLL, not just `lib*.dll`.** `zlib1.dll` does not match that
pattern; without it `CoreSdkDll.dll` cannot load at all, and the symptom is
maddening - login works fine (pure C#) while the world never loads, because the
native networking layer is simply absent. Diagnose it by the absence of
`CoreSdk_OutputLog.txt`.

A `DIAG.bat` exists for players: it reports which port the mod was told to use,
which port the client actually dialled, the config, and whether the machine can
reach both servers.

## Resource placement knobs

The live Haven population is generated offline from its extracted collision
surface. The original server-worker resource sampler is not present in the
shipped player client, so waiting for a 1010/1011 response from a player can
never populate the island. The deterministic generator covers the entire
terrain using surface, spacing, exclusion, and starter-biome rules.

| Variable | Default | Effect |
|---|---|---|
| `WAREBORN_METAL_HANDSHAKE` | off in production | Keep disabled for player clients; the necessary retail server-worker visualizer is not shipped in them. |
| `WAREBORN_DEPOSIT_COUNT` | all | How many deterministic whole-island Haven deposits to spawn. The table contains 40 biome-profiled iron deposits across the full terrain; clamped to `[1, 40]`. |
| `WAREBORN_TREE_COUNT` | all | Total Haven trees, including the proven near-spawn birch. The full deterministic layout is 81 birches (one anchor + 80 whole-island seats); clamped to `[1, 81]`. |
| `WAREBORN_TREE_SPECIES` | ignored on Haven | Legacy experiment that cycled every recovered wood species on one island. Haven now has an explicit birch starter-biome profile, so this cannot turn it into a random assortment. |
| `WAREBORN_SPAWN_ATLAS` | on | `0` stops the fallback lodging atlas shards in its deposits. |
| `WAREBORN_INTEREST_RADIUS_M` | deployment-specific | Per-player resource load radius. Distant resources remain registered but are not sent to that client. |
| `WAREBORN_INTEREST_UNLOAD_RADIUS_M` | load radius + hysteresis | Larger unload radius that prevents churn at the boundary. |

Resource entities remain authoritative in the world registry. At connect, only
nearby resources join the loading barrier; subsequent player-position updates
reconcile additions and removals through a paced queue. Essential entities,
player-built structures, ships, and the world-global biome entity are never
distance-gated.
