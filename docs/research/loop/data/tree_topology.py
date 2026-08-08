"""Recover the authored TreeBase topology (branches / sectionMask / sectionsActive)
and the per-TreeSection id+harvestable+cutPoint from the shipped Worlds Adrift
client assets.

resources.assets ships no MonoBehaviour typetrees, so the serialized layout is
hand-parsed.  Technique copied from docs/research/loop/data/tree_woodtypes.py.

MonoBehaviour header:
  m_GameObject PPtr (int32 fileID + int64 pathID = 12) | m_Enabled u8 + 3 pad
  | m_Script PPtr (12) | m_Name (int32 len + utf8 + align4)

TreeBase serialized fields (decompile /acs/TreeBase.cs, declaration order):
  TreeSection[] treeSections   int32 n + n*PPtr(12)
  Branch[]      branches       int32 n + n*{int32 root; int32 m; m*int32 sections}
  int  sectionMask             4
  int  sectionsActive          4
  Transform sectionsRoot       PPtr 12
  bool dynamic                 1 + 3 pad
  bool doCombineOptimization   1 + 3 pad
  MeshFilter combinedMeshFilter PPtr 12
  bool drawTreeBranchGizmos    1 + 3 pad
  TreeFsimVisualizer fsimVisualizer PPtr 12
  float rigidbodyDensity       4
  string autoFillSections      "Auto Fill Sections"
  string deparentAll           "Deparent All"
  string debugInitTree         "Debug Initialize Tree"

TreeSection serialized fields (/acs/TreeSection.cs):
  bool harvestable  (4) | TreeBase tree PPtr(12) | int id (4)
  | Transform cutPoint PPtr(12) | int connectionStrength (4)
  | Transform hitEffect PPtr(12)
"""
import json, os, struct, sys, UnityPy

DATA = "/home/ttanurhan/Games/WorldsAdrift/UnityClient@Windows_Data"
FILES = ("resources.assets", "sharedassets0.assets",
         "sharedassets1.assets", "globalgamemanagers.assets")
TARGET = sys.argv[1] if len(sys.argv) > 1 else "Tree_unityclient"

env = UnityPy.load(*[os.path.join(DATA, f) for f in FILES])

idx = {}
for o in env.objects:
    idx[(os.path.basename(getattr(o.assets_file, "name", "")).lower(), o.path_id)] = o
print(f"indexed {len(idx)} objects")


def key(o):
    return (os.path.basename(getattr(o.assets_file, "name", "")).lower(), o.path_id)


def resolve(src, fid, pid):
    if pid == 0:
        return None
    if fid == 0:
        nm = os.path.basename(getattr(src.assets_file, "name", "")).lower()
    else:
        try:
            nm = os.path.basename(src.assets_file.externals[fid - 1].name).lower()
        except Exception:
            return None
    return idx.get((nm, pid))


def rstr(raw, off):
    n = struct.unpack_from("<i", raw, off)[0]
    if n < 0 or n > 4096 or off + 4 + n > len(raw):
        return None, off
    s = raw[off + 4:off + 4 + n].decode("utf-8", "replace")
    off = off + 4 + n
    off = (off + 3) // 4 * 4
    return s, off


scriptname = {}
for k, o in idx.items():
    if str(o.type.name) != "MonoScript":
        continue
    try:
        scriptname[k] = o.read_typetree().get("m_ClassName", "")
    except Exception:
        pass

goname = {}
for k, o in idx.items():
    if str(o.type.name) != "GameObject":
        continue
    try:
        goname[k] = o.read_typetree().get("m_Name", "")
    except Exception:
        pass
print(f"{len(scriptname)} MonoScripts, {len(goname)} GameObjects")


def classof(mb, raw):
    sf, sp = struct.unpack_from("<iq", raw, 16)
    ms = resolve(mb, sf, sp)
    if ms is None:
        return None
    return scriptname.get(key(ms))


def gameobject_of(mb, raw):
    gf, gp = struct.unpack_from("<iq", raw, 0)
    go = resolve(mb, gf, gp)
    if go is None:
        return None, "?"
    return go, goname.get(key(go), "?")


# ---------------------------------------------------------------- collect
mbraw = {}
for k, o in idx.items():
    if str(o.type.name) != "MonoBehaviour":
        continue
    try:
        raw = o.get_raw_data()
    except Exception:
        continue
    if len(raw) < 32:
        continue
    mbraw[k] = raw

treebases = []
for k, raw in mbraw.items():
    o = idx[k]
    if classof(o, raw) == "TreeBase":
        treebases.append((k, o, raw))
print(f"TreeBase instances: {len(treebases)}")


def parse_section(o, raw):
    off = 28
    name, off = rstr(raw, off)
    harvestable = raw[off]
    off += 4
    tf, tp = struct.unpack_from("<iq", raw, off); off += 12
    sid = struct.unpack_from("<i", raw, off)[0]; off += 4
    cf, cp = struct.unpack_from("<iq", raw, off); off += 12
    conn = struct.unpack_from("<i", raw, off)[0]; off += 4
    hf, hp = struct.unpack_from("<iq", raw, off); off += 12
    go, gname = gameobject_of(o, raw)
    cut = resolve(o, cf, cp)
    return dict(go=gname, harvestable=bool(harvestable), harv_raw=harvestable,
                id=sid, cutPoint_pathid=cp, cutPoint_resolved=(cut is not None),
                cutPoint_type=(str(cut.type.name) if cut is not None else None),
                connectionStrength=conn, hitEffect_pathid=hp,
                tail=len(raw) - off, tree_pathid=tp)


def parse_treebase(o, raw):
    r = {}
    off = 28
    r["m_Name"], off = rstr(raw, off)
    n = struct.unpack_from("<i", raw, off)[0]; off += 4
    r["treeSections_count"] = n
    if n < 0 or n > 256:
        r["error"] = f"implausible treeSections count {n}"
        return r
    secptr = []
    for _ in range(n):
        f, p = struct.unpack_from("<iq", raw, off); off += 12
        secptr.append((f, p))
    nb = struct.unpack_from("<i", raw, off)[0]; off += 4
    r["branch_count"] = nb
    if nb < 0 or nb > 256:
        r["error"] = f"implausible branch count {nb}"
        return r
    branches = []
    for _ in range(nb):
        root = struct.unpack_from("<i", raw, off)[0]; off += 4
        m = struct.unpack_from("<i", raw, off)[0]; off += 4
        if m < 0 or m > 256 or off + 4 * m > len(raw):
            r["error"] = f"implausible branch sections count {m}"
            return r
        secs = list(struct.unpack_from("<%di" % m, raw, off)); off += 4 * m
        branches.append({"root": root, "sections": secs})
    r["branches"] = branches
    r["sectionMask"] = struct.unpack_from("<i", raw, off)[0]; off += 4
    r["sectionsActive"] = struct.unpack_from("<i", raw, off)[0]; off += 4
    sf, sp = struct.unpack_from("<iq", raw, off); off += 12   # sectionsRoot
    r["dynamic"] = bool(raw[off]); off += 4
    r["doCombineOptimization"] = bool(raw[off]); off += 4
    cf, cp = struct.unpack_from("<iq", raw, off); off += 12   # combinedMeshFilter
    r["drawTreeBranchGizmos"] = bool(raw[off]); off += 4
    ff, fp = struct.unpack_from("<iq", raw, off); off += 12   # fsimVisualizer
    r["fsimVisualizer_pathid"] = fp
    r["rigidbodyDensity"] = struct.unpack_from("<f", raw, off)[0]; off += 4
    r["autoFillSections"], off = rstr(raw, off)
    r["deparentAll"], off = rstr(raw, off)
    r["debugInitTree"], off = rstr(raw, off)
    r["tail_bytes"] = len(raw) - off
    # resolve sections
    secs = []
    for i, (f, p) in enumerate(secptr):
        so = resolve(o, f, p)
        if so is None:
            secs.append({"index": i, "resolved": False, "pathid": p})
            continue
        sraw = mbraw.get(key(so))
        cn = classof(so, sraw) if sraw is not None else None
        d = {"index": i, "resolved": True, "pathid": p, "class": cn}
        if cn == "TreeSection":
            d.update(parse_section(so, sraw))
        secs.append(d)
    r["sections"] = secs
    return r


results = []
for k, o, raw in treebases:
    go, gname = gameobject_of(o, raw)
    r = parse_treebase(o, raw)
    r["gameObject"] = gname
    r["assetfile"] = k[0]
    r["path_id"] = k[1]
    r["raw_len"] = len(raw)
    results.append(r)

STR_OK = ("Auto Fill Sections", "Deparent All", "Debug Initialize Tree")


def strings_ok(r):
    return (r.get("autoFillSections"), r.get("deparentAll"), r.get("debugInitTree")) == STR_OK


ok = sum(1 for r in results if strings_ok(r))
print(f"structured parse: {ok}/{len(results)} TreeBase have the 3 sentinel strings correct")
bad = [r for r in results if not strings_ok(r)]
for r in bad[:10]:
    print("   BAD:", r.get("gameObject"), r.get("error"),
          repr(r.get("autoFillSections"))[:40], repr(r.get("deparentAll"))[:30])

names = sorted({r["gameObject"] for r in results})
print(f"\ndistinct TreeBase GameObject names ({len(names)}):")
for nm in names:
    print("   ", nm)

out = "/tmp/claude-1000/-home-ttanurhan-Documents-Claude-Projects/ff15d21e-990d-43c5-9a18-cdf8ff2884cf/scratchpad/tree_topology.json"
json.dump(results, open(out, "w"), indent=1)
print("\nwrote", out)

# ---------------------------------------------------------------- target
tg = [r for r in results if r["gameObject"] == TARGET]
print(f"\n=== TARGET {TARGET}: {len(tg)} match(es) ===")
for r in tg:
    print(json.dumps({k: v for k, v in r.items() if k != "sections"}, indent=1))
    for s in r.get("sections", []):
        print("   ", s)
