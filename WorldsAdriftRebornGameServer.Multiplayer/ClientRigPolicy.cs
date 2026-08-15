using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    public enum RemotePlayerPositionBranch
    {
        Global,
        ShipRelative,
    }

    /// <summary>
    /// Client-side rules that decide which Traveller rig is MINE and who is
    /// allowed to claim a prefab singleton.
    ///
    /// This lives in the server-side policy library only because that is the one
    /// project with unit tests; the BepInEx mod (net35, Unity) LINKS this file
    /// via &lt;Compile Include&gt; rather than referencing the assembly, so the mod
    /// and the tests exercise the same code. Keep it net35 / C# 7.3 clean: no
    /// IReadOnly* generics, no LINQ, no nullable annotations, no target-typed new.
    ///
    /// Every function here takes plain strings and bools, never UnityEngine
    /// types, precisely so it can be tested without a game.
    /// </summary>
    public static class ClientRigPolicy
    {
        /// <summary>
        /// Components that exist ONLY on the full local-player rig
        /// (Traveller@Player). The plain remote Traveller carries none of them.
        ///
        /// Rig discrimination must be done with these and never with the root
        /// object's NAME. A name-based check is what let the sweeper neutralise
        /// the real player: it made the local rig kinematic and left the remote
        /// mover driving it - "spawned in the sky, falling forever", confirmed by
        /// telemetry (kinematic=True, vel=0, Y decreasing). LocalPlayer.Instance
        /// is no help either: it is a SCENE object, not part of the Traveller
        /// prefab, so its root never equals a rig root.
        /// </summary>
        public static readonly string[] LocalOnlyComponents =
        {
            "LocalPlayerInit",
            "ClientAuthoritativePlayerMovement",
            "InputBehaviour",
            "PlayerInputSetup",
            "CameraProxy",
        };

        /// <summary>
        /// True if the rig carrying these component type names is the LOCAL
        /// player. Decided purely by component presence - deliberately blind to
        /// the rig's name.
        /// </summary>
        public static bool IsLocalRig(IEnumerable<string> componentTypeNames)
        {
            if (componentTypeNames == null)
            {
                return false;
            }

            foreach (string name in componentTypeNames)
            {
                if (name == null)
                {
                    continue;
                }
                for (int i = 0; i < LocalOnlyComponents.Length; i++)
                {
                    if (name == LocalOnlyComponents[i])
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Whether PlayerVisualizer_Patch lets the game's own FixedUpdate run
        /// (local rig) instead of running the safe remote global/ship-relative
        /// reconstruction.
        ///
        /// Components only, never the root name - rule 11. A name check used to
        /// be OR'd in here. It was unreachable, because mirrored remotes spawn
        /// from prefab context "Default" and so are named "Traveller N" rather
        /// than "Traveller@Player", but the consequence of it ever firing is the
        /// worst one we have seen: handing a REMOTE rig to the game's own
        /// FixedUpdate takes the Parent branch, which is what put a rig ~90 km
        /// away and through the map. The name check that cost us that round is
        /// gone everywhere else; it is gone here now too.
        /// </summary>
        public static bool TreatAsLocalForPlayerVisualizer(string rootName, IEnumerable<string> componentTypeNames)
        {
            return IsLocalRig(componentTypeNames);
        }

        /// <summary>
        /// Selects the safe remote position branch. A positive relative bias
        /// with a resolved relative object means the 1073 position is expressed
        /// in that object's coordinate frame. Rendering only its global 190602
        /// position makes an aboard avatar trail a moving ship by roughly
        /// ship-speed times interpolation latency.
        ///
        /// This deliberately does not expose retail's TransformState.Parent
        /// branch; unresolved parent state previously placed remote rigs tens of
        /// kilometres away. Only the proven ship-relative frame is restored.
        /// </summary>
        public static RemotePlayerPositionBranch PositionBranchForRemote(
            bool hasRelativeObject,
            float relativeBias)
        {
            return hasRelativeObject && relativeBias > 0f
                ? RemotePlayerPositionBranch.ShipRelative
                : RemotePlayerPositionBranch.Global;
        }

        /// <summary>
        /// Keep-first rule for prefab singletons (LocalPlayer.Instance,
        /// CameraSelectionVisualizer.Instance, the CameraProxy that owns the
        /// camera).
        ///
        /// Unity's [Require] visualizer gating only suppresses OnEnable/Update -
        /// Awake and Start ALWAYS run on instantiation. So a mirrored second rig
        /// would otherwise take the local-player identity and the camera the
        /// instant it spawns. The local player always instantiates first (the
        /// server only mirrors remotes after the local AddEntityOp), so keeping
        /// the first claimant keeps the right one.
        ///
        /// <paramref name="currentOwnerIsAlive"/> must be computed with Unity's
        /// overloaded ==, so a DESTROYED owner counts as not alive and a respawn
        /// can re-claim. <paramref name="candidateIsCurrentOwner"/> must be a
        /// ReferenceEquals check, so the owner re-running its own hook is allowed
        /// through.
        /// </summary>
        public static bool ShouldClaimSingleton(bool currentOwnerIsAlive, bool candidateIsCurrentOwner)
        {
            return !currentOwnerIsAlive || candidateIsCurrentOwner;
        }
    }
}
