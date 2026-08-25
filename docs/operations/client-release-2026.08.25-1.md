# Staged client release `2026.08.25-1` - attitude spline (turn vibration)

Cut on `fix/client-rotation-interpolation`, **staged only**. Nothing has been
pushed to the VPS and nothing has been deployed. The publish commands are at the
bottom; run them when the in-game A/B has passed.

## What is in it

One changed file. Everything else is byte-identical to the live release.

| | |
|---|---|
| Live release before this | `2026.08.24-2` - "CLR2-compatible low-speed ship follower coherence" |
| Staged version | `2026.08.25-1` |
| Build label | `client attitude spline (turn vibration)` |
| Files in the manifest | 54 (3 plugin + 51 gameroot), unchanged count |
| Added / removed | none |
| **Changed** | `BepInEx/plugins/WorldsAdriftReborn/WorldsAdriftReborn.dll` |

```
WorldsAdriftReborn.dll
  live 2026.08.24-2   sha256 b0d682ccfdad5fd790600ebded74cc96fd55a0a98d3bb48d6de0a90ddc9179df   279552 bytes
  staged 2026.08.25-1 sha256 129e197c264a181fc7ed1d3d304003bea1588ef531d1702afe43d9fcb1107baf   287232 bytes
                      md5    a85dfc4be922b55844a7b1dd30dec621
```

`CoreSdkDll.dll` and all 51 gameroot DLLs come from the committed
`WorldsAdriftRebornCoreSdk/build-mingw/` tree and hash identically to the live
manifest, so the patcher will download exactly one file per player.

## How it was built

```bash
mono20=/opt/wine-cachyos/share/wine/mono/wine-mono-10.4.1/lib/mono/2.0-api
REL=/tmp/wareborn-client-release
rm -rf "$REL" WorldsAdriftReborn/obj WorldsAdriftReborn/bin
dotnet restore WorldsAdriftReborn/WorldsAdriftReborn.csproj \
  -p:FrameworkPathOverride="$mono20"
dotnet build   WorldsAdriftReborn/WorldsAdriftReborn.csproj -c Release --no-restore \
  -p:FrameworkPathOverride="$mono20" -p:PluginOutputDirectory="$REL/"

PACK=/tmp/wareborn-pack; rm -rf "$PACK"; mkdir -p "$PACK/plugin" "$PACK/gameroot"
M=WorldsAdriftRebornCoreSdk/build-mingw
cp "$REL/WorldsAdriftReborn.dll" "$REL/WorldsAdriftReborn.dll.config" \
   "$M/CoreSdkDll.dll" "$PACK/plugin/"
for f in "$M"/*.dll; do
  [ "$(basename "$f")" = CoreSdkDll.dll ] && continue
  cp "$f" "$PACK/gameroot/"
done

tools/patcher/build-manifest.sh --pack "$PACK" \
  --version 2026.08.25-1 --build "client attitude spline (turn vibration)"
```

`build-manifest.sh` refuses a CLR 4.0 payload; this one reports
`mscorlib, Version=2.0.0.0`, which is what the Unity 5.6 client runs.

## Verified before staging

- freshly built `WorldsAdriftReborn.dll` md5 == the md5 of the staged copy under
  `tools/patcher/dist/files/`;
- every one of the 54 staged files re-hashes to the `sha256` and `sizeBytes` its
  own manifest entry claims;
- diffed against the LIVE `manifest.json`: nothing added, nothing removed, and
  exactly one file changed.

`tools/patcher/dist/` is git-ignored, so the artefacts are on the dev box only.
Re-cut it with the block above if the working tree has moved on.

## Publish - NOT RUN

Both machines are already on the patcher, so this is the whole procedure. The
Caddy route and the login server's `/patch*` handlers are long since live; only
the payload changes.

```bash
# 1. ship the release
rsync -av --delete tools/patcher/dist/ root@62.171.161.19:/opt/wareborn/patch/

# 2. confirm it is being served
curl -s https://wareborn.ratlabs.cc/patch/manifest.json | jq -r .version   # 2026.08.25-1
curl -sSI https://wareborn.ratlabs.cc/patch/files/BepInEx__plugins__WorldsAdriftReborn__WorldsAdriftReborn.dll | head -1
```

Then each player runs `WAPatch.exe` -> Check for updates -> Patch. It downloads
only the one changed DLL, verifies its sha256 before writing, and keeps a `.bak`.

No server restart, no game-server restart, no Caddy reload: this release changes
a client DLL and nothing else.

## Rollback

Re-cut the manifest from the previous plugin build and rsync again, or restore
`/opt/wareborn/patch/` from the previous `dist/`. A player can also just set
`[Flight] Flight_SmoothShipRotation = false` in
`BepInEx/config/WorldsAdriftReborn.cfg`, which returns the stock client
behaviour without touching any file - the value is re-read live every 5 s.

## Local dev install

The same DLL is installed at
`~/Games/WorldsAdrift/BepInEx/plugins/WorldsAdriftReborn/WorldsAdriftReborn.dll`,
with the previous one kept beside it as
`WorldsAdriftReborn.dll.pre-attitude-spline-20260825` (md5
`019c678563a9d5b6251d4a9fb5ab3ac0`). `~/Games/WorldsAdrift-2` was left alone;
copy the same file there if the A/B needs two clients.
