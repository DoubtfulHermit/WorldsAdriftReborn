using System.Reflection;
using System.Text;
using HarmonyLib;
using ImposterSystem;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.InGameChanges
{
    /// <summary>
    /// Reports whether the retail impostor bake queue is keeping up, because
    /// that is the one fact that separates the two explanations for islands
    /// appearing to swing towards the player and it cannot be read from the
    /// decompile.
    ///
    /// <c>ImpostersHandler</c> drains <c>queueOfImposters</c> in LateUpdate at
    /// <c>maxUpdatesPerFrame</c> bakes per frame, shared across every impostor
    /// and camera; each bake is two full camera renders of the object. If the
    /// backlog below is persistently non-zero, island billboards are rendering
    /// stale silhouettes for many frames at a time and the swing is a starvation
    /// symptom. If it sits at zero and bakes/s tracks movement, the bake
    /// pipeline is healthy and the swing is purely the follow-angle tolerance
    /// that <see cref="IslandImpostorSwing"/> clamps.
    ///
    /// Read-only apart from re-asserting the follow angle, which the handler
    /// pushes to the shader every LateUpdate and which the debug console's
    /// `imposter` toggle can reset by disabling and re-enabling the handler.
    /// </summary>
    internal sealed class IslandImpostorProbe : MonoBehaviour
    {
        private const float ReportIntervalSeconds = 30f;

        private static readonly FieldInfo IsStaticField =
            AccessTools.Field(typeof(Renderable), "_isStatic");
        private static readonly FieldInfo RenderTypeField =
            AccessTools.Field(typeof(ImpostersHandler), "_impostersRenderType");
        private static readonly FieldInfo QueueTypeField =
            AccessTools.Field(typeof(ImpostersHandler), "queueType");

        private ImpostersHandler handler;
        private float nextReportAt;
        private int bakesThisWindow;
        private int maxBacklogThisWindow;
        private bool reportedBounds;

        private void Start()
        {
            nextReportAt = Time.unscaledTime + ReportIntervalSeconds;
        }

        private void LateUpdate()
        {
            if (handler == null)
            {
                // Cheap enough at this cadence, and the handler only appears once
                // the world scene is up - long after the plugin's Awake.
                if (Time.unscaledTime < nextReportAt) return;
                handler = Object.FindObjectOfType<ImpostersHandler>();
                nextReportAt = Time.unscaledTime + ReportIntervalSeconds;
                if (handler == null) return;
            }

            bakesThisWindow += handler.updatedByFrameImpostersCount;
            int backlog = handler.currentImpostersCount;
            if (backlog > maxBacklogThisWindow) maxBacklogThisWindow = backlog;

            // Re-assert rather than assume: this is a single global the game
            // itself can reset, and the handler may not have existed when the
            // first island was configured.
            IslandImpostorSwing.ApplyFollowAngle(handler);

            if (Time.unscaledTime < nextReportAt) return;
            nextReportAt = Time.unscaledTime + ReportIntervalSeconds;
            Report();
            bakesThisWindow = 0;
            maxBacklogThisWindow = 0;
        }

        private void Report()
        {
            StringBuilder text = new StringBuilder(384);
            text.Append("[WAReborn][impostor] ")
                .Append((bakesThisWindow / ReportIntervalSeconds).ToString("0.0"))
                .Append(" bakes/s over ").Append((int)ReportIntervalSeconds)
                .Append("s, peak queue backlog=").Append(maxBacklogThisWindow)
                .Append("/").Append(handler.maxUpdatesPerFrame).Append(" per frame");
            text.Append(", islands configured=").Append(IslandImpostorSwing.ConfiguredIslands);
            text.Append(", followAngle=").Append(handler.minAngleToStopLookAtCamera);
            text.Append(", updating=").Append(!handler.disableImpostersUpdating);
            if (RenderTypeField != null)
                text.Append(", renderType=").Append(RenderTypeField.GetValue(handler));
            if (QueueTypeField != null)
                text.Append(", queueType=").Append(QueueTypeField.GetValue(handler));

            ImposterController sample = IslandImpostorSwing.Sample;
            if (sample != null && !reportedBounds && sample.quadSize > 0f)
            {
                // Only meaningful after IslandVisualiser.DelayedShowImposter has
                // run RecalculateBounds. A quadSize that never leaves zero, or a
                // centre far from the terrain, would put the billboard's pivot in
                // the wrong place - which swings too, for a different reason.
                reportedBounds = true;
                text.Append("; sample island quadSize=").Append(sample.quadSize.ToString("0.0"));
                text.Append(" boundsSize=").Append(sample.size.ToString("0.0"));
                text.Append(" centreOffset=").Append(sample.center.ToString("0.0"));
                text.Append(" pos=").Append(sample.transform.position.ToString("0.0"));
                if (IsStaticField != null)
                    text.Append(" isStatic=").Append(IsStaticField.GetValue(sample));
                text.Append(" errorCameraAngle=").Append(sample.errorCameraAngle);
                text.Append(" timeInterval=").Append(sample.timeInterval);
            }

            Debug.Log(text.ToString());
        }
    }
}
