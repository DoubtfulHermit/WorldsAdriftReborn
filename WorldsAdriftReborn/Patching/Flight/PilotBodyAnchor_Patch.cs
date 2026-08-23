using System;
using System.Reflection;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Core;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Places the local pilot at the helm's authored standing point when they
    /// take control, independent of which side they approached from.
    ///
    /// WHY THE BODY/CAMERA START ANYWHERE. Nothing in the retail client moves the
    /// player on man: the driving state only zeroes locomotion
    /// (PlayerCharacterAnimation.cs:263-267) and binds hand/look IK effectors
    /// (IKOrder.SetupIKTargets) - the body ROOT simply keeps whatever facing the
    /// player approached the helm with. PilotCameraController then uses the
    /// player's CameraTargetPilot as its positional target, so entering from the
    /// left or right permanently offsets the whole pilot camera by that amount.
    ///
    /// THE FAITHFUL ANCHOR. The shipped Helm01_unityclient prefab contains an
    /// explicit child named <c>#PilotPosition</c> at helm-local
    /// (0, 0.074, -1.4070084), identity rotation. It is the safe, authored spot
    /// behind and dead-centre on the wheel - not a guessed camera/body offset.
    /// The modular-cannon retail path uses the same #PilotPosition convention.
    /// On the transition into driving this patch resolves that child from the
    /// server-provided ControlEntityId (the helm), snaps the client-authoritative
    /// player root and rigidbody to its exact world pose, clears stale ground-
    /// relative movement caches, and zeroes locomotion. The ordinary player
    /// transform stream remains authoritative afterwards.
    /// </summary>
    [HarmonyPatch]
    internal static class PilotBodyAnchor_Patch
    {
        private static readonly Type PilotVisualizerType = AccessTools.TypeByName("PilotVisualizer");
        private static readonly FieldInfo PilotReaderField =
            PilotVisualizerType == null ? null : AccessTools.Field(PilotVisualizerType, "_pilot");

        private const string PilotAnchorName = "#PilotPosition";

        private static bool _loggedError;
        private static bool _loggedMissingAnchor;

        private static bool Prepare()
        {
            bool ok = PilotVisualizerType != null && PilotReaderField != null;
            if (!ok)
            {
                Debug.LogWarning("[WAR][flight] PilotBodyAnchor_Patch: PilotVisualizer/_pilot not"
                    + " resolvable; body-facing patch skipped.");
            }
            return ok;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(PilotVisualizerType, "OnChangeLinkedEntity");
        }

        private static void Postfix(object __instance, EntityId drivenEntityId)
        {
            try
            {
                if (EntityId.IsInvalidEntityId(drivenEntityId))
                {
                    PilotBodyAnchorFollower.StopActive();
                    return; // dismount transition
                }
                if (!LocalPlayer.Exists)
                {
                    return; // no rig yet
                }

                var pilot = PilotReaderField.GetValue(__instance) as Bossa.Travellers.Controls.PilotStateReader;

                // Prefer the helm entity named by our 1109 ControlEntityId. The
                // hull fallback preserves compatibility with a retail-style Unity
                // hierarchy where the helm (and its anchor) is a hull child.
                Transform reference = null;
                if (pilot != null && !EntityId.IsInvalidEntityId(pilot.ControlEntityId))
                {
                    var helm = global::Improbable.Unity.Core.SpatialOS.Universe.Get(pilot.ControlEntityId);
                    if (helm != null && helm.UnderlyingGameObject != null)
                    {
                        reference = helm.UnderlyingGameObject.transform;
                    }
                }
                if (reference == null)
                {
                    var vehicle = global::Improbable.Unity.Core.SpatialOS.Universe.Get(drivenEntityId);
                    if (vehicle == null || vehicle.UnderlyingGameObject == null)
                    {
                        return;
                    }
                    reference = vehicle.UnderlyingGameObject.transform;
                }

                Transform anchor = FindDescendant(reference, PilotAnchorName);
                if (anchor == null)
                {
                    if (!_loggedMissingAnchor)
                    {
                        _loggedMissingAnchor = true;
                        Debug.LogWarning("[WAR][flight] helm has no authored " + PilotAnchorName
                            + " child; refusing to invent a pilot/camera offset.");
                    }
                    return;
                }

                Transform root = LocalPlayer.Transform;
                if (root == null)
                {
                    return;
                }

                Vector3 prior = root.position;
                Vector3 position = anchor.position;
                Quaternion rotation = anchor.rotation;

                // Clear the two relative-ground ledgers which otherwise remember
                // the approach-side deck position and can restore it on a later
                // physics correction/dismount.
                ClientAuthoritativePlayerMovement clientMovement =
                    LocalPlayer.Instance.ClientAuthoritativePlayerMovement;
                if (clientMovement != null)
                {
                    clientMovement.PlayerWasRepositioned();
                }
                PlayerMove playerMove = LocalPlayer.Instance.playerMove;
                if (playerMove != null)
                {
                    playerMove.PlayerWasRespositioned(); // retail spelling
                }

                // The Rigidbody and transform are the same physical root in the
                // shipped Traveller prefab. Set both so the current render frame
                // and the next physics frame agree; no delayed MovePosition that
                // leaves the camera one frame on the approach side.
                Rigidbody body = root.GetComponent<Rigidbody>();
                root.position = position;
                root.rotation = rotation;
                if (body != null)
                {
                    body.position = position;
                    body.rotation = rotation;
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                if (playerMove != null)
                {
                    playerMove.ZeroOut(Vector3.zero, Vector3.zero);
                }

                // Retail relies on the locally simulated ship Rigidbody to carry
                // this body after the one-time anchor. Our hull is instead moved
                // by a kinematic PathFollower, while the helm is a separate "~"
                // follower entity. During an authoritative acceleration correction
                // the helm therefore advances but the pilot Rigidbody does not,
                // producing the observed 240 ms micro-steps and growing hand/body
                // gap. Keep the body on the SAME authored anchor for as long as the
                // exact driving lifecycle remains active. This changes only the
                // local client-authoritative player pose; ship motion stays wholly
                // server-authored.
                PilotBodyAnchorFollower.StartOrRefresh(root, anchor, pilot, drivenEntityId.Id);

                Debug.Log("[WAR][flight] pilot snapped to helm's authored " + PilotAnchorName
                    + " anchor (approach offset " + Vector3.Distance(prior, position).ToString("0.###")
                    + " m cleared).");
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Debug.LogWarning("[WAR][flight] PilotBodyAnchor_Patch failed (once): " + e.Message);
                }
            }
        }

        private static Transform FindDescendant(Transform root, string exactName)
        {
            if (root == null)
            {
                return null;
            }
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                if (string.Equals(descendants[i].name, exactName, StringComparison.Ordinal))
                {
                    return descendants[i];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Presentation/character carry adapter for the one retail assumption our
    /// server topology violates: a piloted helm and hull are separate entities,
    /// not one locally simulated Rigidbody hierarchy.
    ///
    /// It is deliberately lifecycle-bound and fail-closed. It follows only while
    /// PilotVisualizer still names the exact hull captured on Man, stops on the
    /// native dismount transition, and never moves a ship or a remote player.
    /// </summary>
    internal sealed class PilotBodyAnchorFollower : MonoBehaviour
    {
        private static PilotBodyAnchorFollower _active;

        private Transform _anchor;
        private Rigidbody _body;
        private PlayerMove _playerMove;
        private Bossa.Travellers.Controls.PilotStateReader _pilot;
        private long _hullEntityId;
        private Vector3 _lastAnchorPosition;
        private bool _hasLastAnchorPosition;
        private bool _loggedFailure;

        internal static bool IsActive => _active != null && _active.enabled;

        internal static float CurrentGapMeters
        {
            get
            {
                PilotBodyAnchorFollower active = _active;
                return active == null || active._anchor == null
                    ? 0f
                    : Vector3.Distance(active.transform.position, active._anchor.position);
            }
        }

        internal static void StartOrRefresh(Transform playerRoot, Transform anchor,
            Bossa.Travellers.Controls.PilotStateReader pilot, long hullEntityId)
        {
            if (playerRoot == null || anchor == null || pilot == null || hullEntityId <= 0)
            {
                return;
            }

            PilotBodyAnchorFollower follower = playerRoot.GetComponent<PilotBodyAnchorFollower>();
            if (follower == null)
            {
                follower = playerRoot.gameObject.AddComponent<PilotBodyAnchorFollower>();
            }
            follower.Bind(anchor, pilot, hullEntityId);
            _active = follower;
        }

        internal static void StopActive()
        {
            PilotBodyAnchorFollower active = _active;
            _active = null;
            if (active != null)
            {
                active.enabled = false;
                Destroy(active);
                Debug.Log("[WAR][flight] pilot anchor carry released on dismount.");
            }
        }

        private void Bind(Transform anchor, Bossa.Travellers.Controls.PilotStateReader pilot,
            long hullEntityId)
        {
            _anchor = anchor;
            _pilot = pilot;
            _hullEntityId = hullEntityId;
            _body = GetComponent<Rigidbody>();
            _playerMove = LocalPlayer.Exists ? LocalPlayer.Instance.playerMove : null;
            _lastAnchorPosition = anchor.position;
            _hasLastAnchorPosition = true;
            enabled = true;
            Debug.Log("[WAR][flight] pilot anchor carry armed for hull " + hullEntityId + ".");
        }

        private bool StillOwnsExactDrivingLifecycle()
        {
            if (_anchor == null || !LocalPlayer.Exists || _pilot == null)
            {
                return false;
            }
            return !EntityId.IsInvalidEntityId(_pilot.DrivingEntityId)
                && _pilot.DrivingEntityId.Id == _hullEntityId;
        }

        private void FixedUpdate()
        {
            try
            {
                if (!StillOwnsExactDrivingLifecycle())
                {
                    StopActive();
                    return;
                }

                Vector3 position = _anchor.position;
                Quaternion rotation = _anchor.rotation;
                Vector3 velocity = Vector3.zero;
                if (_hasLastAnchorPosition && Time.fixedDeltaTime > 0f)
                {
                    velocity = (position - _lastAnchorPosition) / Time.fixedDeltaTime;
                    // PathFollower itself treats >=60 m/s deltas as invalid for
                    // speed telemetry. Never inject a teleport-sized velocity into
                    // the player merely because a checkout/origin correction ran.
                    if (!IsFinite(velocity) || velocity.sqrMagnitude >= 3600f)
                    {
                        velocity = Vector3.zero;
                    }
                }
                _lastAnchorPosition = position;
                _hasLastAnchorPosition = true;

                transform.position = position;
                transform.rotation = rotation;
                if (_body != null)
                {
                    _body.position = position;
                    _body.rotation = rotation;
                    _body.velocity = velocity;
                    _body.angularVelocity = Vector3.zero;
                }
                if (_playerMove != null)
                {
                    _playerMove.ZeroOut(velocity, Vector3.zero);
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.LogWarning("[WAR][flight] pilot anchor carry failed closed: " + e.Message);
                }
                StopActive();
            }
        }

        // Physics owns the authoritative local root in FixedUpdate; this late
        // render pass removes the one-frame gap between that root and the helm's
        // separately updated IK target without changing velocity or networking.
        private void LateUpdate()
        {
            if (enabled && StillOwnsExactDrivingLifecycle())
            {
                transform.position = _anchor.position;
                transform.rotation = _anchor.rotation;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void OnDestroy()
        {
            if (_active == this)
            {
                _active = null;
            }
        }
    }
}
