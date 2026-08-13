# Worlds Adrift Reborn — Wareborn

An experimental, community-run revival of **Worlds Adrift**. This fork replaces
the retired online services with a custom login service, ENet game server,
SpatialOS compatibility layer, and a BepInEx client mod.

> This is an unofficial fan project. It is not affiliated with, endorsed by, or
> supported by Bossa Studios. No Worlds Adrift game assets are distributed here.

## Project lineage and credits

Wareborn is a continuation of the community
[WAReborn/WorldsAdriftReborn](https://github.com/WAReborn/WorldsAdriftReborn)
project and retains its architecture: the BepInEx/Harmony client mod, C++
SpatialOS replacement, HTTP login server, and ENet game server. That foundation
was created by the original WAReborn contributors, including the earlier work
published by
[sp00ktober/WorldsAdriftReborn](https://github.com/sp00ktober/WorldsAdriftReborn).
Their reverse-engineering work made this continuation possible.

Worlds Adrift itself, its client, art, audio, and game data remain the work and
property of Bossa Studios and their contributors. This repository contains only
original compatibility, server, tooling, and research code.

## Current state

The public Wareborn test server is playable, but this is still an experimental
revival—not a complete recreation of the retail MMO.

Implemented and running as of **August 2026**:

- account registration, login, character creation, and per-player identity;
- persistent inventories, knowledge/progression, placed stations, built ships,
  loose parts, and mounted ship components;
- a whole-island Haven starter-biome population: birch trees, iron deposits,
  fuel pods, atlas shards, and databanks placed on extracted terrain surfaces;
- per-player resource-interest streaming, so distant island resources are added
  and removed as the player travels instead of all loading at login;
- gathering, salvage, fuel and atlas-shard acquisition, tree respawn, crafting,
  schematics, shipyard and assembly-station workflows;
- ship blueprint construction, deck generation, loose-part placement and
  mounting, station pickup, docking and undocking;
- controllable ships with persistent helm throttle, centred helm entry,
  functional sails, climb, pitch, roll, and yaw;
- loading barriers, spawn pacing, acknowledgement timeouts, prefab precaching,
  and client-side recovery paths for more reliable joins;
- a native Linux x64 game server. The client remains Windows x64.

The authoritative server is a reconstruction. Ship flight currently uses a
documented kinematic model rather than retail's unavailable weather/Rigidbody
simulation, and only the currently reconstructed world content is available.
Expect bugs and occasional resets while development continues.

## Join the public test server

### What you need

- Windows x64;
- a legitimate Steam account entitled to Worlds Adrift;
- the supported archived client build listed below;
- [BepInEx 5.x x64](https://github.com/BepInEx/BepInEx/releases);
- [WAPatch.exe from the latest Wareborn release](https://github.com/DoubtfulHermit/WorldsAdriftReborn/releases/latest/download/WAPatch.exe).

The patcher contains no game assets. It only installs and updates the Wareborn
mod, native compatibility DLL, and their runtime dependencies.

### 1. Install the supported Worlds Adrift client

The final Steam build is not usable because content was removed before the
official shutdown. Use
[DepotDownloader](https://github.com/SteamRE/DepotDownloader) with your own
Steam account to obtain the supported depot:

```text
DepotDownloader.exe -app 322780 -depot 322783 -manifest 4624240741051053915 -username <your Steam username> -password <your Steam password>
```

Copy the downloaded depot into a dedicated Worlds Adrift game folder. Do not
post or redistribute those files.

### 2. Install BepInEx

Extract **BepInEx 5.x x64** into the game folder. The folder containing
`UnityClient@Windows.exe` should now also contain a `BepInEx` directory.

Create `steam_appid.txt` in the same game folder with exactly this content:

```text
322780
```

### 3. Install the Wareborn client patch

1. Close Worlds Adrift completely.
2. Download and run
   [WAPatch.exe](https://github.com/DoubtfulHermit/WorldsAdriftReborn/releases/latest/download/WAPatch.exe).
3. Browse to the folder containing `UnityClient@Windows.exe`.
4. Select **Check for updates**, then **Patch**.
5. Wait for the verified install to finish before launching the game.

The patcher downloads the current signed-by-hash manifest from
`https://wareborn.ratlabs.cc/patch/manifest.json`, verifies every file with
SHA-256, backs up an existing file before its first replacement, and performs
atomic writes. Re-run it whenever a new client version is announced. If the
game was open during an update, close it fully and patch again before joining.

Windows may show an unknown-publisher warning because this community binary is
not code-signed. Verify the SHA-256 shown on the GitHub release before running
it if you want an independent integrity check.

### 4. Point the client at Wareborn

Launch the game once after patching, then close it. This creates:

```text
BepInEx\config\WorldsAdriftReborn.cfg
```

Open that file and set these values in their existing sections:

```ini
[GameServer]
GameServer_Host = 62.171.161.19
GameServer_Port = 7779

[REST]
REST_ServerUrl = http://62.171.161.19:8085
REST_ServerDeploymentUrl = http://62.171.161.19:8085/deploymentStatus
```

Use a unique password for this test service. The browser signup is HTTPS, but
the legacy game client's authentication request currently uses plain HTTP.

### 5. Create an account and enter the world

1. Register at <https://wareborn.ratlabs.cc/signup>.
2. Launch `UnityClient@Windows.exe`.
3. Enter the registered username in the game's **Email Address** field and the
   same passphrase in **Password**.
4. Create or select a character and join.

The browser login page at <https://wareborn.ratlabs.cc/login> also provides the
current patcher download after sign-in.

### Useful controls

- **E** — normal interaction; man the helm or furl/unfurl a sail.
- **X** (hold while looking at it) — pack a placed Shipyard or Assembly Station
  back into your inventory.
- **F10** — manual recovery if your character falls through the world.

If joining fails, keep `BepInEx/LogOutput.log` and `CoreSdk_OutputLog.txt` from
the game folder. Include both when reporting the problem, along with the patch
version displayed by WAPatch.

## Run your own server

The repository contains four main pieces:

| Project | Purpose |
|---|---|
| `WorldsAdriftReborn` | BepInEx/Harmony Windows client mod |
| `WorldsAdriftRebornCoreSdk` | replacement native SpatialOS/ENet client SDK |
| `WorldsAdriftServer` | account, login, character, patch, and admin HTTP service |
| `WorldsAdriftRebornGameServer` | authoritative multiplayer game server |

Clone with submodules:

```bash
git clone --recurse-submodules https://github.com/DoubtfulHermit/WorldsAdriftReborn.git
```

The projects require the compatible game assemblies and local paths described
by `DevEnv.targets`. The managed projects target modern .NET SDKs; the client
mod additionally requires the Windows game installation and native client SDK
toolchain. Start with:

- [server deployment and configuration](docs/hosting.md);
- [patcher build and release process](tools/patcher/README.md);
- [research index](docs/research/README.md).

Common validation commands:

```bash
dotnet test WorldsAdriftRebornGameServer.Multiplayer.Tests -c Release
dotnet test WorldsAdriftServer.Tests -c Release
dotnet build WorldsAdriftRebornGameServer -c Release
```

The production game server now runs as a self-contained Linux x64 executable
with `libCoreSdkDll.so`; the systemd unit template is
[`deploy/wareborn-game-native.service`](deploy/wareborn-game-native.service).
The player-facing client and patcher remain Windows x64.

## Contributing and support

Bug reports with reproducible steps and logs are especially useful. Please keep
changes evidence-driven: much of the protocol and game behavior must be
reconstructed from shipped client code and live packet traces.

See the upstream
[contribution guide](https://github.com/WAReborn/WorldsAdriftReborn/blob/main/CONTRIBUTING.md)
and the original WAReborn community Discord: <https://discord.gg/pSrfna7NDx>.

## Legal

This project does not provide the Worlds Adrift client or any proprietary game
assets. You must obtain the game through an account that is legitimately
entitled to it. Names and trademarks belong to their respective owners.
