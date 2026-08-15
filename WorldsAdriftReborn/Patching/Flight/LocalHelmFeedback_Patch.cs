using System;
using System.Globalization;
using System.Reflection;
using Assets.Scripts.Visualisers.Ship;
using Bossa.DeadReckoning.Improbable;
using Bossa.Travellers.Controls;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Core;
using UnityEngine;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Restores retail's zero-round-trip helm animation for our separately
    /// registered helm entities.
    ///
    /// Retail ShipControlsBehaviour.UpdateHelm uses GetComponentInParent on the
    /// local player. That works when the controlled helm is in the same Unity
    /// hierarchy, but our mounted helm is its own SpatialOS entity following the
    /// hull. The lookup returns null, so the wheel waits for client 1111 -> server
    /// integration -> echoed helm 1111 before moving. At ordinary Internet RTT,
    /// the 240 ms ship cadence and HelmVisualizer's reader interpolation make
    /// that feel close to a second.
    ///
    /// PilotState already names the exact helm in ControlEntityId. After the
    /// HelmVisualizer has consumed its possibly stale server reader for this
    /// frame, reapply the local ShipControlsBehaviour values to that helm only.
    /// Ship movement remains server-authoritative; this predicts presentation,
    /// not physics. A later server echo converges to the same held input.
    /// </summary>
    [HarmonyPatch(typeof(HelmVisualizer), "Update")]
    internal static class LocalHelmFeedback_Patch
    {
        // Leave authoritative helm/hull presentation synchronized while the
        // latency trace measures the real path. This opt-in exists for A/B
        // testing only; it is deliberately not the shipped default.
        private static readonly bool PredictionEnabled =
            string.Equals(Environment.GetEnvironmentVariable("WAREBORN_LOCAL_HELM_PREDICTION"),
                "1", StringComparison.Ordinal);

        internal static readonly FieldInfo PilotField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_pilot");
        internal static readonly FieldInfo ThrottleField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_throttle");
        internal static readonly FieldInfo VerticalField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_vertical");
        internal static readonly FieldInfo AxesField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_axes");
        internal static readonly FieldInfo InputField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_input");
        internal static readonly FieldInfo TimeSinceSentField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_timeSinceSent");

        private static bool _loggedActive;
        private static bool _loggedFailure;

        private static bool Prepare()
        {
            bool ready = PilotField != null && ThrottleField != null
                && VerticalField != null && AxesField != null
                && InputField != null && TimeSinceSentField != null;
            if (!ready)
            {
                Debug.LogWarning("[WAR][flight] local helm feedback fields were not resolvable;"
                    + " prediction patch skipped.");
            }
            return ready;
        }

        private static void Postfix(HelmVisualizer __instance)
        {
            try
            {
                ShipControlsBehaviour controls = ShipControlsBehaviour.Instance;
                if (controls == null || __instance == null)
                {
                    return;
                }

                var pilot = PilotField.GetValue(controls) as PilotStateReader;
                if (pilot == null
                    || EntityId.IsInvalidEntityId(pilot.DrivingEntityId)
                    || EntityId.IsInvalidEntityId(pilot.ControlEntityId))
                {
                    return;
                }

                var helmEntity = global::Improbable.Unity.Core.SpatialOS.Universe.Get(pilot.ControlEntityId);
                if (helmEntity == null || helmEntity.UnderlyingGameObject == null)
                {
                    return;
                }

                HelmVisualizer controlledHelm =
                    helmEntity.UnderlyingGameObject.GetComponentInChildren<HelmVisualizer>(true);
                if (controlledHelm != __instance)
                {
                    return; // never predict another player's or another ship's helm
                }

                float throttle = (float)ThrottleField.GetValue(controls);
                float vertical = (float)VerticalField.GetValue(controls);
                Vector3 axes = (Vector3)AxesField.GetValue(controls);
                if (PredictionEnabled)
                {
                    __instance.SetState(throttle, vertical, axes.x, axes.y, axes.z);
                }
                FlightLatencyTrace.ObserveHelm(__instance, pilot, __instance.ShipAxesShow.y);

                if (PredictionEnabled && !_loggedActive)
                {
                    _loggedActive = true;
                    Debug.Log("[WAR][flight] local helm feedback is predicted directly from input;"
                        + " server echo remains authoritative for remote observers.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.LogWarning("[WAR][flight] local helm feedback prediction failed (once): "
                        + e.Message);
                }
            }
        }
    }

    /// <summary>
    /// Event-based latency trace for one local yaw transition. It deliberately
    /// logs only edges and the first downstream observation, never every frame.
    /// UTC permits correlation with the VPS journal; realtime/frame values expose
    /// client-main-thread stalls without trusting wall-clock synchronization.
    /// </summary>
    internal static class FlightLatencyTrace
    {
        private const float RawThreshold = 0.1f;
        private static int _lastRawDirection;
        private static long _sequence;
        private static string _eventId;
        private static float _inputRealtime;
        private static int _inputFrame;
        private static GameObject _hull;
        private static long _hullEntityId;
        private static float _baselineYaw;
        private static float _baselineHelmAxis;
        private static bool _sendLogged;
        private static bool _helmLogged;
        private static bool _pointLogged;
        private static bool _renderLogged;

        internal static void ObserveRawInput(ShipControlsBehaviour controls,
            PilotStateReader pilot, float rawYaw)
        {
            int direction = rawYaw > RawThreshold ? 1 : rawYaw < -RawThreshold ? -1 : 0;
            if (direction == _lastRawDirection)
            {
                return;
            }
            int previous = _lastRawDirection;
            _lastRawDirection = direction;

            if (direction == 0)
            {
                if (!string.IsNullOrEmpty(_eventId))
                {
                    Log("input-release", "rawYaw=" + rawYaw.ToString("0.###", CultureInfo.InvariantCulture));
                }
                return;
            }
            if (pilot == null || EntityId.IsInvalidEntityId(pilot.DrivingEntityId))
            {
                return;
            }

            var hullEntity = global::Improbable.Unity.Core.SpatialOS.Universe.Get(pilot.DrivingEntityId);
            if (hullEntity == null || hullEntity.UnderlyingGameObject == null)
            {
                return;
            }

            _sequence++;
            _eventId = "C" + _sequence.ToString(CultureInfo.InvariantCulture);
            _inputRealtime = Time.realtimeSinceStartup;
            _inputFrame = Time.frameCount;
            _hull = hullEntity.UnderlyingGameObject;
            _hullEntityId = pilot.DrivingEntityId.Id;
            _baselineYaw = _hull.transform.rotation.eulerAngles.y;
            _baselineHelmAxis = 0f;
            if (!EntityId.IsInvalidEntityId(pilot.ControlEntityId))
            {
                var helmEntity = global::Improbable.Unity.Core.SpatialOS.Universe.Get(pilot.ControlEntityId);
                HelmVisualizer helm = helmEntity?.UnderlyingGameObject?
                    .GetComponentInChildren<HelmVisualizer>(true);
                if (helm != null)
                {
                    _baselineHelmAxis = helm.ShipAxesShow.y;
                }
            }
            _sendLogged = false;
            _helmLogged = false;
            _pointLogged = false;
            _renderLogged = false;

            Log("input", "rawYaw=" + rawYaw.ToString("0.###", CultureInfo.InvariantCulture)
                + " direction=" + direction + " previous=" + previous
                + " baselineHullYaw=" + _baselineYaw.ToString("0.###", CultureInfo.InvariantCulture));
        }

        internal static void ObserveSend(ShipControlsBehaviour controls)
        {
            if (_sendLogged || string.IsNullOrEmpty(_eventId))
            {
                return;
            }
            float sinceSent = (float)LocalHelmFeedback_Patch.TimeSinceSentField.GetValue(controls);
            if (sinceSent > 0.0001f)
            {
                return;
            }
            _sendLogged = true;
            Vector3 axes = (Vector3)LocalHelmFeedback_Patch.AxesField.GetValue(controls);
            Log("1111-send", "axisYaw=" + axes.y.ToString("0.###", CultureInfo.InvariantCulture));
        }

        internal static void ObserveHelm(HelmVisualizer helm, PilotStateReader pilot, float axisYaw)
        {
            if (_helmLogged || string.IsNullOrEmpty(_eventId) || pilot == null
                || pilot.DrivingEntityId.Id != _hullEntityId)
            {
                return;
            }
            if (Mathf.Abs(axisYaw - _baselineHelmAxis) < 0.01f)
            {
                return;
            }
            _helmLogged = true;
            Log("helm-render", "axisYaw=" + axisYaw.ToString("0.###", CultureInfo.InvariantCulture));
        }

        internal static void ObserveControlPoint(MonoBehaviour visualizer)
        {
            if (_pointLogged || string.IsNullOrEmpty(_eventId) || !MatchesHull(visualizer))
            {
                return;
            }
            _pointLogged = true;
            Log("1130-receive", "firstControlPointAfterInput=true");
        }

        internal static void ObserveRenderedHull(PathFollower follower)
        {
            if (_renderLogged || string.IsNullOrEmpty(_eventId) || !MatchesHull(follower))
            {
                return;
            }
            float yaw = _hull.transform.rotation.eulerAngles.y;
            float delta = Mathf.Abs(Mathf.DeltaAngle(_baselineYaw, yaw));
            if (delta < 0.1f)
            {
                return;
            }
            _renderLogged = true;
            Log("hull-render", "yaw=" + yaw.ToString("0.###", CultureInfo.InvariantCulture)
                + " delta=" + delta.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static bool MatchesHull(MonoBehaviour behaviour)
        {
            return behaviour != null && _hull != null
                && (behaviour.gameObject == _hull || behaviour.transform.root == _hull.transform.root);
        }

        private static void Log(string phase, string detail)
        {
            float elapsedMs = (Time.realtimeSinceStartup - _inputRealtime) * 1000f;
            Debug.Log("[WAR][flight-latency] event=" + _eventId
                + " phase=" + phase
                + " utc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                + " realtimeMs=" + (Time.realtimeSinceStartup * 1000f).ToString("0.0", CultureInfo.InvariantCulture)
                + " elapsedMs=" + elapsedMs.ToString("0.0", CultureInfo.InvariantCulture)
                + " frame=" + Time.frameCount + " inputFrame=" + _inputFrame
                + " hull=" + _hullEntityId + " " + detail);
        }
    }

    [HarmonyPatch(typeof(ShipControlsBehaviour), "UpdateAxes")]
    internal static class HelmYawReversal_Patch
    {
        internal struct YawState
        {
            public float Before;
            public float Raw;
        }

        private static void Prefix(ShipControlsBehaviour __instance, out YawState __state)
        {
            Vector3 axes = (Vector3)LocalHelmFeedback_Patch.AxesField.GetValue(__instance);
            var input = LocalHelmFeedback_Patch.InputField.GetValue(__instance) as InputSink;
            __state = new YawState
            {
                Before = axes.y,
                Raw = input == null ? 0f : input.GetAxis(InputAxes.ShipYaw)
            };
        }

        private static void Postfix(ShipControlsBehaviour __instance, YawState __state)
        {
            Vector3 axes = (Vector3)LocalHelmFeedback_Patch.AxesField.GetValue(__instance);
            axes.y = HelmYawResponsePolicy.ApplyReversal(
                __state.Before, axes.y, __state.Raw, Time.deltaTime);
            LocalHelmFeedback_Patch.AxesField.SetValue(__instance, axes);
        }
    }

    [HarmonyPatch(typeof(ShipControlsBehaviour), "UpdateAxes")]
    internal static class FlightLatencyInput_Patch
    {
        private static void Prefix(ShipControlsBehaviour __instance)
        {
            var pilot = LocalHelmFeedback_Patch.PilotField.GetValue(__instance) as PilotStateReader;
            var input = LocalHelmFeedback_Patch.InputField.GetValue(__instance) as InputSink;
            if (pilot != null && input != null)
            {
                FlightLatencyTrace.ObserveRawInput(__instance, pilot, input.GetAxis(InputAxes.ShipYaw));
            }
        }
    }

    [HarmonyPatch(typeof(ShipControlsBehaviour), "SendData")]
    internal static class FlightLatencySend_Patch
    {
        private static void Postfix(ShipControlsBehaviour __instance)
        {
            FlightLatencyTrace.ObserveSend(__instance);
        }
    }

    [HarmonyPatch(typeof(SSPDeadReckoningVisualizer), "AddControlPoint")]
    internal static class FlightLatencyControlPoint_Patch
    {
        private static void Prefix(SSPDeadReckoningVisualizer __instance)
        {
            FlightLatencyTrace.ObserveControlPoint(__instance);
        }
    }

    [HarmonyPatch(typeof(PathFollower), "Move")]
    internal static class FlightLatencyHullRender_Patch
    {
        private static void Postfix(PathFollower __instance)
        {
            FlightLatencyTrace.ObserveRenderedHull(__instance);
        }
    }
}
