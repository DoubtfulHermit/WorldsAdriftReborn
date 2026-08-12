# Running two-player Worlds Adrift locally

Machine layout (all under `~/Games/`):

| Path | What |
|---|---|
| `WorldsAdrift/` | game install #1 (pinned pre-shutdown depot build) |
| `WorldsAdrift-2/` | game install #2 (btrfs reflink copy — zero extra disk) |
| `WorldsAdrift.known-good/` | untouched snapshot of the working install |
| `WAReborn-servers/` | both servers + all launcher scripts |
| `WAReborn-src/` | this repo, branch `multiplayer` |
| `wa-prefix/` | Wine prefix for the servers (portable .NET 6 at `C:\dotnet6`) |
| `wa-proton/`, `wa-proton2/` | per-client Proton (GE-Proton10-34) prefixes |

## Start order

```
cd ~/Games/WAReborn-servers
./run-login.sh        # terminal 1 — HTTP login/characters, 0.0.0.0:8080
./run-gameserver.sh   # terminal 2 — ENet world server, UDP 7777
./run-client.sh       # client 1 (game copy 1, prefix 1)
./run-client2.sh      # client 2 (game copy 2, prefix 2)
```

In each client: RETRY past the "Failed to authenticate with Steam" dialog
(expected — Steam auth is stubbed), then JOIN GAME.

## Rules learned the hard way

- **Both servers need real terminals.** The login server ends in
  `Console.ReadKey()` and dies instantly on redirected stdin. Scripted runs
  use `sleep infinity | script -qfc './run-login.sh' /dev/null`.
- **Never launch the client via `proton run`** — it exits silently without
  starting the game. The scripts call GE-Proton's `wine` binary directly with
  `WINEDLLOVERRIDES="winhttp=n,b"` (that override is what lets BepInEx
  inject).
- **Two clients cannot share a game directory** (BepInEx per-dir state) or a
  prefix (the second one needs DXVK — clone the working prefix, don't wineboot
  a fresh one).
- **Port 8080** must be free; the odysseus docker stack owns it when running
  (`docker start odysseus-...` to bring that stack back when done).
- Logs: `<install>/BepInEx/LogOutput.log` per client (Unity log capture is
  enabled in `BepInEx.cfg`; keep it that way), server logs on their consoles.
- Stale processes: `pkill -9 -f '[U]nityClient@Windows.exe'`, game server by
  `pkill -9 -f '[W]orldsAdriftRebornGameServer.dll'`; if UDP 7777 stays bound
  afterwards a leaked wineserver socket holds it — `WINEPREFIX=~/Games/wa-prefix
  wineserver -k`.
