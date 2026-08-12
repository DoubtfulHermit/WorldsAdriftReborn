"""Recover Bossa's authored per-species woodType from the _unityworker tree prefabs.

TreeFsimVisualizer is added at prefab-export time on UnityWorker builds only
(TreePreprocessor.cs:35-39), and carries `public string woodType`.  resources.assets
ships no MonoBehaviour typetrees, so parse the serialized layout by hand:

  m_GameObject PPtr (4+8) | m_Enabled u8 +3 pad | m_Script PPtr (4+8) | m_Name str
  then TreeFsimVisualizer's own serialized fields, in declaration order:
    public bool drawSpaceCheckGizmos   (1 byte + 3 pad)
    public TreeBase tree               (PPtr 4+8)
    public string woodType             (int32 len + utf8 + align4)
"""
import json, os, struct, collections, UnityPy

DATA="/home/ttanurhan/Games/WorldsAdrift/UnityClient@Windows_Data"
WOODS={"cedar","hemlock","chestnut","elm","birch","ash","oak","palm"}
env=UnityPy.load(*[os.path.join(DATA,f) for f in
                   ("resources.assets","sharedassets0.assets",
                    "sharedassets1.assets","globalgamemanagers.assets")])
idx={}
for o in env.objects:
    idx[(os.path.basename(getattr(o.assets_file,"name","")).lower(),o.path_id)]=o

def resolve(src,fid,pid):
    if pid==0: return None
    if fid==0: nm=os.path.basename(getattr(src.assets_file,"name","")).lower()
    else:
        try: nm=os.path.basename(src.assets_file.externals[fid-1].name).lower()
        except Exception: return None
    return idx.get((nm,pid))

def rstr(raw,off):
    n=struct.unpack_from("<i",raw,off)[0]
    if n<0 or n>512: return None,off
    s=raw[off+4:off+4+n].decode("utf-8","replace")
    off=off+4+n
    off=(off+3)//4*4
    return s,off

# map MonoScript pathid -> class name
scriptname={}
for (nm,pid),o in idx.items():
    if str(o.type.name)!="MonoScript": continue
    try: scriptname[(nm,pid)]=o.read_typetree().get("m_ClassName","")
    except Exception: pass

# GameObject names
goname={}
for (nm,pid),o in idx.items():
    if str(o.type.name)!="GameObject": continue
    try: goname[(nm,pid)]=o.read_typetree().get("m_Name","")
    except Exception: pass

out={}; structured=0; scanned=0; misses=[]
for (nm,pid),o in idx.items():
    if str(o.type.name)!="MonoBehaviour": continue
    try: raw=o.get_raw_data()
    except Exception: continue
    if len(raw)<32: continue
    sf,sp=struct.unpack_from("<iq",raw,16)
    ms=resolve(o,sf,sp)
    if ms is None: continue
    cn=scriptname.get((os.path.basename(getattr(ms.assets_file,"name","")).lower(),ms.path_id))
    if cn!="TreeFsimVisualizer": continue
    gf,gp=struct.unpack_from("<iq",raw,0)
    go=resolve(o,gf,gp)
    root=goname.get((os.path.basename(getattr(go.assets_file,"name","")).lower(),go.path_id),"?") if go else "?"
    # structured parse
    off=28
    _,off=rstr(raw,off)          # m_Name
    off+=4                        # drawSpaceCheckGizmos + pad
    off+=12                       # tree PPtr
    wood,_=rstr(raw,off)
    scanned+=1
    if wood in WOODS:
        structured+=1
    else:
        # fallback: brute scan for any known wood as a length-prefixed string
        found=None
        for w in WOODS:
            b=struct.pack("<i",len(w))+w.encode()
            if b in raw: found=w; break
        misses.append((root,repr(wood)[:40],found))
        wood=found
    out[root]=wood

print(f"TreeFsimVisualizer instances: {scanned}   structured-parse hits: {structured}")
if misses:
    print(f"structured parse missed {len(misses)}:")
    for m in misses[:15]: print("   ",m)
print()
byw=collections.Counter(v for v in out.values() if v)
print("WOOD TYPE DISTRIBUTION:")
for k,v in byw.most_common(): print(f"   {v:4d}  {k}")
print(f"   unresolved: {sum(1 for v in out.values() if not v)}")
print()
for k in sorted(out): print(f"   {out[k]:<10} {k}")
json.dump(out,open("tree_woodtypes.json","w"),indent=1,sort_keys=True)
