#!/usr/bin/env python3
"""MonoBehaviour class census on selected prefab roots, resolving MonoScript across files.

Usage: census2.py <name-substring> [...]
Loads the whole UnityClient@Windows_Data dir so cross-file MonoScript PPtrs resolve.
Prints TSV: root_path_id, root_name, component_class, enabled
"""
import os, sys, UnityPy

DATA = os.path.expanduser("~/Games/WorldsAdrift/UnityClient@Windows_Data")
wanted = [w.lower() for w in sys.argv[1:]]

FILES = ["resources.assets", "globalgamemanagers.assets", "sharedassets0.assets",
         "globalgamemanagers", "level0", "level1"]

envs = {}
script_names = {}   # (srcfile, path_id) -> class name ; also path_id -> name fallback
by_pid = {}
for f in FILES:
    p = os.path.join(DATA, f)
    if not os.path.exists(p):
        continue
    e = UnityPy.load(p)
    envs[f] = e
    for o in e.objects:
        if o.type.name == "MonoScript":
            try:
                d = o.read_typetree()
            except Exception:
                continue
            script_names.setdefault(o.path_id, d.get("m_ClassName", "?"))

env = envs["resources.assets"]
objs = {o.path_id: o for o in env.objects}

roots = []
gos = {}
for pid, o in objs.items():
    if o.type.name == "GameObject":
        try:
            d = o.read_typetree()
        except Exception:
            continue
        nm = d.get("m_Name", "")
        gos[pid] = (nm, d)
        if any(w in nm.lower() for w in wanted):
            roots.append(pid)

def pptr(x):
    if isinstance(x, dict):
        return x.get("m_FileID", 0), x.get("m_PathID", 0)
    return getattr(x, "file_id", 0), getattr(x, "path_id", 0)

print("root_path_id\troot_name\tcomponent_class\tenabled")
for pid in sorted(roots):
    nm, d = gos[pid]
    for c in d.get("m_Component", []):
        comp = c.get("component", c) if isinstance(c, dict) else c
        fid, cpid = pptr(comp)
        if cpid not in objs:
            print(f"{pid}\t{nm}\t<missing pptr {fid}:{cpid}>\t")
            continue
        co = objs[cpid]
        if co.type.name != "MonoBehaviour":
            print(f"{pid}\t{nm}\t<{co.type.name}>\t")
            continue
        try:
            cd = co.read_typetree()
            sfid, spid = pptr(cd.get("m_Script", {}))
            en = cd.get("m_Enabled")
        except Exception:
            # raw MonoBehaviour header: PPtr<GameObject>{i32,i64} u8 enabled +3pad
            # PPtr<MonoScript>{i32,i64}
            raw = co.get_raw_data()
            sfid = int.from_bytes(raw[16:20], "little", signed=True)
            spid = int.from_bytes(raw[20:28], "little", signed=True)
            en = raw[12]
        cname = script_names.get(spid, f"?fid{sfid}:pid{spid}")
        print(f"{pid}\t{nm}\t{cname}\t{en}")
