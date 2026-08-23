using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Assets.Scripts.Visualisers.Ship;
using Assets.Visualizers;
using Bossa.Travellers.Controls;
using Bossa.Travellers.Interact;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Core;
using Travellers.UI.Login;
using UnityEngine;
using LandingScreenType = Travellers.UI.Login.LandingScreen;

namespace WorldsAdriftReborn.Patching.Automation
{
    /// <summary>
    /// Opt-in, loopback-only control seam for visual acceptance automation.
    ///
    /// The bridge deliberately translates semantic commands on Unity's main
    /// thread instead of injecting desktop input through Wayland/Wine. It is
    /// inert unless the environment opt-in or an owner-created one-shot token
    /// file supplies a non-empty per-run token. Normal players therefore do
    /// not open a listener.
    /// </summary>
    internal sealed class LocalTestBridge : MonoBehaviour
    {
        private const int DefaultPort = 47631;
        private const int MaxLineLength = 512;
        private const int MaxCommandsPerFrame = 8;
        private const float MaxPulseSeconds = 10f;
        private const string OneShotTokenFileName = ".wareborn-test-bridge-token";
        private const int MaxInteractionTargets = 24;
        private const float InteractionReleaseMarginSeconds = 0.1f;

        private static string _startupToken;

        private static readonly Type PlayerLookingAtType =
            AccessTools.TypeByName("Assets.Scripts.Player.PlayerLookingAt");
        private static readonly PropertyInfo PlayerLookingAtInstance =
            PlayerLookingAtType == null ? null : AccessTools.Property(PlayerLookingAtType, "Instance");
        private static readonly PropertyInfo LookingAtInteractive =
            PlayerLookingAtType == null ? null : AccessTools.Property(PlayerLookingAtType, "LookingAtInteractive");
        private static readonly PropertyInfo LookingAtCollider =
            PlayerLookingAtType == null ? null : AccessTools.Property(PlayerLookingAtType, "LookingAtCollider");
        private static readonly MethodInfo CheckInteractionMethod =
            AccessTools.Method(typeof(Bossa.Prototype.Character.Observer.InteractAgentObserver),
                "CheckInteraction");
        private static readonly FieldInfo InteractionTotalTimeField =
            AccessTools.Field(typeof(TimedInteractionController), "_interactionTotalTime");
        private static readonly Type PilotVisualizerType =
            AccessTools.TypeByName("PilotVisualizer");
        private static readonly MethodInfo PilotIsDrivingMethod =
            PilotVisualizerType == null ? null : AccessTools.Method(PilotVisualizerType, "IsDriving");

        private readonly Queue<PendingCommand> _pending = new Queue<PendingCommand>();
        private TcpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;
        private string _token;

        internal static bool ShouldStart()
        {
            string environmentToken = Environment.GetEnvironmentVariable(
                "WAREBORN_TEST_BRIDGE_TOKEN");
            if (string.Equals(Environment.GetEnvironmentVariable("WAREBORN_TEST_BRIDGE"),
                    "1", StringComparison.Ordinal)
                && IsValidToken(environmentToken))
            {
                _startupToken = environmentToken;
                return true;
            }

            // Wine can reparent/re-exec the Unity process and, on some launch
            // paths, strip non-Wine environment variables. A root-owned test
            // orchestrator may therefore place one 0600 token beside the exe.
            // Presence is the opt-in. Consume it before opening the listener so
            // a later ordinary launch cannot inherit automation accidentally.
            string path = Path.Combine(Environment.CurrentDirectory, OneShotTokenFileName);
            try
            {
                FileInfo file = new FileInfo(path);
                if (!file.Exists)
                    return false;

                string token = file.Length > 0 && file.Length <= MaxLineLength
                    ? File.ReadAllText(path).Trim()
                    : null;
                File.Delete(path);
                if (!IsValidToken(token))
                {
                    Debug.LogError("[WAR][test-bridge] invalid one-shot token file consumed; disabled.");
                    return false;
                }
                _startupToken = token;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[WAR][test-bridge] could not consume one-shot token; disabled: "
                    + e.GetType().Name);
                return false;
            }
        }

        private void Awake()
        {
            _token = _startupToken;
            _startupToken = null;
            if (!IsValidToken(_token))
            {
                Debug.LogError("[WAR][test-bridge] requested without WAREBORN_TEST_BRIDGE_TOKEN; disabled.");
                Destroy(this);
                return;
            }

            int port = ParsePort(Environment.GetEnvironmentVariable("WAREBORN_TEST_BRIDGE_PORT"));
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start(1);
                SyntheticInput.Enable();
                _running = true;
                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "WAReborn local test bridge"
                };
                _listenerThread.Start();
                Debug.Log("[WAR][test-bridge] listening on 127.0.0.1:" + port
                    + " (semantic commands; token required; local test mode only).");
            }
            catch (Exception e)
            {
                Debug.LogError("[WAR][test-bridge] failed to start: " + e);
                StopBridge();
                Destroy(this);
            }
        }

        private void Update()
        {
            SyntheticInput.Tick();
            for (int i = 0; i < MaxCommandsPerFrame; i++)
            {
                PendingCommand command;
                lock (_pending)
                {
                    if (_pending.Count == 0)
                        return;
                    command = _pending.Dequeue();
                }

                try
                {
                    if (command.Cancelled)
                    {
                        command.Response = Error("cancelled", "request timed out before execution");
                        continue;
                    }
                    command.Response = Execute(command.Command);
                }
                catch (Exception e)
                {
                    command.Response = Error("command_failed", e.GetType().Name + ": " + e.Message);
                }
                finally
                {
                    command.Completed.Set();
                }
            }
        }

        private string Execute(string command)
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return Error("bad_request", "command is empty");

            if (parts[0] == "input.tap" && parts.Length == 2)
            {
                InputButtons button;
                if (!TryParseEnum(parts[1], out button))
                    return Error("bad_button", parts[1]);
                SyntheticInput.Tap(button);
                return Ok(command);
            }
            if (parts[0] == "input.hold" && parts.Length == 3)
            {
                InputButtons button;
                bool held;
                if (!TryParseEnum(parts[1], out button))
                    return Error("bad_button", parts[1]);
                if (!bool.TryParse(parts[2], out held))
                    return Error("bad_value", parts[2]);
                SyntheticInput.Hold(button, held);
                return Ok(command);
            }
            if (parts[0] == "input.pulse" && parts.Length == 3)
            {
                InputButtons button;
                float seconds;
                if (!TryParseEnum(parts[1], out button))
                    return Error("bad_button", parts[1]);
                if (!TryParsePulseSeconds(parts[2], out seconds))
                    return Error("bad_duration", parts[2]);
                SyntheticInput.Pulse(button, seconds);
                return Ok(command);
            }
            if (parts[0] == "axis.set" && parts.Length == 3)
            {
                InputAxes axis;
                float value;
                if (!TryParseEnum(parts[1], out axis))
                    return Error("bad_axis", parts[1]);
                if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    || float.IsNaN(value) || float.IsInfinity(value) || value < -1f || value > 1f)
                    return Error("bad_value", parts[2]);
                SyntheticInput.SetAxis(axis, value);
                return Ok(command);
            }
            if (parts[0] == "axis.pulse" && parts.Length == 4)
            {
                InputAxes axis;
                float value;
                float seconds;
                if (!TryParseEnum(parts[1], out axis))
                    return Error("bad_axis", parts[1]);
                if (!TryParseUnitValue(parts[2], out value))
                    return Error("bad_value", parts[2]);
                if (!TryParsePulseSeconds(parts[3], out seconds))
                    return Error("bad_duration", parts[3]);
                SyntheticInput.PulseAxis(axis, value, seconds);
                return Ok(command);
            }
            if (parts[0] == "axis.clear" && parts.Length == 2)
            {
                InputAxes axis;
                if (!TryParseEnum(parts[1], out axis))
                    return Error("bad_axis", parts[1]);
                SyntheticInput.ClearAxis(axis);
                return Ok(command);
            }
            if (parts[0] == "input.clear" && parts.Length == 1)
            {
                SyntheticInput.Clear();
                return Ok(command);
            }
            if (parts[0] == "interact.list" && (parts.Length == 1 || parts.Length == 2))
            {
                string kind = parts.Length == 2 ? parts[1] : null;
                if (kind != null && !IsSupportedInteractionKind(kind))
                    return Error("bad_kind", kind);
                return InteractionListJson(kind);
            }
            if (parts[0] == "interact.use" && parts.Length == 3)
            {
                if (parts[1] == "kind")
                {
                    if (!IsSupportedInteractionKind(parts[2]))
                        return Error("bad_kind", parts[2]);
                    return UseInteractionTarget(parts[2], 0);
                }
                if (parts[1] == "entity")
                {
                    long entityId;
                    if (!long.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out entityId) || entityId <= 0)
                        return Error("bad_entity", parts[2]);
                    return UseInteractionTarget(null, entityId);
                }
                return Error("bad_selector", parts[1]);
            }
            if (parts[0] == "interact.release" && parts.Length == 1)
                return ReleaseInteraction();

            switch (command)
            {
                case "ping":
                    return Ok("pong");
                case "state":
                    return StateJson();
                case "menu.play":
                {
                    LandingScreenType screen = FindActive<LandingScreenType>();
                    if (screen == null)
                        return Error("wrong_state", "active LandingScreen not found");
                    screen.Play();
                    return Ok("menu.play");
                }
                case "menu.continue":
                {
                    SplashScreen screen = FindActive<SplashScreen>();
                    if (screen == null)
                        return Error("wrong_state", "active SplashScreen not found");
                    object welcome = AccessTools.Field(typeof(SplashScreen), "_welcomeParent").GetValue(screen);
                    GameObject welcomeRoot = welcome as GameObject;
                    string method = welcomeRoot != null && welcomeRoot.activeSelf
                        ? "ShowServerInformation"
                        : "OnSplashScreenButtonClicked";
                    AccessTools.Method(typeof(SplashScreen), method).Invoke(screen, null);
                    return Ok("menu.continue");
                }
                case "menu.enter-world":
                {
                    CharacterSelectionScreen screen = FindActive<CharacterSelectionScreen>();
                    if (screen == null)
                        return Error("wrong_state", "active CharacterSelectionScreen not found");
                    screen.EnterWorld();
                    return Ok("menu.enter-world");
                }
                default:
                    return Error("unknown_command", command);
            }
        }

        private string StateJson()
        {
            SplashScreen splash = FindActive<SplashScreen>();
            GameObject splashWelcome = splash == null ? null
                : AccessTools.Field(typeof(SplashScreen), "_welcomeParent").GetValue(splash) as GameObject;
            bool localPlayer = LocalPlayer.Exists;
            bool connected = global::Improbable.Unity.Core.SpatialOS.IsConnected;
            string phase = splash != null
                ? (splashWelcome != null && splashWelcome.activeSelf ? "splash-welcome" : "splash-server-info")
                : FindActive<LandingScreenType>() != null
                    ? "landing"
                    : FindActive<CharacterSelectionScreen>() != null
                        ? "character-selection"
                        : localPlayer ? "world" : connected ? "connected-transition" : "transition";

            string playerFields = string.Empty;
            bool timedInteraction = false;
            if (localPlayer)
            {
                Improbable.Math.Vector3d position = LocalPlayer.GlobalPosition;
                playerFields = ",\"playerPosition\":{\"x\":" + JsonNumber(position.X)
                    + ",\"y\":" + JsonNumber(position.Y)
                    + ",\"z\":" + JsonNumber(position.Z) + "}";
                TimedInteractionController interaction = LocalPlayer.Instance.timedInteractionController;
                timedInteraction = interaction != null && interaction.IsInteracting();
            }

            bool helmAttached = false;
            bool helmStateAvailable = false;
            long hullEntityId = 0;
            long controlEntityId = 0;
            float throttle = 0f;
            float vertical = 0f;
            Vector3 controlAxes = Vector3.zero;
            string hullPoseFields = string.Empty;
            ShipControlsBehaviour controls = ShipControlsBehaviour.Instance;
            if (controls != null && Patching.Flight.LocalHelmFeedback_Patch.PilotField != null)
            {
                try
                {
                    PilotStateReader pilot = Patching.Flight.LocalHelmFeedback_Patch.PilotField
                        .GetValue(controls) as PilotStateReader;
                    helmStateAvailable = pilot != null;
                    helmAttached = pilot != null && EntityId.IsValidEntityId(pilot.DrivingEntityId);
                    if (helmAttached)
                    {
                        hullEntityId = pilot.DrivingEntityId.Id;
                        controlEntityId = EntityId.IsValidEntityId(pilot.ControlEntityId)
                            ? pilot.ControlEntityId.Id : 0;
                        throttle = ShipControlsBehaviour.Throttle;
                        vertical = ShipControlsBehaviour.Vertical;
                        if (Patching.Flight.LocalHelmFeedback_Patch.AxesField != null)
                            controlAxes = (Vector3)Patching.Flight.LocalHelmFeedback_Patch.AxesField
                                .GetValue(controls);

                        var hull = global::Improbable.Unity.Core.SpatialOS.Universe
                            .Get(pilot.DrivingEntityId);
                        if (hull != null && hull.UnderlyingGameObject != null)
                        {
                            Transform hullTransform = hull.UnderlyingGameObject.transform;
                            hullPoseFields = ",\"renderedHullPose\":{\"position\":"
                                + JsonVector(hullTransform.position)
                                + ",\"euler\":" + JsonVector(hullTransform.rotation.eulerAngles)
                                + "}";
                        }
                    }
                }
                catch
                {
                    helmStateAvailable = false;
                }
            }
            string interactionTarget = InteractionTargetJson();
            return "{\"ok\":true,\"phase\":\"" + JsonEscape(phase)
                + "\",\"scene\":\"" + JsonEscape(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                + "\",\"connected\":" + JsonBool(connected)
                + ",\"localPlayer\":" + JsonBool(localPlayer)
                + playerFields
                + ",\"timedInteraction\":" + JsonBool(timedInteraction)
                + ",\"syntheticInteractHeld\":"
                    + JsonBool(SyntheticInput.IsHeld(InputButtons.Interact))
                + ",\"interactionTarget\":" + interactionTarget
                + ",\"helmStateAvailable\":" + JsonBool(helmStateAvailable)
                + ",\"helmAttached\":" + JsonBool(helmAttached)
                + ",\"hullEntityId\":" + hullEntityId.ToString(CultureInfo.InvariantCulture)
                + ",\"controlEntityId\":" + controlEntityId.ToString(CultureInfo.InvariantCulture)
                + ",\"throttle\":" + JsonNumber(throttle)
                + ",\"vertical\":" + JsonNumber(vertical)
                + ",\"controlAxes\":" + JsonVector(controlAxes)
                + hullPoseFields
                + ",\"frame\":" + Time.frameCount.ToString(CultureInfo.InvariantCulture)
                + ",\"realtime\":" + Time.realtimeSinceStartup.ToString("0.000", CultureInfo.InvariantCulture)
                + "}";
        }

        private static string InteractionTargetJson()
        {
            try
            {
                if (PlayerLookingAtInstance == null || LookingAtInteractive == null)
                    return "null";
                object lookingAt = PlayerLookingAtInstance.GetValue(null, null);
                if (lookingAt == null)
                    return "null";
                InteractiveObjectVisualizer interactive =
                    LookingAtInteractive.GetValue(lookingAt, null) as InteractiveObjectVisualizer;
                if (interactive == null)
                    return "null";

                Collider collider = LookingAtCollider == null ? null
                    : LookingAtCollider.GetValue(lookingAt, null) as Collider;
                InteractVerb verb = interactive.GetVerb(collider);
                SailVisualizer sail = interactive.GetComponent<SailVisualizer>();
                string kind = interactive.GetComponent<HelmVisualizer>() != null
                    ? "helm" : sail != null ? "sail" : "interactive";
                string sailField = sail == null ? string.Empty
                    : ",\"sailUnfurled\":" + JsonBool(sail.Unfurled);
                return "{\"entityId\":"
                    + interactive.EntityId.Id.ToString(CultureInfo.InvariantCulture)
                    + ",\"name\":\"" + JsonEscape(interactive.gameObject.name) + "\""
                    + ",\"kind\":\"" + kind + "\""
                    + ",\"verb\":\"" + JsonEscape(verb.ToString()) + "\""
                    + ",\"enabled\":" + JsonBool(interactive.InteractionEnabled)
                    + ",\"range\":" + JsonNumber(interactive.InteractRange)
                    + ",\"holdSeconds\":" + JsonNumber(interactive.GetInteractTime(collider))
                    + sailField + "}";
            }
            catch
            {
                // Looking-at state changes during fixed/update boundaries. A
                // transient destroyed object must not make the whole state
                // command fail; null truthfully means no stable target sample.
                return "null";
            }
        }

        private static string InteractionListJson(string kind)
        {
            if (!LocalPlayer.Exists)
                return Error("wrong_state", "local player is not initialized");

            var observer = LocalPlayer.Instance.interactAgentObserver;
            bool inputAvailable = observer != null && observer.CanInteract();
            bool driving = IsHelmAttached();
            List<InteractionCandidate> candidates = driving
                ? new List<InteractionCandidate>() : FindInteractionCandidates(kind, 0);
            StringBuilder json = new StringBuilder(256);
            json.Append("{\"ok\":true,\"action\":\"interact.list\",\"inputAvailable\":")
                .Append(JsonBool(inputAvailable))
                .Append(",\"driving\":").Append(JsonBool(driving))
                .Append(",\"targets\":[");
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0)
                    json.Append(',');
                json.Append(InteractionCandidateJson(candidates[i]));
            }
            json.Append("]}");
            return json.ToString();
        }

        private static string UseInteractionTarget(string kind, long entityId)
        {
            if (!LocalPlayer.Exists)
                return Error("wrong_state", "local player is not initialized");
            if (IsHelmAttached())
                return Error("wrong_state", "release the current helm before selecting a target");

            var observer = LocalPlayer.Instance.interactAgentObserver;
            TimedInteractionController timer = LocalPlayer.Instance.timedInteractionController;
            if (observer == null || timer == null || CheckInteractionMethod == null
                || InteractionTotalTimeField == null)
                return Error("client_state_unavailable", "native interaction seam is unavailable");
            if (!observer.CanInteract())
                return Error("input_blocked", "native interact input is currently captured by UI");
            if (timer.IsInteracting())
                return Error("busy", "a native timed interaction is already active");

            List<InteractionCandidate> candidates = FindInteractionCandidates(kind, entityId);
            InteractionCandidate selected = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (entityId == 0 || candidates[i].EntityId == entityId)
                {
                    selected = candidates[i];
                    break;
                }
            }
            if (selected == null)
                return Error("no_target", entityId == 0
                    ? "no enabled in-range " + kind + " target"
                    : "entity is not an enabled in-range helm or sail target");

            // CheckInteraction is the native client seam used by a physical E
            // press. It performs friendliness/lock checks, starts the real
            // TimedInteractionController, calls AttemptInteraction, and emits
            // the ordinary 1211 request. We only replace camera target choice.
            SyntheticInput.Pulse(InputButtons.Interact, MaxPulseSeconds);
            try
            {
                CheckInteractionMethod.Invoke(observer,
                    new object[] { selected.Visualizer, selected.Collider });
            }
            catch (Exception e)
            {
                SyntheticInput.Hold(InputButtons.Interact, false);
                return Error("interaction_failed", e.GetType().Name + ": " + e.Message);
            }

            bool timed = timer.IsInteracting();
            float holdSeconds = timed
                ? (float)InteractionTotalTimeField.GetValue(timer) : 0f;
            float releaseAfter = timed
                ? holdSeconds + InteractionReleaseMarginSeconds : 0.05f;
            if (releaseAfter > MaxPulseSeconds)
            {
                timer.CancelInteraction();
                SyntheticInput.Hold(InputButtons.Interact, false);
                return Error("hold_too_long", "native hold exceeds bounded automation limit");
            }
            SyntheticInput.ReleaseAfter(InputButtons.Interact, releaseAfter);

            return "{\"ok\":true,\"action\":\"interact.use\",\"target\":"
                + InteractionCandidateJson(selected)
                + ",\"timed\":" + JsonBool(timed)
                + ",\"nativeHoldSeconds\":" + JsonNumber(holdSeconds)
                + ",\"releaseAfterSeconds\":" + JsonNumber(releaseAfter) + "}";
        }

        private static string ReleaseInteraction()
        {
            if (!LocalPlayer.Exists || LocalPlayer.Instance.interactAgentObserver == null)
                return Error("wrong_state", "local player is not initialized");
            TimedInteractionController timer = LocalPlayer.Instance.timedInteractionController;
            if (timer != null && timer.IsInteracting())
                timer.CancelInteraction();
            SyntheticInput.Hold(InputButtons.Interact, false);
            LocalPlayer.Instance.interactAgentObserver.ReleaseInteractiveObject();
            return Ok("interact.release");
        }

        private static List<InteractionCandidate> FindInteractionCandidates(string kind,
            long requiredEntityId)
        {
            var result = new List<InteractionCandidate>();
            if (!LocalPlayer.Exists || LocalPlayer.Instance.interactAgentObserver == null)
                return result;

            Vector3 playerPosition = LocalPlayer.Instance.interactAgentObserver.transform.position;
            UnityEngine.Object[] objects =
                Resources.FindObjectsOfTypeAll(typeof(InteractiveObjectVisualizer));
            var seen = new HashSet<long>();
            for (int i = 0; i < objects.Length; i++)
            {
                InteractiveObjectVisualizer visualizer =
                    objects[i] as InteractiveObjectVisualizer;
                if (visualizer == null || !visualizer.gameObject.activeInHierarchy
                    || !visualizer.gameObject.scene.IsValid() || !visualizer.InteractionEnabled)
                    continue;

                string candidateKind = GetInteractionKind(visualizer);
                if (!IsSupportedInteractionKind(candidateKind)
                    || (kind != null && candidateKind != kind))
                    continue;
                EntityId candidateEntity = visualizer.EntityId;
                if (!EntityId.IsValidEntityId(candidateEntity)
                    || (requiredEntityId != 0 && candidateEntity.Id != requiredEntityId))
                    continue;

                float distance = Vector3.Distance(visualizer.transform.position, playerPosition);
                // Exact initial native rule from PlayerLookingAt.InRange(..., 0):
                // distance + a 0.5 m body allowance must be strictly below 1210 radius.
                if (distance + 0.5f >= visualizer.InteractRange)
                    continue;
                if (!seen.Add(candidateEntity.Id))
                    continue;

                Collider collider = FindInteractionCollider(visualizer, playerPosition);
                if (collider == null)
                    continue;
                result.Add(new InteractionCandidate
                {
                    Visualizer = visualizer,
                    Collider = collider,
                    EntityId = candidateEntity.Id,
                    Kind = candidateKind,
                    Verb = visualizer.GetVerb(collider),
                    Distance = distance,
                    Range = visualizer.InteractRange,
                    HoldSeconds = visualizer.GetInteractTime(collider),
                    SailUnfurled = candidateKind == "sail"
                        ? (bool?)visualizer.GetComponent<SailVisualizer>().Unfurled : null
                });
            }
            result.Sort(delegate(InteractionCandidate left, InteractionCandidate right)
            {
                int distance = left.Distance.CompareTo(right.Distance);
                return distance != 0 ? distance : left.EntityId.CompareTo(right.EntityId);
            });
            if (result.Count > MaxInteractionTargets)
                result.RemoveRange(MaxInteractionTargets, result.Count - MaxInteractionTargets);
            return result;
        }

        private static Collider FindInteractionCollider(
            InteractiveObjectVisualizer visualizer, Vector3 playerPosition)
        {
            Collider[] colliders = visualizer.GetComponentsInChildren<Collider>(true);
            Collider closest = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled
                    || !collider.gameObject.activeInHierarchy
                    || !collider.gameObject.IsInLayerMask(Layers.Interactables))
                    continue;
                float distance = Vector3.Distance(collider.transform.position, playerPosition);
                if (distance < closestDistance)
                {
                    closest = collider;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        private static bool IsHelmAttached()
        {
            try
            {
                if (PilotIsDrivingMethod != null
                    && (bool)PilotIsDrivingMethod.Invoke(null, null))
                    return true;
                ShipControlsBehaviour controls = ShipControlsBehaviour.Instance;
                if (controls == null || Patching.Flight.LocalHelmFeedback_Patch.PilotField == null)
                    return false;
                PilotStateReader pilot = Patching.Flight.LocalHelmFeedback_Patch.PilotField
                    .GetValue(controls) as PilotStateReader;
                return pilot != null && EntityId.IsValidEntityId(pilot.DrivingEntityId);
            }
            catch
            {
                return false;
            }
        }

        private static string GetInteractionKind(InteractiveObjectVisualizer visualizer)
        {
            if (visualizer.GetComponent<HelmVisualizer>() != null)
                return "helm";
            if (visualizer.GetComponent<SailVisualizer>() != null)
                return "sail";
            return "interactive";
        }

        private static bool IsSupportedInteractionKind(string kind)
        {
            return kind == "helm" || kind == "sail";
        }

        private static string InteractionCandidateJson(InteractionCandidate candidate)
        {
            string sailField = candidate.SailUnfurled.HasValue
                ? ",\"sailUnfurled\":" + JsonBool(candidate.SailUnfurled.Value)
                : string.Empty;
            return "{\"entityId\":" + candidate.EntityId.ToString(CultureInfo.InvariantCulture)
                + ",\"name\":\"" + JsonEscape(candidate.Visualizer.gameObject.name) + "\""
                + ",\"collider\":\"" + JsonEscape(candidate.Collider.gameObject.name) + "\""
                + ",\"kind\":\"" + candidate.Kind + "\""
                + ",\"verb\":\"" + JsonEscape(candidate.Verb.ToString()) + "\""
                + ",\"distance\":" + JsonNumber(candidate.Distance)
                + ",\"range\":" + JsonNumber(candidate.Range)
                + ",\"holdSeconds\":" + JsonNumber(candidate.HoldSeconds)
                + sailField + "}";
        }

        private static T FindActive<T>() where T : Component
        {
            UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(typeof(T));
            for (int i = 0; i < objects.Length; i++)
            {
                T component = objects[i] as T;
                if (component != null && component.gameObject.activeInHierarchy)
                    return component;
            }
            return null;
        }

        private void ListenLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    client.ReceiveTimeout = 12000;
                    client.SendTimeout = 12000;
                    HandleClient(client);
                }
                catch (SocketException)
                {
                    if (_running)
                        Debug.LogWarning("[WAR][test-bridge] listener socket interrupted.");
                }
                catch (Exception e)
                {
                    if (_running)
                        Debug.LogWarning("[WAR][test-bridge] request failed: " + e.Message);
                }
                finally
                {
                    if (client != null)
                        client.Close();
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 512))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 512))
            {
                writer.AutoFlush = true;
                bool tooLong;
                string line = ReadBoundedLine(reader, MaxLineLength, out tooLong);
                if (line == null || tooLong)
                {
                    writer.WriteLine(Error("bad_request", "one bounded command line required"));
                    return;
                }

                int split = line.IndexOf(' ');
                if (split <= 0 || !FixedTimeEquals(line.Substring(0, split), _token))
                {
                    writer.WriteLine(Error("unauthorized", "invalid test bridge token"));
                    return;
                }

                string commandText = line.Substring(split + 1).Trim();
                PendingCommand command = new PendingCommand(commandText);
                lock (_pending)
                    _pending.Enqueue(command);

                if (!command.Completed.WaitOne(10000, false))
                {
                    command.Cancelled = true;
                    writer.WriteLine(Error("timeout", "Unity main thread did not complete command"));
                    return;
                }
                writer.WriteLine(command.Response);
            }
        }

        private static bool FixedTimeEquals(string actual, string expected)
        {
            if (actual == null || expected == null)
                return false;
            int difference = actual.Length ^ expected.Length;
            int length = Math.Max(actual.Length, expected.Length);
            for (int i = 0; i < length; i++)
            {
                char actualChar = i < actual.Length ? actual[i] : (char)0;
                char expectedChar = i < expected.Length ? expected[i] : (char)0;
                difference |= actualChar ^ expectedChar;
            }
            return difference == 0;
        }

        private static bool IsValidToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 32 || token.Length > 256)
                return false;
            for (int i = 0; i < token.Length; i++)
            {
                if (char.IsWhiteSpace(token[i]) || char.IsControl(token[i]))
                    return false;
            }
            return true;
        }

        private static string ReadBoundedLine(TextReader reader, int maximumLength,
            out bool tooLong)
        {
            tooLong = false;
            StringBuilder builder = new StringBuilder(Math.Min(maximumLength, 128));
            while (true)
            {
                int next = reader.Read();
                if (next < 0)
                    return builder.Length == 0 ? null : builder.ToString();
                if (next == '\n')
                    return builder.ToString();
                if (next == '\r')
                {
                    if (reader.Peek() == '\n')
                        reader.Read();
                    return builder.ToString();
                }
                if (builder.Length >= maximumLength)
                {
                    tooLong = true;
                    return null;
                }
                builder.Append((char)next);
            }
        }

        private static int ParsePort(string text)
        {
            int port;
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                && port >= 1024 && port <= 65535 ? port : DefaultPort;
        }

        private static bool TryParseEnum<T>(string text, out T value) where T : struct
        {
            try
            {
                if (!Enum.IsDefined(typeof(T), text))
                {
                    value = default(T);
                    return false;
                }
                value = (T)Enum.Parse(typeof(T), text, false);
                return true;
            }
            catch
            {
                value = default(T);
                return false;
            }
        }

        private static bool TryParseUnitValue(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value)
                && value >= -1f && value <= 1f;
        }

        private static bool TryParsePulseSeconds(string text, out float seconds)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out seconds)
                && !float.IsNaN(seconds) && !float.IsInfinity(seconds)
                && seconds >= 0.02f && seconds <= MaxPulseSeconds;
        }

        private static string Ok(string action)
        {
            return "{\"ok\":true,\"action\":\"" + JsonEscape(action) + "\"}";
        }

        private static string Error(string code, string message)
        {
            return "{\"ok\":false,\"error\":\"" + JsonEscape(code)
                + "\",\"message\":\"" + JsonEscape(message) + "\"}";
        }

        private static string JsonBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string JsonNumber(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string JsonVector(Vector3 value)
        {
            return "{\"x\":" + JsonNumber(value.x)
                + ",\"y\":" + JsonNumber(value.y)
                + ",\"z\":" + JsonNumber(value.z) + "}";
        }

        private static string JsonEscape(string value)
        {
            if (value == null)
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private void OnDestroy()
        {
            StopBridge();
        }

        private void StopBridge()
        {
            _running = false;
            SyntheticInput.Disable();
            if (_listener != null)
            {
                try { _listener.Stop(); }
                catch { }
                _listener = null;
            }
        }

        private sealed class PendingCommand
        {
            internal readonly string Command;
            internal readonly ManualResetEvent Completed = new ManualResetEvent(false);
            internal volatile bool Cancelled;
            internal string Response;

            internal PendingCommand(string command)
            {
                Command = command;
            }
        }

        private sealed class InteractionCandidate
        {
            internal InteractiveObjectVisualizer Visualizer;
            internal Collider Collider;
            internal long EntityId;
            internal string Kind;
            internal InteractVerb Verb;
            internal float Distance;
            internal float Range;
            internal float HoldSeconds;
            internal bool? SailUnfurled;
        }
    }
}
