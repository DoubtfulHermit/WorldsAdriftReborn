using System;
using Bossa.Travellers.Rope;
using Improbable;
using Improbable.CoreLibrary.CoordinateRemapping;
using Improbable.Collections;
using Improbable.Entity.Component;
using Improbable.Math;
using Improbable.Worker;
using Improbable.Worker.Internal;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * Draws a remote player's grapple rope. The plain "Default" rig carries no
     * rope visualizer, so seeding 1098 RopeControlPoints alone renders nothing:
     * there is no component instance to inject a reader into, and runtime-added
     * components never get SDK [Require] injection.
     *
     * Instead this reads 1098 BY COMPONENT ID through the SDK, which needs no
     * [Require]: ComponentDatabase.IdToMetaclass(1098) -> IComponentFactory ->
     * GetComponentForEntity(thisEntityId) returns the per-entity Impl, which IS
     * the RopeControlPointsReader (its PointsUpdated event fires with current data
     * on subscribe). The Impl exists because the mirror seeded 1098, which sends
     * the AddComponent that populates the factory's per-entity map.
     *
     * Drawing mirrors the game's own (obsolete, absent) SimpleGrapplingHookVisualizer.
     */
    internal class RemoteGrappleLine : MonoBehaviour
    {
        private RopeControlPointsReader reader;
        private LineRenderer line;
        private bool subscribed;

        private void Update()
        {
            if (reader != null)
            {
                // Hide the game's grapple tube EVERY frame, unconditionally - NOT
                // only while our rope line is up. The tube doubles as an AIM
                // visual shown while the player is merely aiming (before the rope
                // attaches, so RopeControlPoints has no points and our line is
                // disabled) - that is the "raycast to the crosshair" the observer
                // saw. It is driven only by local logic, so it is always wrong on
                // a remote rig; our LineRenderer is the only rope the observer
                // needs.
                HideHookTube();
                return;
            }

            // gameObject.EntityId() throws before the entity is registered; guard.
            EntityId entityId;
            try
            {
                entityId = gameObject.EntityId();
            }
            catch
            {
                return;
            }

            IComponentMetaclass metaclass = ComponentDatabase.IdToMetaclass(1098u);
            IComponentFactory factory = metaclass as IComponentFactory;
            if (factory == null)
            {
                return; // metaclass map not populated yet
            }

            reader = factory.GetComponentForEntity(entityId) as RopeControlPointsReader;
            if (reader == null)
            {
                return; // component not received on this entity yet; retry next frame
            }

            EnsureLine();
            reader.PointsUpdated += OnPointsUpdated;
            subscribed = true;
            // Draw whatever is already there.
            OnPointsUpdated(reader.Points);
            Debug.Log("[WAReborn] RemoteGrappleLine bound on '" + transform.root.name + "'.");
        }

        private void EnsureLine()
        {
            if (line != null)
            {
                return;
            }
            GameObject go = new GameObject("WAReborn_RemoteGrappleLine");
            go.transform.SetParent(transform.root, false);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.enabled = false;

            ApplyLineStyle();
        }

        private bool diagLogged;

        /// <summary>
        /// Width + material for the rope. Called on create AND on every update:
        /// a LineRenderer left at its default 1.0 width renders as a fat tapered
        /// wedge in perspective (thick near the camera, narrowing with distance),
        /// which is exactly the "raycast" artefact the observer reported.
        /// </summary>
        private void ApplyLineStyle()
        {
            if (line == null)
            {
                return;
            }

            if (line.material == null || line.material.shader == null)
            {
                Shader shader = Shader.Find("Sprites/Default")
                                ?? Shader.Find("Particles/Alpha Blended")
                                ?? Shader.Find("Unlit/Color")
                                ?? Shader.Find("Diffuse");
                if (shader != null)
                {
                    line.material = new Material(shader);
                }
            }

            Color ropeColor = new Color(0.12f, 0.10f, 0.08f, 1f); // dark rope
            line.SetColors(ropeColor, ropeColor);
            line.SetWidth(0.015f, 0.015f);
        }

        private void OnPointsUpdated(List<Coordinates> points)
        {
            if (line == null)
            {
                return;
            }
            if (points == null || points.Count == 0)
            {
                line.enabled = false;
                return;
            }

            line.enabled = true;
            line.SetVertexCount(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                line.SetPosition(i, points[i].RemapGlobalToUnityVector());
            }

            // Re-assert width/material every update: something (default state or a
            // re-init) can leave the LineRenderer at its default 1.0 width, which
            // renders as a fat tapered wedge in perspective.
            ApplyLineStyle();

            if (!diagLogged)
            {
                diagLogged = true;
                Debug.Log("[WAReborn] LINE DIAG width=" + line.widthMultiplier
                          + " startW=" + line.startWidth + " endW=" + line.endWidth
                          + " mat=" + (line.material != null ? line.material.shader.name : "NULL")
                          + " pts=" + points.Count);
                foreach (Renderer rd in transform.root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!rd.enabled) continue;
                    Vector3 sz = rd.bounds.size;
                    // A stretched wedge mesh has one huge bounds axis; flag anything big.
                    if (sz.magnitude > 3f)
                    {
                        Debug.Log("[WAReborn] LINE DIAG big renderer '" + rd.gameObject.name
                                  + "' (" + rd.GetType().Name + ") bounds=" + sz
                                  + " under '" + rd.transform.parent?.name + "'");
                    }
                }
            }

            // The game's own GrapplingHookTube mesh is driven only by local grapple
            // logic (which never runs on a remote rig), so on the observer it
            // renders as a static mis-stretched wedge - the "raycast" the rope our
            // LineRenderer draws replaces. Keep it hidden while the rope is up.
            HideHookTube();
        }

        private bool loggedHide;

        private void HideHookTube()
        {
            // Re-scan each call: the tube may not exist until the grapple fires,
            // and the game can re-enable it. Cheap - grappling is brief/intermittent.
            foreach (MeshRenderer mr in transform.root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null && mr.enabled
                    && mr.gameObject.name.IndexOf("Tube", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mr.enabled = false;
                    if (!loggedHide)
                    {
                        loggedHide = true;
                        Debug.Log("[WAReborn] hid remote grapple tube '" + mr.gameObject.name + "'");
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (subscribed && reader != null)
            {
                try { reader.PointsUpdated -= OnPointsUpdated; } catch { }
            }
        }
    }
}
