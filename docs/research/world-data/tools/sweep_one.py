"""Per-island: extract LOD0 surface, thin to spawn candidates in ISLAND-LOCAL space."""
import sys,os,time,json,UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
p=sys.argv[1]; outdir=sys.argv[2]; CELL=8.0
t0=time.time()
env=UnityPy.load(p); objmap={o.path_id:o for o in env.objects}
def sn(t):
    o=objmap.get(t["m_Script"]["m_PathID"])
    if not o: return "?"
    d=o.read_typetree(); ns=d.get("m_Namespace") or ""
    return (ns+"." if ns else "")+(d.get("m_ClassName") or "?")
isd=None; scripts=set()
for o in env.objects:
    if str(o.type.name)=="MonoBehaviour":
        try: t=o.read_typetree()
        except Exception: continue
        n=sn(t); scripts.add(n)
        if n=="IslandSurfaceData": isd=t
tf={}
for o in env.objects:
    if str(o.type.name)=="Transform":
        t=o.read_typetree(); tf[t["m_GameObject"]["m_PathID"]]=t
def offs(go):
    x=y=z=0.0; cur=go
    while True:
        t=tf[cur]; lp=t["m_LocalPosition"]; x+=lp["x"]; y+=lp["y"]; z+=lp["z"]
        fa=t.get("m_Father",{}).get("m_PathID")
        if not fa or fa not in objmap: break
        cur=objmap[fa].read_typetree()["m_GameObject"]["m_PathID"]
    return x,y,z
cand={}; tv=0; up=0
mn=[1e9]*3; mx=[-1e9]*3
for e in (isd or {}).get("lod0Meshes",[]):
    mfo=objmap.get(e["m_PathID"])
    if not mfo: continue
    mf=mfo.read_typetree(); mo=objmap.get(mf["m_Mesh"]["m_PathID"])
    if not mo: continue
    h=MeshHandler(mo.read()); h.process()
    vs=h.m_Vertices or []; ns=h.m_Normals or []
    if len(ns)!=len(vs): ns=[(0.0,1.0,0.0)]*len(vs)
    ox,oy,oz=offs(mf["m_GameObject"]["m_PathID"])
    tv+=len(vs)
    for k in range(len(vs)):
        n=ns[k]
        px,py,pz=vs[k][0]+ox, vs[k][1]+oy, vs[k][2]+oz
        for a,v in enumerate((px,py,pz)):
            if v<mn[a]: mn[a]=v
            if v>mx[a]: mx[a]=v
        if n[1]<=0.4: continue
        up+=1
        key=(int(px//CELL),int(py//CELL),int(pz//CELL))
        if key not in cand:
            cand[key]=(round(px,2),round(py,2),round(pz,2),round(n[0],3),round(n[1],3),round(n[2],3))
wid=os.path.basename(p).split("@")[0]
rec={"island":wid,"mib":round(os.path.getsize(p)/1048576,2),
     "hasProxy":"IslandProxyVisualizer" in scripts,
     "hasSurfaceData":isd is not None,
     "cells":len(isd["lod0Meshes"]) if isd else 0,
     "verts":tv,"upVerts":up,"candidates":len(cand),
     "localAABB":{"min":[round(v,1) for v in mn],"max":[round(v,1) for v in mx]} if tv else None,
     "sec":round(time.time()-t0,2)}
json.dump({"meta":rec,"cell":CELL,"points":list(cand.values())},
          open(os.path.join(outdir,wid+".json"),"w"))
print(json.dumps(rec))
