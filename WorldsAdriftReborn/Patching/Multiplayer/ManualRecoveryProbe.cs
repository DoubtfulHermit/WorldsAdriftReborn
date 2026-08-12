using System;
using Assets.Scripts.Utils;
using Improbable.CoreLibrary.CoordinateRemapping;
using Improbable.Math;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * F10 recovers the LOCAL player to Haven's spawn, in-process, with no server
     * round trip.
     *
     * WHY THIS EXISTS. The server used to AUTO-teleport anyone who dropped below
     * the island back to Haven (FallPolicy / FallRescueService). That was safe
     * when the only thing to stand on was one island, but once ships fly a
     * player who is intentionally below the island - flying, boarding,
     * descending - would be snatched home mid-flight. So the automatic yank is
     * now OFF by default (only a very deep "fell through the world" net remains,
     * server side) and recovery is THIS manual button.
     *
     * WHY CLIENT-SIDE SELF-RECOVERY rather than signalling the server. It is
     * exactly what the server's teleport already does, minus the network:
     *
     *   - TeleportTransformVisualizer.HandleTeleportRequest (decompiled) does
     *         transform.position = LocalPosition.RemapGlobalToUnityVector();
     *         playerMove.Respawn(transform.position, transform.rotation);
     *     i.e. it takes the SAME global Haven coordinate the server would send on
     *     190607, remaps it through CoordinateRemappingBehaviour, sets the
     *     transform, and calls PlayerMove.Respawn (which zeroes velocity, revives
     *     the ragdoll and drops carried items). VERIFIED against the decompile at
     *     ~/Games/WAReborn-decompiled.
     *   - The local player owns its 190602 TransformState, so
     *     LocalTransformUpdaterBehaviour publishes the new (unparented) position
     *     to the server and other players on its own - the same reason the
     *     server's teleport propagates. No new authority, no new channel, no
     *     server code path, and it mirrors ReconnectProbe's shape exactly.
     *
     * The Haven coordinate is the SAME one the server spawns and teleports to:
     * SpawnPolicy.PlayerSpawnPosition = island-local (208, 6.70, 4.00) on Haven
     * instance #5 = world (17212.4300, -311.9693420, -1130.16748) m. Kept here as
     * a literal rather than shared, because this net35 mod cannot reference the
     * net6 server policy assembly; if the server's spawn point ever moves, this
     * must move with it (it is asserted against SpawnPolicy in the server tests
     * on the server side; here it is a documented constant).
     *
     * Resolved defensively, like ReconnectProbe: a missing local rig or a missing
     * PlayerMove logs a warning instead of throwing, so a mis-timed press or an
     * unexpected scene never breaks anything.
     */
    internal class ManualRecoveryProbe : MonoBehaviour
    {
        private const KeyCode RecoveryKey = KeyCode.F10;

        /// <summary>
        /// Haven #5 spawn point in GLOBAL metres - the SAME value the server puts
        /// on 190607 (WorldsAdriftRebornGameServer SpawnPolicy.PlayerSpawnPosition,
        /// Q52.12 (70502113, -1277826, -4629165)). Fed through the game's own
        /// global->Unity remap below, exactly as the teleport visualizer does.
        /// </summary>
        private static readonly Vector3d HavenSpawnGlobal =
            new Vector3d(17212.4300, -311.9693420, -1130.16748);

        private void Update()
        {
            if (!Input.GetKeyDown(RecoveryKey))
            {
                return;
            }

            try
            {
                Recover();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] F10 recovery threw: " + e);
            }
        }

        private static void Recover()
        {
            // The reliable "which Traveller is mine" anchor - the rig whose
            // CameraProxy claimed the camera. Same source LocalPlayerTelemetry and
            // RemoteRigSweeper use.
            Transform root = CameraProxy_Patch.OwnerRoot;
            if (root == null)
            {
                Debug.LogWarning("[WAReborn] F10 recovery: no local player rig yet "
                    + "(CameraProxy_Patch.OwnerRoot is null). Are you in the world?");
                return;
            }

            // Match the visualizer: it finds PlayerMove the same way on the player
            // GameObject before calling Respawn.
            PlayerMove playerMove =
                GameObjectUtils.GetComponentInChildrenAndSubChildren<PlayerMove>(root.gameObject);
            if (playerMove == null)
            {
                Debug.LogWarning("[WAReborn] F10 recovery: no PlayerMove under '"
                    + root.name + "'; cannot respawn.");
                return;
            }

            // The one remap the teleport visualizer performs, with the same global
            // coordinate. Doing it through CoordinateRemappingBehaviour means the
            // floating origin is honoured identically to a server teleport.
            Vector3 unityPos = CoordinateRemappingBehaviour.GlobalVectorToUnityPosition(HavenSpawnGlobal);

            root.position = unityPos;
            playerMove.Respawn(unityPos, root.rotation);

            Debug.Log("[WAReborn] F10 recovery: placed local player at Haven spawn (global "
                + HavenSpawnGlobal.X + ", " + HavenSpawnGlobal.Y + ", " + HavenSpawnGlobal.Z
                + " -> unity " + unityPos + ") and called PlayerMove.Respawn.");
        }
    }
}
