using System;
using System.Collections.Generic;
using Improbable;
using Improbable.Unity.Core;
using Improbable.Unity.Entity;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// GROUND-TRUTH orientation probe. Three rounds of server-side "the helm is at
    /// identity, therefore it renders correctly" have been contradicted by what the
    /// player actually sees (ghost preview and mounted helm do NOT match). This
    /// probe stops the argument by logging the RENDERED world rotations - the only
    /// data that describes the screen:
    ///
    ///   [WAR][orient] hull <id> yaw=..  helm <id> yaw=.. (local ..)  graphics yaw=..
    ///                 deckSpan X=..m Z=..m  player yaw=..
    ///
    /// every 5 s while a built hull + mounted helm are in the world. If the helm's
    /// rendered yaw differs from the hull's, the "~" follower is not applying the
    /// served local rotation - a client-side transform bug no byte inspection can
    /// see. If they match but the deck's long world span is perpendicular to the
    /// hull's forward, the geometry disagreement is real and named. Pure logging;
    /// touches nothing.
    /// </summary>
    internal sealed class OrientationProbe : MonoBehaviour
    {
        private float _nextAt;
        private readonly HashSet<long> _detailed = new HashSet<long>();

        private void Awake()
        {
            Debug.Log("[WAR][orient] probe armed: logs rendered hull/helm/deck orientation every 5s.");
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextAt)
            {
                return;
            }
            _nextAt = Time.realtimeSinceStartup + 5f;

            try
            {
                Report();
            }
            catch (Exception)
            {
                // pure diagnostics - never throw into the frame
            }
        }

        private static float Yaw(Quaternion q) => q.eulerAngles.y;

        private void Report()
        {
            var hulls = new List<KeyValuePair<long, GameObject>>();
            var helms = new List<KeyValuePair<long, GameObject>>();
            var entityRoots = new HashSet<Transform>();

            Improbable.Unity.Core.SpatialOS.Universe.IterateOverAllEntityObjects((id, ent) =>
            {
                GameObject go = ent?.UnderlyingGameObject;
                if (go == null) return;
                entityRoots.Add(go.transform);
                string n = go.name;
                if (hulls.Count < 4 && n.StartsWith("ShipFrame", StringComparison.OrdinalIgnoreCase))
                {
                    hulls.Add(new KeyValuePair<long, GameObject>(id.Id, go));
                }
                else if (helms.Count < 4 && n.StartsWith("Helm", StringComparison.OrdinalIgnoreCase))
                {
                    helms.Add(new KeyValuePair<long, GameObject>(id.Id, go));
                }
            });

            if (hulls.Count == 0 && helms.Count == 0)
            {
                return; // nothing relevant in the world yet
            }

            string line = "[WAR][orient]";
            foreach (var h in hulls)
            {
                GameObject hull = h.Value;
                Vector3 p = hull.transform.position;
                line += " | hull " + h.Key + " pos=(" + p.x.ToString("F0") + "," + p.z.ToString("F0")
                      + ") yaw=" + Yaw(hull.transform.rotation).ToString("F1");

                // Rendered hull footprint in HULL-LOCAL space (world bounds would
                // rotate with the ship's heading). Our server decode claims the
                // player's hull is X=12.09 x Z=8.00 - if the client draws it
                // Z-long instead, our decode is axis-swapped and every
                // "orientation is correct" conclusion inverts. Mounted parts are
                // Unity children of the hull but separate entities (the helm's
                // VfxNode alone reaches 37 m and poisoned the first measurement),
                // so any subtree rooted at another entity is skipped.
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                bool any = false;
                var stack = new Stack<Transform>();
                stack.Push(hull.transform);
                while (stack.Count > 0)
                {
                    Transform t = stack.Pop();
                    if (t != hull.transform && entityRoots.Contains(t))
                    {
                        continue; // another entity riding the hull - not hull geometry
                    }
                    var r = t.GetComponent<MeshRenderer>();
                    if (r != null)
                    {
                        Bounds wb = r.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 c = new Vector3(
                                (i & 1) == 0 ? wb.min.x : wb.max.x,
                                (i & 2) == 0 ? wb.min.y : wb.max.y,
                                (i & 4) == 0 ? wb.min.z : wb.max.z);
                            Vector3 l = hull.transform.InverseTransformPoint(c);
                            min = Vector3.Min(min, l); max = Vector3.Max(max, l);
                            any = true;
                        }
                    }
                    for (int i = 0; i < t.childCount; i++)
                    {
                        stack.Push(t.GetChild(i));
                    }
                }
                if (any)
                {
                    line += " localSpan X=" + (max.x - min.x).ToString("F1")
                          + "m Z=" + (max.z - min.z).ToString("F1") + "m";
                }

                // ONE-TIME per-renderer breakdown: the 26x34m spans are still
                // polluted (the ShipFrame entity also carries fixed scaffold
                // graphics), so name every mesh with its hull-local box once and
                // let the generated hull skin identify itself.
                if (_detailed.Add(h.Key))
                {
                    int printed = 0;
                    var walk = new Stack<Transform>();
                    walk.Push(hull.transform);
                    while (walk.Count > 0 && printed < 28)
                    {
                        Transform t = walk.Pop();
                        if (t != hull.transform && entityRoots.Contains(t)) continue;
                        var mr = t.GetComponent<MeshRenderer>();
                        if (mr != null)
                        {
                            Bounds wb = mr.bounds;
                            Vector3 lmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                            Vector3 lmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                            for (int i = 0; i < 8; i++)
                            {
                                Vector3 c = new Vector3(
                                    (i & 1) == 0 ? wb.min.x : wb.max.x,
                                    (i & 2) == 0 ? wb.min.y : wb.max.y,
                                    (i & 4) == 0 ? wb.min.z : wb.max.z);
                                Vector3 l = hull.transform.InverseTransformPoint(c);
                                lmin = Vector3.Min(lmin, l); lmax = Vector3.Max(lmax, l);
                            }
                            Vector3 ctr = (lmin + lmax) * 0.5f, sz = lmax - lmin;
                            string flag = t.GetComponent<ShipPanel>() != null || t.GetComponentInParent<ShipPanel>() != null
                                ? " PANEL" : "";
                            Debug.Log("[WAR][orient][detail] hull " + h.Key + " mesh '" + t.name
                                + "' ctr=(" + ctr.x.ToString("F1") + "," + ctr.y.ToString("F1") + "," + ctr.z.ToString("F1")
                                + ") size=(" + sz.x.ToString("F1") + "," + sz.y.ToString("F1") + "," + sz.z.ToString("F1") + ")" + flag);
                            printed++;
                        }
                        for (int i = 0; i < t.childCount; i++) walk.Push(t.GetChild(i));
                    }
                    Debug.Log("[WAR][orient][detail] hull " + h.Key + " breakdown complete (" + printed + " meshes shown)");
                }
            }
            foreach (var h in helms)
            {
                GameObject helm = h.Value;
                Vector3 p = helm.transform.position;
                line += " | helm " + h.Key + " (" + helm.name + ") pos=(" + p.x.ToString("F0") + "," + p.z.ToString("F0")
                      + ") yaw=" + Yaw(helm.transform.rotation).ToString("F1");
                if (hulls.Count > 0)
                {
                    // relative to the NEAREST hull - the one it is mounted on
                    GameObject nearest = null; float best = float.MaxValue;
                    foreach (var hh in hulls)
                    {
                        float d = (hh.Value.transform.position - p).sqrMagnitude;
                        if (d < best) { best = d; nearest = hh.Value; }
                    }
                    if (nearest != null)
                    {
                        line += " (rel " + Mathf.DeltaAngle(Yaw(nearest.transform.rotation), Yaw(helm.transform.rotation)).ToString("F1") + ")";
                    }
                }
                Transform gfx = helm.transform.Find("Graphics");
                if (gfx != null)
                {
                    line += " gfxLocalYaw=" + gfx.localEulerAngles.y.ToString("F1");
                }
            }

            var lp = LocalPlayer.Instance;
            if (lp != null && lp.playerGameObject != null)
            {
                Vector3 pp = lp.playerGameObject.transform.position;
                line += " | player pos=(" + pp.x.ToString("F0") + "," + pp.z.ToString("F0")
                      + ") yaw=" + Yaw(lp.playerGameObject.transform.rotation).ToString("F1");
            }

            Debug.Log(line);
        }
    }
}
