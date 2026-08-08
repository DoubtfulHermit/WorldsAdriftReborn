"""Read an island bundle and yield its LOD0 terrain surface in ISLAND-LOCAL space.

The one interesting thing this does is compose a full TRS chain (see
``unity_transform`` for the conventions and how they were confirmed) instead of
summing ``m_LocalPosition``.  On every island bundle inspected the LOD0 cell
GameObject carries ``m_LocalScale = (4,4,4)``, which the old code dropped.
"""
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

from unity_transform import (IDENTITY4, apply3, mat_mul, normal_matrix,
                             normalize, transform_point, trs)


class IslandBundle:
    def __init__(self, path):
        self.path = path
        self.env = UnityPy.load(path)
        self.objmap = {o.path_id: o for o in self.env.objects}
        self.tf_of_go = {}      # GameObject pathID -> Transform typetree
        self.go_name = {}       # GameObject pathID -> name
        self.scripts = set()
        self._wm_cache = {}
        self.surface_data = None
        self.meta = None
        for o in self.env.objects:
            tn = str(o.type.name)
            if tn in ("Transform", "RectTransform"):
                t = o.read_typetree()
                self.tf_of_go[t["m_GameObject"]["m_PathID"]] = t
            elif tn == "GameObject":
                self.go_name[o.path_id] = o.read_typetree().get("m_Name")
            elif tn == "MonoBehaviour":
                try:
                    t = o.read_typetree()
                except Exception:
                    continue
                n = self._script_name(t)
                self.scripts.add(n)
                if n == "IslandSurfaceData":
                    self.surface_data = t
                elif n == "IslandMetaData":
                    self.meta = t

    # ------------------------------------------------------------ helpers
    def _script_name(self, t):
        o = self.objmap.get(t.get("m_Script", {}).get("m_PathID"))
        if not o:
            return "?"
        d = o.read_typetree()
        ns = d.get("m_Namespace") or ""
        return (ns + "." if ns else "") + str(d.get("m_ClassName") or "?")

    def tree(self, pid):
        o = self.objmap.get(pid)
        return o.read_typetree() if o else None

    def chain(self, go_pid):
        """[(go_pid, transform typetree)] from the GameObject up to the root."""
        out = []
        cur = go_pid
        seen = set()
        while cur is not None and cur in self.tf_of_go and cur not in seen:
            seen.add(cur)
            t = self.tf_of_go[cur]
            out.append((cur, t))
            fa = t.get("m_Father", {}).get("m_PathID")
            if not fa or fa not in self.objmap:
                break
            cur = self.objmap[fa].read_typetree()["m_GameObject"]["m_PathID"]
        return out

    def world_matrix(self, go_pid):
        """localToWorldMatrix of a GameObject, relative to the bundle root.

        world = parent_world * TRS(localPosition, localRotation, localScale).
        Built root-down, so the multiplication order is literally that.
        """
        hit = self._wm_cache.get(go_pid)
        if hit is not None:
            return hit
        ch = self.chain(go_pid)
        m = IDENTITY4
        for pid, t in reversed(ch):           # root first
            cached = self._wm_cache.get(pid)
            if cached is not None:
                m = cached
                continue
            lp, lq, ls = t["m_LocalPosition"], t["m_LocalRotation"], t["m_LocalScale"]
            local = trs((lp["x"], lp["y"], lp["z"]),
                        (lq["x"], lq["y"], lq["z"], lq["w"]),
                        (ls["x"], ls["y"], ls["z"]))
            m = mat_mul(m, local)
            self._wm_cache[pid] = m
        self._wm_cache[go_pid] = m
        return m

    # ------------------------------------------------------------ surface
    def lod0_cells(self):
        """[(mesh_filter_typetree, mesh_object)] for every LOD0 terrain cell."""
        out = []
        for e in (self.surface_data or {}).get("lod0Meshes", []):
            mfo = self.objmap.get(e["m_PathID"])
            if not mfo:
                continue
            mf = mfo.read_typetree()
            mo = self.objmap.get(mf["m_Mesh"]["m_PathID"])
            if not mo:
                continue
            out.append((mf, mo))
        return out

    def iter_surface_vertices(self):
        """Yield (x, y, z, nx, ny, nz) in island-local space, fully TRS-composed."""
        for mf, mo in self.lod0_cells():
            h = MeshHandler(mo.read())
            h.process()
            vs = h.m_Vertices or []
            ns = h.m_Normals or []
            if len(ns) != len(vs):
                ns = [(0.0, 1.0, 0.0)] * len(vs)
            m = self.world_matrix(mf["m_GameObject"]["m_PathID"])
            nm = normal_matrix(m)
            for k in range(len(vs)):
                p = transform_point(m, vs[k])
                n = normalize(apply3(nm, ns[k]))
                yield (p[0], p[1], p[2], n[0], n[1], n[2])
