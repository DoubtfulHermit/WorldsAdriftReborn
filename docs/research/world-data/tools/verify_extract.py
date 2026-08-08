"""Sanity-check the offline surface extraction against independent signals."""
import sys, re, collections
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

env = UnityPy.load(sys.argv[1])
objmap = {o.path_id: o for o in env.objects}
def T(pid):
    o = objmap.get(pid); return o.read_typetree() if o else None
def sname(t):
    o = objmap.get(t["m_Script"]["m_PathID"])
    d = o.read_typetree(); ns = d.get("m_Namespace") or ""
    return (ns+"." if ns else "")+d["m_ClassName"]

isd = None
for o in env.objects:
    if str(o.type.name)=="MonoBehaviour":
        t=o.read_typetree()
        if sname(t)=="IslandSurfaceData": isd=t; break

tf_of_go={}
for o in env.objects:
    if str(o.type.name)=="Transform":
        t=o.read_typetree(); tf_of_go[t["m_GameObject"]["m_PathID"]]=t

def worldpos(go):
    # accumulate localPosition up the chain (rotations are identity for the grid cells; verified below)
    x=y=z=0.0; rots=[]
    cur=go
    while True:
        t=tf_of_go[cur]
        lp=t["m_LocalPosition"]; x+=lp["x"]; y+=lp["y"]; z+=lp["z"]
        rots.append(t["m_LocalRotation"]); sc=t["m_LocalScale"]
        fa=t.get("m_Father",{}).get("m_PathID")
        if not fa or fa not in objmap: break
        cur=objmap[fa].read_typetree()["m_GameObject"]["m_PathID"]
    return (x,y,z), rots

print("=== check 1: are all grid-cell transforms unrotated / unit scale? ===")
bad_rot=bad_scale=0
for e in isd["lod0Meshes"]:
    mf=T(e["m_PathID"]); t=tf_of_go[mf["m_GameObject"]["m_PathID"]]
    q=t["m_LocalRotation"]; s=t["m_LocalScale"]
    if abs(q["w"]-1.0)>1e-5 or abs(q["x"])+abs(q["y"])+abs(q["z"])>1e-5: bad_rot+=1
    if abs(s["x"]-1)>1e-5 or abs(s["y"]-1)>1e-5 or abs(s["z"]-1)>1e-5: bad_scale+=1
print(f"  rotated cells: {bad_rot}/497   non-unit-scale cells: {bad_scale}/497")

print()
print("=== check 2: does the cell NAME (i,j,k) predict its world position? ===")
samples=[]
for e in isd["lod0Meshes"][:400]:
    mf=T(e["m_PathID"])
    mo=objmap[mf["m_Mesh"]["m_PathID"]]
    nm=mo.read_typetree()["m_Name"]
    m=re.match(r"\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)_LOD0", nm)
    if not m: continue
    i,j,k=[float(g) for g in m.groups()]
    wp,_=worldpos(mf["m_GameObject"]["m_PathID"])
    samples.append(((i,j,k),wp))
if samples:
    for (ijk,wp) in samples[:4]:
        print(f"  cell {ijk} -> transform world {tuple(round(v,2) for v in wp)}")
    # infer cell size from two cells differing by 1 in i
    d=collections.defaultdict(list)
    for (i,j,k),wp in samples: d[(j,k)].append((i,wp[0]))
    for key,vals in d.items():
        vals.sort()
        if len(vals)>=2:
            step=(vals[-1][1]-vals[0][1])/(vals[-1][0]-vals[0][0])
            print(f"  inferred cell size along X = {step:.2f} m  (row j,k={key}, {len(vals)} cells)")
            break

print()
print("=== check 3: do MeshCollider meshes == lod0Meshes? ===")
lod0_meshpids={T(e["m_PathID"])["m_Mesh"]["m_PathID"] for e in isd["lod0Meshes"]}
colpids=set(); ncol=0
for o in env.objects:
    if str(o.type.name)=="MeshCollider":
        ncol+=1
        t=o.read_typetree()
        mp=t.get("m_Mesh",{}).get("m_PathID")
        if mp: colpids.add(mp)
print(f"  MeshColliders: {ncol}, distinct collider meshes: {len(colpids)}")
print(f"  collider meshes that ARE lod0Meshes: {len(colpids & lod0_meshpids)} / {len(colpids)}")

print()
print("=== check 4: spot-check absolute vertex positions of one cell ===")
e=isd["lod0Meshes"][0]; mf=T(e["m_PathID"])
mo=objmap[mf["m_Mesh"]["m_PathID"]]; mesh=mo.read()
h=MeshHandler(mesh); h.process()
wp,_=worldpos(mf["m_GameObject"]["m_PathID"])
vs=h.m_Vertices
xs=[v[0] for v in vs]; ys=[v[1] for v in vs]; zs=[v[2] for v in vs]
print(f"  mesh '{mo.read_typetree()['m_Name']}' verts={len(vs)}")
print(f"  LOCAL vertex range x[{min(xs):.1f},{max(xs):.1f}] y[{min(ys):.1f},{max(ys):.1f}] z[{min(zs):.1f},{max(zs):.1f}]")
print(f"  cell transform localPos chain sum = {tuple(round(v,2) for v in wp)}")
print(f"  => ISLAND-LOCAL x[{min(xs)+wp[0]:.1f},{max(xs)+wp[0]:.1f}] y[{min(ys)+wp[1]:.1f},{max(ys)+wp[1]:.1f}] z[{min(zs)+wp[2]:.1f},{max(zs)+wp[2]:.1f}]")
ns=h.m_Normals
print(f"  normals present: {ns is not None and len(ns)==len(vs)}; first normal {tuple(round(c,3) for c in ns[0][:3])}, |n|={sum(c*c for c in ns[0][:3])**0.5:.4f}")

print()
print("=== check 5: IslandMetaData ===")
for o in env.objects:
    if str(o.type.name)=="MonoBehaviour":
        t=o.read_typetree()
        if sname(t)=="IslandMetaData":
            print("  ",{k:v for k,v in t.items() if k not in ("m_Script","m_GameObject")})
