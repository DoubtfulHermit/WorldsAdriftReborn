# Hosting the server (VPS)

The server runs on the VPS at **62.171.161.19**, installed under `/opt/wareborn`,
as two systemd units that start at boot.

## Ports

| Service | Port | Why not the default |
|---|---|---|
| Game (ENet) | **UDP 7779** | 7777 is permanently held by `elementbrawl` (godot), 7778 by the Dragonwilds `frps` tunnel |
| Login / REST | **TCP 8085** | 8080 is held by a docker-proxy |

Both are opened in `ufw` (the INPUT policy is DROP, so rules are required).

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
├── WorldsAdriftServer/            login/REST server
├── WorldsAdriftRebornGameServer/  ENet game server (+ CoreSdkDll + game DLLs)
├── wineprefix/                    Wine prefix, portable .NET 6 at C:\dotnet6
├── run-login.sh                   wrapper (sets WINEPREFIX + port, execs wine)
└── run-game.sh
```

Wine 9.0 from the Ubuntu repos; no X needed for console apps.

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

- **Both servers need a pty.** The login server ends in `Console.ReadKey()` and
  dies instantly on redirected stdin. The units run
  `sleep infinity | script -qfc <wrapper> /dev/null` to give it a tty whose stdin
  never delivers input.
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

Server code (no client change needed):

```bash
cd ~/Games/WAReborn-src
dotnet build WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.csproj \
    -c Release -p:WorldsAdriftGameDir="$HOME/Games/WorldsAdrift"
scp WorldsAdriftRebornGameServer/bin/Release/net6.0/WorldsAdriftRebornGameServer*.dll \
    root@62.171.161.19:/opt/wareborn/WorldsAdriftRebornGameServer/
ssh root@62.171.161.19 systemctl restart wareborn-game
```

**Deploy the build you just made.** A whole debugging round was lost to uploading
a stale binary that still hardcoded 7777, because the fresh build was never copied
into the deploy folder first.

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
