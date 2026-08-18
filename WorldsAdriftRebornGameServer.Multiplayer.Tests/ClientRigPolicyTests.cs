using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// docs/multiplayer.md rules 9 and 11: which rig is MINE, and who may claim
    /// a prefab singleton.
    ///
    /// The type under test is LINKED into the BepInEx mod (see
    /// WorldsAdriftReborn.csproj), so these tests exercise the code the mod
    /// actually runs rather than a copy of it.
    /// </summary>
    public class ClientRigPolicyTests
    {
        /// <summary>A full local player rig: Traveller@Player.</summary>
        private static readonly string[] FullLocalRig =
        {
            "LocalPlayerInit", "CameraProxy", "InputBehaviour", "PlayerInputSetup",
            "ClientAuthoritativePlayerMovement", "PlayerVisualizer",
            "TransformChildHierarchyBehaviour", "CharacterCustomisationVisualizer",
        };

        /// <summary>The plain Traveller the server mirrors as a remote player.</summary>
        private static readonly string[] PlainRemoteRig =
        {
            "PlayerVisualizer", "TransformChildHierarchyBehaviour",
            "CharacterCustomisationVisualizer", "RemotePlayerLayerHack",
            "RemoteRigMover", "RemoteGrappleLine", "BoneAnimationReader",
        };

        // ------------------------------------------------------------------
        // Local vs remote discrimination
        // ------------------------------------------------------------------

        [Fact]
        public void The_full_local_rig_is_recognised_as_local()
        {
            Assert.True(ClientRigPolicy.IsLocalRig(FullLocalRig));
        }

        [Fact]
        public void A_rig_named_like_the_local_player_but_carrying_no_local_components_is_still_remote()
        {
            // Name-based discrimination is what let the sweeper neutralise the
            // REAL player: it made the local rig kinematic and left the remote
            // mover driving it - "spawned in the sky, falling forever"
            // (telemetry: kinematic=True, vel=0, Y decreasing). The decision must
            // not be able to see a name at all, which is why IsLocalRig takes
            // only component names.
            Assert.False(ClientRigPolicy.IsLocalRig(PlainRemoteRig));
        }

        [Fact]
        public void A_rig_named_like_a_remote_but_carrying_local_components_is_local()
        {
            // The mirror image of the bug above: if the local rig ever gets a
            // different root name, a name check calls it remote and the sweeper
            // eats it. Components decide.
            Assert.True(ClientRigPolicy.IsLocalRig(new[] { "PlayerVisualizer", "LocalPlayerInit" }));
        }

        [Theory]
        [InlineData("LocalPlayerInit")]
        [InlineData("ClientAuthoritativePlayerMovement")]
        [InlineData("InputBehaviour")]
        [InlineData("PlayerInputSetup")]
        [InlineData("CameraProxy")]
        public void Any_single_local_only_component_identifies_the_local_rig(string marker)
        {
            Assert.True(ClientRigPolicy.IsLocalRig(new[] { "PlayerVisualizer", marker }));
        }

        [Fact]
        public void The_local_only_marker_set_is_exactly_the_agreed_one()
        {
            // These are the components the plain remote Traveller provably does
            // NOT carry. Adding a component the remote rig also has silently
            // reclassifies every remote as local and disables the sweeper.
            Assert.Equal(
                new[]
                {
                    "LocalPlayerInit", "ClientAuthoritativePlayerMovement",
                    "InputBehaviour", "PlayerInputSetup", "CameraProxy",
                },
                ClientRigPolicy.LocalOnlyComponents);
        }

        [Fact]
        public void A_rig_with_no_components_is_remote_rather_than_throwing()
        {
            Assert.False(ClientRigPolicy.IsLocalRig(Array.Empty<string>()));
        }

        [Fact]
        public void A_null_component_list_is_remote_rather_than_throwing()
        {
            // Unity hands back destroyed components as null; a NullReference in
            // a per-frame sweep would take the whole mod down.
            Assert.False(ClientRigPolicy.IsLocalRig(null!));
            Assert.False(ClientRigPolicy.IsLocalRig(new string?[] { null, null }!));
        }

        [Fact]
        public void LocalPlayer_is_not_a_local_rig_marker_because_it_is_a_scene_object()
        {
            // LocalPlayer.Instance is NOT on the Traveller prefab, so its root
            // never equals a rig root. Using it to identify "my rig" froze both
            // players.
            Assert.DoesNotContain("LocalPlayer", ClientRigPolicy.LocalOnlyComponents);
        }

        // ------------------------------------------------------------------
        // PlayerVisualizer patch gate
        // ------------------------------------------------------------------

        [Fact]
        public void PlayerVisualizer_runs_the_games_own_FixedUpdate_for_the_local_rig()
        {
            Assert.True(ClientRigPolicy.TreatAsLocalForPlayerVisualizer("Traveller@Player 1", FullLocalRig));
        }

        [Fact]
        public void PlayerVisualizer_uses_the_safe_remote_reconstruction_for_a_remote_rig()
        {
            // The Parent branch of the game's FixedUpdate is what dropped a
            // remote rig ~90km away and made it fall through the map.
            Assert.False(ClientRigPolicy.TreatAsLocalForPlayerVisualizer("Traveller 3", PlainRemoteRig));
        }

        [Fact]
        public void PlayerVisualizer_decides_local_vs_remote_by_components_only_and_never_by_name()
        {
            Assert.False(ClientRigPolicy.TreatAsLocalForPlayerVisualizer("Traveller@Player 2", PlainRemoteRig));
            Assert.True(ClientRigPolicy.TreatAsLocalForPlayerVisualizer("Traveller 7", FullLocalRig));
        }

        [Fact]
        public void A_resolved_ship_frame_with_positive_bias_uses_ship_relative_position()
        {
            Assert.Equal(
                RemotePlayerPositionBranch.ShipRelative,
                ClientRigPolicy.PositionBranchForRemote(hasRelativeObject: true, relativeBias: 1f));
        }

        [Theory]
        [InlineData(false, 1f)]
        [InlineData(true, 0f)]
        [InlineData(true, -0.1f)]
        public void An_unresolved_or_inactive_relative_frame_uses_global_position(
            bool hasRelativeObject,
            float relativeBias)
        {
            Assert.Equal(
                RemotePlayerPositionBranch.Global,
                ClientRigPolicy.PositionBranchForRemote(hasRelativeObject, relativeBias));
        }

        // ------------------------------------------------------------------
        // Keep-first singleton claiming (rule 9)
        // ------------------------------------------------------------------

        [Fact]
        public void A_second_rig_never_takes_a_live_singleton_from_the_local_player()
        {
            // Unity's [Require] gating only suppresses OnEnable/Update - Awake
            // and Start always run - so a mirrored rig would otherwise steal
            // LocalPlayer.Instance and the camera the instant it spawns.
            Assert.False(ClientRigPolicy.ShouldClaimSingleton(currentOwnerIsAlive: true, candidateIsCurrentOwner: false));
        }

        [Fact]
        public void The_current_owner_re_running_its_own_hook_is_allowed_through()
        {
            // LocalPlayer assigns the singleton in BOTH Awake and OnEnable; the
            // owner must not be blocked from its own second assignment.
            Assert.True(ClientRigPolicy.ShouldClaimSingleton(currentOwnerIsAlive: true, candidateIsCurrentOwner: true));
        }

        [Fact]
        public void A_destroyed_owner_can_be_replaced_so_a_respawn_still_works()
        {
            // Unity's overloaded == reports a destroyed object as null; keep-first
            // must not mean keep-forever.
            Assert.True(ClientRigPolicy.ShouldClaimSingleton(currentOwnerIsAlive: false, candidateIsCurrentOwner: false));
        }
    }
}
