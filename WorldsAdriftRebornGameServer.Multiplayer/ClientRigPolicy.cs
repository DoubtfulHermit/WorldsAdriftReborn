using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
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
        /// Root-name prefix of the full local rig. Present ONLY so the one
        /// remaining name-based check in the codebase has a single definition;
        /// no new code should consult it. See
        /// <see cref="TreatAsLocalForPlayerVisualizer"/>.
        /// </summary>
        public const string FullRigRootPrefix = "Traveller@Player";

        /// <summary>
        /// Whether PlayerVisualizer_Patch lets the game's own FixedUpdate run
        /// (local rig) instead of forcing the remote global-position branch.
        ///
        /// This still ORs in a name check, which is the rule-11 violation the
        /// rest of the codebase eliminated: see ClientRigPolicyTests for the
        /// skipped test that pins the intended behaviour. Behaviour is preserved
        /// here as-is; changing it is a fix, not a test pass.
        /// </summary>
        public static bool TreatAsLocalForPlayerVisualizer(string rootName, IEnumerable<string> componentTypeNames)
        {
            if (rootName != null && rootName.StartsWith(FullRigRootPrefix))
            {
                return true;
            }
            return IsLocalRig(componentTypeNames);
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
