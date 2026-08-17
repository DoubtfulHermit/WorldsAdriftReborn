using System;
using System.Reflection;
using Assets.Visualizers;
using Bossa.Prototype.Character.Observer;
using Bossa.Travellers.CraftingStation;
using Bossa.Travellers.Interact;
using HarmonyLib;
using Improbable;
using UnityEngine;
using WorldsAdriftReborn.Config;

namespace WorldsAdriftReborn.Patching.Interactions
{
    /*
     * STATION PICKUP SENDER - the client half of the non-retail "pack a placed
     * Shipyard / Assembly Station back into inventory" extension.
     *
     * Retail has NO input path that emits InteractVerb.PickUp on these prefabs:
     * both bake Craft into their InteractiveObjectVisualizer, and the scanner tool
     * explicitly refuses every placeable that is not a ShipPartVisualizer
     * (PlayerScannerTool.cs:495, decompile). So a dedicated key produces the 1211
     * event instead, mirroring exactly what the client's own completed interaction
     * does: InteractAgentObserver.IssueInteraction(entityId, verb) ->
     * interactWriter.Update.TriggerInteractWithObject(entityId, verb).FinishAndSend()
     * (InteractAgentObserver.cs:451, decompile). The server validates everything
     * (ownership, owner uid, busy states, range, reservation) in
     * StationPickupPolicy - this sender is deliberately dumb.
     *
     * TARGET RESOLUTION is the game's own looking-at chain, not our own raycast:
     * PlayerLookingAt (the component that feeds the E prompt) already requires the
     * visualizer to be available, enabled and within its 1210 radius before it
     * sets LookingAtInteractive. PlayerLookingAt is an INTERNAL class, so it is
     * reached by reflection (AccessTools, the established pattern in this mod -
     * see PilotBodyAnchor_Patch); InteractAgentObserver and
     * CraftingStationBehaviour are public and used directly.
     *
     * The key is HELD for 0.5 s (a deliberate pack, not a fat-finger) and fires
     * ONCE per hold. Default X, configurable via WorldsAdriftReborn.cfg
     * [Interact] Interact_StationPickupKey. The E/Craft flow is untouched - this
     * adds a component, patches nothing, and touches no prefab colliders.
     */
    internal class StationPickupSender : MonoBehaviour
    {
        private const float HoldSeconds = 0.5f;

        private static readonly Type PlayerLookingAtType =
            AccessTools.TypeByName("Assets.Scripts.Player.PlayerLookingAt");
        private static readonly PropertyInfo LookingAtInstanceProp =
            PlayerLookingAtType == null ? null : AccessTools.Property(PlayerLookingAtType, "Instance");
        private static readonly PropertyInfo LookingAtInteractiveProp =
            PlayerLookingAtType == null ? null : AccessTools.Property(PlayerLookingAtType, "LookingAtInteractive");

        private KeyCode _key = KeyCode.X;
        private bool _keyResolved;
        private long _armedTarget;
        private float _held;
        private bool _sentThisHold;

        // A PickUp packet is only a request. Keep the exact station we requested
        // until its server-owned 1210 reader changes to available=false. The
        // server sends a 190602 sink at the same time, but these prefabs use
        // StaticLocalTransformBehaviour, whose retail OnEnable reads 190602 once
        // and deliberately never observes later updates. Therefore the 1210
        // transition is the authoritative live-removal signal.
        private long _pendingTarget;
        private InteractiveObjectVisualizer _pendingVisualizer;
        private GameObject _pendingStationObject;

        private void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                // A mis-timed scene or an unexpected null must never spam per-frame:
                // log once and switch the sender off for this session.
                Debug.LogWarning("[WAR][pickup] station pickup sender disabled after error: " + e);
                enabled = false;
            }
        }

        private void Tick()
        {
            CompleteAuthoritativeRemoval();

            if (LookingAtInstanceProp == null || LookingAtInteractiveProp == null)
            {
                Debug.LogWarning("[WAR][pickup] PlayerLookingAt not resolvable via reflection; station pickup is off.");
                enabled = false;
                return;
            }

            // The game's own looking-at component (null until the player rig exists).
            Component lookingAt = LookingAtInstanceProp.GetValue(null, null) as Component;
            if (lookingAt == null)
            {
                Disarm();
                return;
            }

            // The interactive object the game itself says the player is looking at
            // (availability + 1210 radius already enforced by PlayerLookingAt).
            InteractiveObjectVisualizer visualizer =
                LookingAtInteractiveProp.GetValue(lookingAt, null) as InteractiveObjectVisualizer;
            if (visualizer == null)
            {
                Disarm();
                return;
            }

            // Only a placed Shipyard / Assembly Station: resolved through the same
            // component walk the game's own crafting gate uses (the prefab's
            // CraftingStationBehaviour sits on/above the console collider -
            // InteractAgentObserver.HasCraftingStationButUseForbidden, decompile).
            CraftingStationBehaviour station = visualizer.GetComponentInParent<CraftingStationBehaviour>();
            if (station == null || (!station.IsShipyard && !station.IsCraftingStation))
            {
                Disarm();
                return;
            }

            EntityId target = visualizer.EntityId;
            if (!target.IsValid())
            {
                Disarm();
                return;
            }

            ResolveKeyOnce();

            if (target.Id != _armedTarget)
            {
                _armedTarget = target.Id;
                _held = 0f;
                _sentThisHold = false;
                Debug.Log("[WAR][pickup] armed on " + (station.IsShipyard ? "shipyard" : "assembly station")
                    + " entity " + target.Id + ": hold " + _key + " for " + HoldSeconds
                    + "s to pack it back into your inventory.");
            }

            if (Input.GetKey(_key))
            {
                _held += Time.deltaTime;
                if (!_sentThisHold && _held >= HoldSeconds)
                {
                    _sentThisHold = true; // once per hold; release re-arms
                    if (Send(lookingAt, target, station.IsShipyard))
                    {
                        _pendingTarget = target.Id;
                        _pendingVisualizer = visualizer;
                        // The behaviour can live below the entity root. Disable
                        // SpatialOS's whole underlying prefab, not merely the
                        // console child, so renderers, collision and UI bindings
                        // all leave together.
                        Improbable.Unity.Internal.EntityObject entity =
                            station.gameObject.GetSpatialOsEntity();
                        _pendingStationObject = entity != null
                            ? entity.UnderlyingGameObject
                            : station.gameObject;
                    }
                }
            }
            else
            {
                _held = 0f;
                _sentThisHold = false;
            }
        }

        /// <summary>
        /// Sends the exact wire event a completed native interaction sends -
        /// TriggerInteractWithObject(target, PickUp) on the player's own 1211 -
        /// through the game's own public InteractAgentObserver.IssueInteraction.
        /// </summary>
        private static bool Send(Component lookingAt, EntityId target, bool isShipyard)
        {
            InteractAgentObserver observer = lookingAt.GetComponent<InteractAgentObserver>();
            if (observer == null)
            {
                Debug.LogWarning("[WAR][pickup] no InteractAgentObserver on the player rig; cannot send PickUp.");
                return false;
            }

            observer.IssueInteraction(target, InteractVerb.PickUp);
            Debug.Log("[WAR][pickup] sent PickUp (1211 InteractWithObject) for "
                + (isShipyard ? "shipyard" : "assembly station") + " entity " + target.Id
                + "; the server decides (watch its [pickup] log lines).");
            return true;
        }

        /// <summary>
        /// Hides a packed station only after the server accepts the transaction.
        /// InteractiveState.available=false is safe as the acknowledgement because
        /// the target was available when PlayerLookingAt let us arm on it, and the
        /// pickup transaction is the only station path that flips it false. A
        /// rejection leaves it true, so the station remains visible and usable.
        /// </summary>
        private void CompleteAuthoritativeRemoval()
        {
            if (_pendingTarget <= 0 || _pendingVisualizer == null)
            {
                return;
            }

            long observedTarget = _pendingVisualizer.EntityId.Id;
            if (!WorldsAdriftRebornGameServer.Multiplayer.Placement.StationPickupVisibilityPolicy.ShouldHide(
                    _pendingTarget, observedTarget, _pendingVisualizer.InteractionEnabled))
            {
                return;
            }

            Debug.Log("[WAR][pickup] server accepted pickup for station entity "
                + _pendingTarget + "; hiding its static prefab locally.");

            // Disable rather than Destroy: SpatialOS still owns this entity because
            // this transport has no RemoveEntityOp. Disabling runs the prefab's
            // normal OnDisable cleanup (crafting UI registration, looking-at cache,
            // audio/VFX) while the server tombstone and removed persistence record
            // keep it absent for late joiners and future boots.
            if (_pendingStationObject != null)
            {
                _pendingStationObject.SetActive(false);
            }

            _pendingTarget = 0;
            _pendingVisualizer = null;
            _pendingStationObject = null;
        }

        private void Disarm()
        {
            _armedTarget = 0;
            _held = 0f;
            _sentThisHold = false;
        }

        /// <summary>
        /// Reads the configured key name once (net35 has no Enum.TryParse). An
        /// unknown name logs and falls back to X so the feature is never silently
        /// dead.
        /// </summary>
        private void ResolveKeyOnce()
        {
            if (_keyResolved)
            {
                return;
            }
            _keyResolved = true;

            string configured = ModSettings.stationPickupKey != null ? ModSettings.stationPickupKey.Value : "X";
            try
            {
                _key = (KeyCode)Enum.Parse(typeof(KeyCode), configured, true);
            }
            catch (Exception)
            {
                _key = KeyCode.X;
                Debug.LogWarning("[WAR][pickup] configured key '" + configured
                    + "' is not a UnityEngine.KeyCode; falling back to X.");
            }
            Debug.Log("[WAR][pickup] station pickup key is " + _key
                + " (hold " + HoldSeconds + "s while looking at a placed station).");
        }
    }
}
