# Worlds Adrift Reborn - client patcher

Self-update pipeline so a player installs once and pulls new client files from a
hosted manifest, instead of us hand-mailing `WAReborn-Update.zip` every time a
DLL changes. Three pieces:

1. **`build-manifest.sh`** - dev-box release tool. Turns a pack into
   `manifest.json` + a flat file dir ready to rsync.
2. **`WAPatch/`** - the Windows app a player runs. One self-contained `.exe`:
   pick the game folder, check, patch. Verifies every downloaded byte.
3. **The `/patch` page + Caddy static hosting** - a browser index of the latest
   build, and the static bytes the app downloads.

The first real payload through this pipe will be **client pack #2** (not merged
yet). It is seeded and tested end-to-end with the current known-good files.

---

## 1. Cutting a release (on the dev box)

```bash
tools/patcher/build-manifest.sh \
  --pack /path/to/pack \          # dir with plugin/ and gameroot/ (defaults to the known-good WAReborn-Update pack)
  --version 2026.08.09-1 \        # what players see; auto YYYY.MM.DD-N if omitted
  --build   client-pack-2         # human label
```

Output lands in `tools/patcher/dist/` (git-ignored):

```
dist/manifest.json      the manifest
dist/files/<flattened>  every payload file, one flat dir
```

`dist/` mirrors exactly what lives at `/opt/wareborn/patch/` on the VPS.

### Where the files come from

The layout is identical to `WAReborn-Update.zip`, so a pack is a valid input:

| pack subdir  | installs to                              | contents                              |
|--------------|------------------------------------------|---------------------------------------|
| `plugin/*`   | `BepInEx/plugins/WorldsAdriftReborn/`    | `WorldsAdriftReborn.dll` + `.dll.config`, `CoreSdkDll.dll` |
| `gameroot/*` | game root                                | 51 runtime DLLs (`lib*.dll`, `zlib1.dll`, `libprotobuf.dll`, ...) |

The default pack's runtime DLLs + `CoreSdkDll.dll` are byte-identical to the
committed `WorldsAdriftRebornCoreSdk/build-mingw/` tree, and its
`WorldsAdriftReborn.dll` matches the live plugin build - so an equivalent pack
can be assembled from those two locations for a fresh cut.

**Never shipped:** `steam_api64.dll` (the game's own) and `winhttp.dll`
(BepInEx's loader). The tool drops them with a warning if a pack contains one.

### Manifest schema

```json
{
  "schemaVersion": 1,
  "version": "2026.08.09-1",
  "build": "client-pack-2",
  "generatedUtc": "2026-08-09T17:03:52Z",
  "baseUrl": "https://wareborn.ratlabs.cc/patch/files",
  "files": [
    {
      "destPath":  "BepInEx/plugins/WorldsAdriftReborn/CoreSdkDll.dll",
      "name":      "BepInEx__plugins__WorldsAdriftReborn__CoreSdkDll.dll",
      "sha256":    "991deded...94a",
      "sizeBytes": 338140,
      "url":       "https://wareborn.ratlabs.cc/patch/files/BepInEx__plugins__WorldsAdriftReborn__CoreSdkDll.dll"
    }
  ]
}
```

- `destPath` - install path relative to the game folder, forward slashes.
- `name` - `destPath` with `/` -> `__`, so every file is unique in one flat dir.
- `sha256` - lowercase hex; the integrity contract. The app verifies downloaded
  bytes against this and **refuses** a mismatch.
- `url` - absolute download URL (the app prefers it; falls back to
  `baseUrl` + `name`).

---

## 2. Building the patcher app (WAPatch.exe)

Self-contained, single-file, win-x64 - a player needs **no** .NET install.

```bash
# Needs an SDK that includes the Windows Desktop targets (the official .NET SDK
# does; the Arch dotnet package does NOT - use ~/.dotnet if that is your case):
export DOTNET_ROOT=$HOME/.dotnet; export PATH=$HOME/.dotnet:$PATH

dotnet publish tools/patcher/WAPatch/WAPatch.csproj -c Release \
  -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `tools/patcher/WAPatch/bin/Release/net8.0-windows/win-x64/publish/WAPatch.exe`
(~145 MB - WinForms + the whole runtime bundled). Hand this one file to players.

### What it does

- Remembers the chosen install dir + last-installed version in
  `wapatch.config.json` next to the exe.
- Refuses anything that is not a real install: needs `UnityClient@Windows.exe`
  and a `BepInEx/` folder (same guard as the old `UPDATE.bat`).
- Fetches `manifest.json` over HTTPS, hashes each local file, downloads **only**
  files whose hash differs or that are missing.
- **Verifies** each downloaded file's sha256 (and size) against the manifest
  before it touches disk. A mismatch is refused, never written.
- **Backs up** each file once before overwriting (`.bak`, keep-first) - a second
  run never clobbers the original backup.
- Writes via a temp file + move, so a crash mid-write can't leave a half DLL.
- Never touches `steam_api64.dll` or `winhttp.dll`.
- Shows current-vs-latest version so a player knows if they are behind.

### How a player uses it

**First run:** double-click `WAPatch.exe` -> **Browse** to the Worlds Adrift
folder (the one with `UnityClient@Windows.exe`) -> **Check for updates** ->
**Patch**. Close the game first if it is running.

**Later:** double-click, **Check for updates**. If it says "up to date", done.
Otherwise **Patch**. The folder is remembered between runs.

---

## 3. Going live (operator steps on the VPS)

You (the operator) apply these; the release tool and app are built on the dev box.

### 3a. Create the patch dir and rsync the release

```bash
ssh root@62.171.161.19 'mkdir -p /opt/wareborn/patch'

# from the dev box, after running build-manifest.sh:
rsync -av --delete tools/patcher/dist/ root@62.171.161.19:/opt/wareborn/patch/
```

On-disk result:

```
/opt/wareborn/patch/manifest.json
/opt/wareborn/patch/files/<flattened files>
```

### 3b. Caddy edit

Add this **inside the existing `wareborn.ratlabs.cc { ... }` block** in
`/root/Avatar/Caddyfile`, alongside the `/signup`, `/register`, `/admin*`
routes. Replace `LOGIN_UPSTREAM` with the **same upstream the existing `/signup`
route proxies to** (the login server).

```caddy
# --- client patcher -------------------------------------------------------
# Static payload: request /patch/<x> maps to /opt/wareborn/patch/<x>
# (so /patch/manifest.json -> /opt/wareborn/patch/manifest.json).
@patch_static path /patch/manifest.json /patch/files/*
handle @patch_static {
    root * /opt/wareborn
    file_server
}

# The human-readable index page is served by the login server, like /signup.
handle /patch {
    reverse_proxy LOGIN_UPSTREAM
}
# --------------------------------------------------------------------------
```

Then reload Caddy (`caddy reload --config /root/Avatar/Caddyfile`, or however the
gateway reloads it).

### 3c. Verify

```bash
curl -sSI https://wareborn.ratlabs.cc/patch/manifest.json | head -1   # 200
curl -s   https://wareborn.ratlabs.cc/patch/manifest.json | jq .version
curl -sSI https://wareborn.ratlabs.cc/patch/files/zlib1.dll | head -1  # 200
# and open https://wareborn.ratlabs.cc/patch in a browser
```

The login server also needs the `/patch` route, which is already wired in
`WorldsAdriftServer` (`RequestRouterHandler` + `Web/PatchPage.cs`) - so a
redeploy of the login server is what turns the index page on. The static files
are independent of that and served entirely by Caddy.
