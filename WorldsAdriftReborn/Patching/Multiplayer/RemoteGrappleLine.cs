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

            // Without a material Unity draws the line with the magenta "missing
            // shader" fallback, and the default width of 1.0 makes it a giant
            // neon wedge. Give it a thin width and a plain unlit material so it
            // reads as a rope. Shader.Find works at runtime for always-included
            // built-in shaders; fall back through a couple of common names.
            Shader shader = Shader.Find("Sprites/Default")
                            ?? Shader.Find("Particles/Alpha Blended")
                            ?? Shader.Find("Diffuse");
            if (shader != null)
            {
                line.material = new Material(shader);
            }
            Color ropeColor = new Color(0.15f, 0.13f, 0.1f, 1f); // dark rope
            line.SetColors(ropeColor, ropeColor);
            line.SetWidth(0.04f, 0.04f);
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
