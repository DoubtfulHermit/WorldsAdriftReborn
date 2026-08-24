#!/usr/bin/env bash
#
# build-manifest.sh - cut a client patch release for Worlds Adrift Reborn.
#
# WHY THIS EXISTS
#   Updating a player used to mean hand-mailing them WAReborn-Update.zip and
#   having them run UPDATE.bat. This tool turns a "pack" (the same plugin/ +
#   gameroot/ layout that zip has) into a hosted release the WAPatch client can
#   self-update from: a manifest.json plus a flat directory of the files it
#   references, ready to rsync to the VPS and serve behind Caddy.
#
# WHAT IT PRODUCES  (under --out, default tools/patcher/dist/)
#   manifest.json        version, timestamp, and per-file {destPath, name,
#                        sha256, sizeBytes, url}
#   files/<flattened>    every payload file, named so it is unique in one flat
#                        dir (destPath with '/' -> '__')
#   The whole --out dir mirrors what lives at /opt/wareborn/patch/ on the VPS:
#   rsync it there and Caddy serves manifest.json + files/ under /patch/.
#
# LAYOUT CONTRACT (identical to WAReborn-Update.zip, so a pack is a valid input)
#   <pack>/plugin/*     -> BepInEx/plugins/WorldsAdriftReborn/<name>   (client mod)
#   <pack>/gameroot/*   -> <name>                                       (runtime DLLs at game root)
#
# NEVER SHIPPED
#   steam_api64.dll (the game's own) and winhttp.dll (BepInEx's loader) are
#   excluded on purpose, exactly as the pack excludes them. If a pack ever
#   contains one, this tool drops it and warns rather than shipping it.
#
# SOURCE OF THE FILES
#   The default pack for release #1 is the known-good WAReborn-Update pack that
#   was hand-tested. Its runtime DLLs + CoreSdkDll.dll are byte-identical to the
#   committed WorldsAdriftRebornCoreSdk/build-mingw/ tree, and its
#   WorldsAdriftReborn.dll matches the live plugin build - so a pack assembled
#   from those two locations is equivalent. Point --pack at whatever pack you
#   want to ship (client pack #2 will be the first real payload through here).
#
set -euo pipefail

# ---- defaults ------------------------------------------------------------
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# There is deliberately NO default pack. The original default pointed at a
# throwaway session scratchpad that no longer exists, so an omitted --pack
# failed with a confusing "no plugin/ under pack" instead of saying what to do.
# --pack is now required and the error below is the recipe.
PACK=""
OUT="$HERE/dist"
VERSION=""
BUILD_LABEL="client-pack"
BASE_URL="https://wareborn.ratlabs.cc/patch/files"

usage() {
  cat <<EOF
Usage: build-manifest.sh [options]
  --pack DIR       Pack dir containing plugin/ and gameroot/ (default: the
                   known-good WAReborn-Update pack)
  --out DIR        Output dir; becomes /opt/wareborn/patch/ on the VPS
                   (default: $HERE/dist)
  --version STR    Version string players see (default: YYYY.MM.DD-N auto)
  --build LABEL    Human build label (default: client-pack)
  --base-url URL   Base URL for file downloads
                   (default: https://wareborn.ratlabs.cc/patch/files)
  -h, --help       This help
EOF
}

# ---- args ----------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --pack)     PACK="$2"; shift 2 ;;
    --out)      OUT="$2"; shift 2 ;;
    --version)  VERSION="$2"; shift 2 ;;
    --build)    BUILD_LABEL="$2"; shift 2 ;;
    --base-url) BASE_URL="$2"; shift 2 ;;
    -h|--help)  usage; exit 0 ;;
    *) echo "unknown arg: $1" >&2; usage; exit 2 ;;
  esac
done

BASE_URL="${BASE_URL%/}"   # no trailing slash; we join with a single '/'

if [[ -z "$PACK" ]]; then
  cat >&2 <<'NOPACK'
ERROR: --pack is required.

Assemble one (54 files: 3 plugin + 51 gameroot), from the two locations the
README names as equivalent to the original hand-tested pack:

  PACK=$(mktemp -d /tmp/wareborn-pack.XXXXXX)
  mkdir -p "$PACK/plugin" "$PACK/gameroot"
  M=WorldsAdriftRebornCoreSdk/build-mingw
  # plugin/: the freshly built client DLL + its .config, and CoreSdkDll.dll
  cp "<game>/BepInEx/plugins/WorldsAdriftReborn/WorldsAdriftReborn.dll" \
     "<game>/BepInEx/plugins/WorldsAdriftReborn/WorldsAdriftReborn.dll.config" \
     "$M/CoreSdkDll.dll" "$PACK/plugin/"
  # gameroot/: every other runtime DLL from the same mingw tree
  for f in "$M"/*.dll; do
    [ "$(basename "$f")" = CoreSdkDll.dll ] && continue
    cp "$f" "$PACK/gameroot/"
  done

<game> is the local Worlds Adrift install; building WorldsAdriftReborn copies
the plugin DLL there. Before shipping, diff the assembled pack against the LIVE
manifest hashes - only the files you actually changed should differ.
NOPACK
  exit 1
fi
[[ -d "$PACK/plugin"   ]] || { echo "ERROR: no plugin/ under pack '$PACK'"   >&2; exit 1; }
[[ -d "$PACK/gameroot" ]] || { echo "ERROR: no gameroot/ under pack '$PACK'" >&2; exit 1; }

# Unity 5.6 hosts this plugin on the CLR 2.0 runtime. A net35 project can still
# be accidentally compiled against Mono's 4.5 reference directory when an
# operator supplies the wrong FrameworkPathOverride; the build succeeds, but
# BepInEx then throws TypeLoadException before any compatibility patches run.
# Refuse that payload here, at the last boundary before it reaches players.
CLIENT_DLL="$PACK/plugin/WorldsAdriftReborn.dll"
[[ -f "$CLIENT_DLL" ]] || {
  echo "ERROR: plugin/WorldsAdriftReborn.dll is required" >&2
  exit 1
}
client_refs="$(strings "$CLIENT_DLL")"
if grep -Fq 'mscorlib, Version=4.0.0.0' <<<"$client_refs"; then
  cat >&2 <<'BADCLR'
ERROR: WorldsAdriftReborn.dll targets mscorlib 4.0, but the shipped Unity client
runs CLR 2.0. Rebuild net35 with FrameworkPathOverride pointing at Mono's
2.0-api directory. Publishing this DLL would disable every client patch.
BADCLR
  exit 1
fi
if ! grep -Fq 'mscorlib, Version=2.0.0.0' <<<"$client_refs"; then
  echo "ERROR: could not prove that WorldsAdriftReborn.dll targets CLR 2.0" >&2
  exit 1
fi

# Auto version: date plus a per-day counter so two cuts on one day differ.
if [[ -z "$VERSION" ]]; then
  today="$(date -u +%Y.%m.%d)"
  n=1
  while [[ -e "$OUT/.cut-$today-$n" ]]; do n=$((n+1)); done
  VERSION="$today-$n"
fi

echo "  pack:    $PACK"
echo "  out:     $OUT"
echo "  version: $VERSION  (build: $BUILD_LABEL)"
echo "  baseUrl: $BASE_URL"
echo

# ---- clean output --------------------------------------------------------
rm -rf "$OUT"
mkdir -p "$OUT/files"

# ---- collect (destPath \t sourcePath), applying exclusions ---------------
EXCLUDE_RE='^(steam_api64|winhttp)\.dll$'   # case-insensitive below
ENTRIES=()   # "destPath<TAB>srcPath"

add_dir() {
  local srcdir="$1" destprefix="$2" f base
  # Sorted for a stable manifest order.
  while IFS= read -r -d '' f; do
    base="$(basename "$f")"
    if [[ "${base,,}" =~ $EXCLUDE_RE ]]; then
      echo "  ! skipping excluded file: $base"
      continue
    fi
    ENTRIES+=("${destprefix}${base}"$'\t'"$f")
  done < <(find "$srcdir" -maxdepth 1 -type f -print0 | sort -z)
}

add_dir "$PACK/plugin"   "BepInEx/plugins/WorldsAdriftReborn/"
add_dir "$PACK/gameroot" ""

[[ ${#ENTRIES[@]} -gt 0 ]] || { echo "ERROR: nothing to ship" >&2; exit 1; }

# ---- copy flattened files + gather metadata ------------------------------
# We hand the collected rows to python only to assemble JSON safely; hashing
# and copying happen here so the shell stays the source of truth.
META=""   # "destPath\tflatName\tsha256\tsize" rows
for row in "${ENTRIES[@]}"; do
  destPath="${row%%$'\t'*}"
  srcPath="${row#*$'\t'}"
  flat="${destPath//\//__}"
  cp -f "$srcPath" "$OUT/files/$flat"
  sha="$(sha256sum "$OUT/files/$flat" | cut -d' ' -f1)"
  size="$(stat -c%s "$OUT/files/$flat")"
  META+="${destPath}"$'\t'"${flat}"$'\t'"${sha}"$'\t'"${size}"$'\n'
  printf '  + %-56s %10s bytes\n' "$destPath" "$size"
done

# ---- emit manifest.json --------------------------------------------------
# META goes through a temp file, not stdin: python reads its own program from
# stdin (the heredoc), so the rows have to arrive by another channel.
GENERATED="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
META_FILE="$OUT/.meta.tsv"
printf '%s' "$META" > "$META_FILE"
python3 - "$OUT/manifest.json" "$VERSION" "$BUILD_LABEL" "$GENERATED" "$BASE_URL" "$META_FILE" <<'PY'
import json, sys
out_path, version, build, generated, base_url, meta_file = sys.argv[1:7]
files = []
with open(meta_file, encoding="utf-8") as mf:
    rows = mf.read().splitlines()
for line in rows:
    if not line.strip():
        continue
    destPath, flat, sha, size = line.split("\t")
    files.append({
        "destPath":  destPath,
        "name":      flat,
        "sha256":    sha,
        "sizeBytes": int(size),
        "url":       f"{base_url}/{flat}",
    })
manifest = {
    "schemaVersion": 1,
    "version":       version,
    "build":         build,
    "generatedUtc":  generated,
    "baseUrl":       base_url,
    "files":         files,
}
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
print(f"  manifest: {len(files)} files -> {out_path}")
PY

rm -f "$META_FILE"

# Breadcrumb so the auto per-day version counter advances.
: > "$OUT/.cut-$VERSION"

echo
echo "  Done. Ship it:"
echo "    rsync -av --delete '$OUT/' root@62.171.161.19:/opt/wareborn/patch/"
