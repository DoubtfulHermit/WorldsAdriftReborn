using System.Text;
using Bossa.Travellers.Visualisers.Islands;
using HarmonyLib;
using ImposterSystem;
using UnityEngine;
using WorldsAdriftReborn.Config;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftReborn.Patching.InGameChanges
{
    /// <summary>
    /// Stops distant islands appearing to turn towards the player as the player
    /// moves.
    ///
    /// The retail impostor system is the thing we want; it is only misconfigured
    /// for the way this deployment loads the world. Retail replaces an island's
    /// two furthest LOD levels with a runtime-baked billboard
    /// (<c>IslandVisualiser.SetupIslandImpostors</c>) and then runs it with two
    /// angles twelve times apart: the quad keeps rotating to face the viewer
    /// until it is 30 degrees off the direction its texture was baked from
    /// (<c>ImpostersHandler.minAngleToStopLookAtCamera</c>), but the texture is
    /// only re-rendered once the viewer has moved 2.5 degrees around the island
    /// (<c>ImposterController.errorCameraAngle</c>). While the re-bake keeps up,
    /// the 30 is unreachable. When it does not - the request is queued in
    /// <c>ImpostersHandler.queueOfImposters</c> and drained at
    /// <c>maxUpdatesPerFrame</c> (20) bakes per frame across every impostor in
    /// the scene, each bake being two full camera renders - the island carries on
    /// swinging while wearing a stale silhouette. See
    /// <see cref="ImpostorBillboardPolicy"/> for the rule and its tests.
    ///
    /// This does NOT disable impostors and does not add bakes. It clamps the
    /// billboard's follow tolerance to the re-bake trigger, which is a no-op in
    /// every frame where the bake queue is keeping up.
    /// </summary>
    [HarmonyPatch(typeof(IslandVisualiser), "SetupIslandImpostors")]
    internal static class IslandImpostorSwing
    {
        private static int configuredIslands;
        private static ImposterController sample;
        private static float desiredFollowAngle = -1f;

        internal static int ConfiguredIslands { get { return configuredIslands; } }

        /// <summary>The most recently configured island controller, or null.</summary>
        internal static ImposterController Sample { get { return sample; } }

        [HarmonyPostfix]
        private static void Configure(IslandVisualiser __instance)
        {
            try
            {
                ImposterController controller =
                    __instance.GetComponentInChildren<ImposterController>(true);
                // SetupIslandImpostors early-returns without a controller when the
                // island prefab has no LODGroup; it logs that itself.
                if (controller == null) return;

                ImpostorBillboardSettings retail = new ImpostorBillboardSettings();
                retail.RebakeAngleDegrees = controller.errorCameraAngle;
                retail.RebakeSeconds = controller.timeInterval;
                retail.RebakeOnTime = controller.useUpdateByTime;
                retail.FollowAngleDegrees = ImpostorBillboardPolicy.RetailFollowAngleDegrees;

                ImpostersHandler handler = Object.FindObjectOfType<ImpostersHandler>();
                if (handler != null) retail.FollowAngleDegrees = handler.minAngleToStopLookAtCamera;

                ImpostorBillboardSettings corrected = ImpostorBillboardPolicy.Correct(
                    retail,
                    ModSettings.impostorFollowAngleDegrees.Value,
                    ModSettings.impostorRebakeAngleDegrees.Value,
                    ModSettings.impostorRebakeSeconds.Value);

                controller.errorCameraAngle = corrected.RebakeAngleDegrees;
                controller.timeInterval = corrected.RebakeSeconds;
                // Recorded whether or not the handler exists yet, so the probe can
                // apply it when the world scene brings one up.
                desiredFollowAngle = corrected.FollowAngleDegrees;
                ApplyFollowAngle(handler);

                configuredIslands++;
                sample = controller;
                Report(__instance, controller, handler, retail, corrected);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WAReborn][impostor] could not configure island impostor: " + e);
            }
        }

        /// <summary>
        /// The follow angle is ONE global on the handler, shared with ship
        /// impostors (<c>ShipImposter.InitShipImposter</c> builds the same kind of
        /// controller), and the handler pushes it into the shader as
        /// <c>_ImposterSystem_MinAngleToStopLookAtCamera</c> every LateUpdate. It
        /// is re-asserted rather than written once because the handler is
        /// scene-serialized and the debug console's `imposter` command toggles it
        /// off and on.
        /// </summary>
        internal static void ApplyFollowAngle(ImpostersHandler handler)
        {
            if (handler == null || desiredFollowAngle <= 0f) return;
            if (handler.minAngleToStopLookAtCamera == desiredFollowAngle) return;
            handler.minAngleToStopLookAtCamera = desiredFollowAngle;
        }

        /// <summary>The follow angle the policy asked for, or -1 before any island is set up.</summary>
        internal static float DesiredFollowAngle { get { return desiredFollowAngle; } }

        /// <summary>
        /// Reports what the retail components ACTUALLY hold, once. A previous
        /// round of island work cost a full round trip because client state was
        /// being inferred from screenshots; every number below is read back off
        /// the live components rather than assumed from the decompile.
        /// </summary>
        private static void Report(
            IslandVisualiser island,
            ImposterController controller,
            ImpostersHandler handler,
            ImpostorBillboardSettings retail,
            ImpostorBillboardSettings corrected)
        {
            if (configuredIslands != 1) return;

            StringBuilder text = new StringBuilder(768);
            text.Append("[WAReborn][impostor] island impostor configured (first of session): ");
            text.Append("island=").Append(island.PrefabName);
            text.Append(" updateBehavior=").Append(controller.updateBehavior);
            text.Append(" useUpdateByTime=").Append(controller.useUpdateByTime);
            text.Append(" timeInterval=").Append(retail.RebakeSeconds)
                .Append("->").Append(corrected.RebakeSeconds);
            text.Append(" errorCameraAngle=").Append(retail.RebakeAngleDegrees)
                .Append("->").Append(corrected.RebakeAngleDegrees);
            text.Append(" errorDistance=").Append(controller.errorDistance);
            text.Append(" useErrorLightAngle=").Append(controller.useErrorLightAngle);
            text.Append(" alwaysLookAtCamera=").Append(controller.alwaysLookAtCamera);
            text.Append(" ZOffset=").Append(controller.ZOffset);

            ImposterLOD[] lods = controller.m_LODs;
            text.Append(" LODs=").Append(lods == null ? 0 : lods.Length).Append('[');
            if (lods != null)
            {
                for (int i = 0; i < lods.Length; i++)
                {
                    if (i > 0) text.Append(' ');
                    text.Append(i).Append(':');
                    text.Append(lods[i].isImposter ? "IMPOSTOR" : "mesh");
                    text.Append("@").Append(lods[i].screenRelativeTransitionHeight.ToString("0.####"));
                    text.Append("/r").Append(lods[i].OriginalGoControllerSystem == null
                        ? 0 : lods[i].OriginalGoControllerSystem.RendererStates.Count);
                    text.Append("/res").Append((int)lods[i].minImposterResolution)
                        .Append('-').Append((int)lods[i].maxImposterResolution);
                }
            }
            text.Append(']');

            // Bounds are still the pre-RecalculateBounds defaults here;
            // IslandVisualiser.DelayedShowImposter recomputes them. The probe
            // reports the settled values.
            if (handler == null)
            {
                text.Append(" handler=<none found>");
            }
            else
            {
                text.Append(" handler.minAngleToStopLookAtCamera=").Append(retail.FollowAngleDegrees)
                    .Append("->").Append(corrected.FollowAngleDegrees);
                text.Append(" handler.maxUpdatesPerFrame=").Append(handler.maxUpdatesPerFrame);
                text.Append(" handler.disableImpostersUpdating=").Append(handler.disableImpostersUpdating);
                text.Append(" handler.enabled=").Append(handler.enabled);
                text.Append(" handler.useFading=").Append(handler.useFading);
                text.Append(" handler.invisibleAction=").Append(handler._invisibleImposterAction);
                text.Append(" handler.shadowCasting=").Append(handler.shadowCastingEnabled);
                text.Append(" handler.preloadFactor=").Append(handler.preloadFactor);
                text.Append(" handler.light=").Append(handler.imposterLight == null ? "null" : "set");
            }
            text.Append(" swing: retail steady=")
                .Append(ImpostorBillboardPolicy.SteadyStateSwingDegrees(retail))
                .Append("deg stale=").Append(ImpostorBillboardPolicy.StaleSwingDegrees(retail))
                .Append("deg -> now stale=")
                .Append(ImpostorBillboardPolicy.StaleSwingDegrees(corrected))
                .Append("deg bounded=").Append(ImpostorBillboardPolicy.IsSwingBounded(corrected));

            Debug.Log(text.ToString());
        }
    }
}
