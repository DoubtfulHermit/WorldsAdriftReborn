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
            GameObject hull = null;
            GameObject helm = null;
            long hullId = 0, helmId = 0;

            Improbable.Unity.Core.SpatialOS.Universe.IterateOverAllEntityObjects((id, ent) =>
            {
                GameObject go = ent?.UnderlyingGameObject;
                if (go == null) return;
                string n = go.name;
                if (hull == null && n.StartsWith("ShipFrame", StringComparison.OrdinalIgnoreCase))
                {
                    hull = go; hullId = id.Id;
                }
                else if (helm == null && n.StartsWith("Helm", StringComparison.OrdinalIgnoreCase))
                {
                    helm = go; helmId = id.Id;
                }
            });

            if (hull == null && helm == null)
            {
                return; // nothing relevant in the world yet
            }

            string line = "[WAR][orient]";
            if (hull != null)
            {
                line += " hull " + hullId + " yaw=" + Yaw(hull.transform.rotation).ToString("F1");

                // Rendered hull footprint in HULL-LOCAL space (world bounds would
                // rotate with the ship's heading). This is the number our server
                // decode claims is X=12.09 x Z=8.00 - if the client draws it
                // Z-long instead, our decode is axis-swapped and every
                // "orientation is correct" conclusion inverts.
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                bool any = false;
                foreach (Renderer r in hull.GetComponentsInChildren<Renderer>())
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
                if (any)
                {
                    line += " localSpan X=" + (max.x - min.x).ToString("F1")
                          + "m Z=" + (max.z - min.z).ToString("F1") + "m";
                }
            }
            if (helm != null)
            {
                line += " | helm " + helmId + " yaw=" + Yaw(helm.transform.rotation).ToString("F1");
                if (hull != null)
                {
                    line += " (rel " + Mathf.DeltaAngle(Yaw(hull.transform.rotation), Yaw(helm.transform.rotation)).ToString("F1") + ")";
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
                line += " | player yaw=" + Yaw(lp.playerGameObject.transform.rotation).ToString("F1");
            }

            Debug.Log(line);
        }
    }
}
