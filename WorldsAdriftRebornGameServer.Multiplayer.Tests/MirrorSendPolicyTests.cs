using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The values the server puts on the wire. Every assertion here is on the
    /// VALUE itself, not on whether some mock was called: the bugs these guard
    /// against were all "the right call carrying the wrong number or string".
    /// </summary>
    public class MirrorSendPolicyTests
    {
        // ------------------------------------------------------------------
        // Prefab context (docs/multiplayer.md rule 2)
        // ------------------------------------------------------------------

        [Fact]
        public void Mirrored_remote_players_use_prefab_context_Default()
        {
            Assert.Equal("Default", MirrorSendPolicy.RemotePrefabContext);
        }

        [Fact]
        public void Mirrored_remote_players_are_never_spawned_with_context_Player_which_steals_the_camera()
        {
            // "Player" selects Traveller@Player: the FULL LOCAL RIG. Mirroring
            // with it instantiates a second local player that takes the camera
            // and the local-player identity.
            Assert.NotEqual(MirrorSendPolicy.LocalPrefabContext, MirrorSendPolicy.RemotePrefabContext);
            Assert.Equal("Player", MirrorSendPolicy.LocalPrefabContext);
        }

        [Fact]
        public void Both_local_and_remote_players_come_from_the_Traveller_prefab()
        {
            Assert.Equal("Traveller", MirrorSendPolicy.PrefabName);
        }

        // ------------------------------------------------------------------
        // Resend set (the sky-launch bug)
        // ------------------------------------------------------------------

        [Fact]
        public void AddEntity_may_be_resent_because_a_client_still_loading_the_prefab_drops_it()
        {
            Assert.True(MirrorSendPolicy.MayResend(MirrorOp.AddEntity));
        }

        [Fact]
        public void AddComponents_may_never_be_resent_because_it_reapplies_the_default_TransformState_and_launches_players_into_the_sky()
        {
            Assert.False(MirrorSendPolicy.MayResend(MirrorOp.AddComponents));
        }

        [Theory]
        [InlineData(MirrorOp.AddComponents)]
        [InlineData(MirrorOp.RelayComponentUpdate)]
        [InlineData(MirrorOp.RemoveEntity)]
        public void Only_AddEntity_is_ever_resendable(MirrorOp op)
        {
            Assert.False(MirrorSendPolicy.MayResend(op));
        }

        [Fact]
        public void Every_resendable_op_carries_no_component_data()
        {
            // The safety argument for resending at all: AddEntity carries no
            // component payload, so repeating it cannot move anyone. If a new
            // op type is ever added to the resend set, this test forces whoever
            // adds it to re-derive that argument.
            foreach (MirrorOp op in Enum.GetValues<MirrorOp>())
            {
                if (MirrorSendPolicy.MayResend(op))
                {
                    Assert.Equal(MirrorOp.AddEntity, op);
                }
            }
        }

        // ------------------------------------------------------------------
        // Remote seed (docs/multiplayer.md rule 7)
        // ------------------------------------------------------------------

        [Fact]
        public void Remote_seed_is_exactly_the_agreed_component_set()
        {
            Assert.Equal(
                new uint[] { 190602, 1086, 1081, 1088, 1073, 6910, 1098 },
                MirrorSendPolicy.RemoteSeedComponents);
        }

        [Fact]
        public void Remote_seed_carries_TransformState_or_the_avatar_never_moves()
        {
            Assert.Contains(190602u, MirrorSendPolicy.RemoteSeedComponents);
        }

        [Fact]
        public void Remote_seed_carries_both_Requires_of_CharacterCustomisationVisualizer_or_no_body_is_built()
        {
            // 1081 InventoryState and 1088 PlayerPropertiesState. Without either,
            // the visualizer that builds the visible body never enables.
            Assert.Contains(1081u, MirrorSendPolicy.RemoteSeedComponents);
            Assert.Contains(1088u, MirrorSendPolicy.RemoteSeedComponents);
        }

        [Fact]
        public void Remote_seed_never_carries_CharacterControlsData_which_means_this_is_the_character_you_control()
        {
            // 1072. Seeding it gave each client two entities carrying player
            // state and detached the camera to a top-down view with neither
            // avatar drawn.
            Assert.DoesNotContain(1072u, MirrorSendPolicy.RemoteSeedComponents);
        }

        [Fact]
        public void Remote_seed_never_carries_PilotState_which_steals_the_PilotVisualizer_singleton()
        {
            // 1109 is injected on a client's OWN entity, never on a mirror.
            Assert.DoesNotContain(1109u, MirrorSendPolicy.RemoteSeedComponents);
        }

        [Fact]
        public void Remote_seed_stays_minimal_so_visualizers_are_not_enabled_against_default_data()
        {
            // A larger seed enables visualizers whose OnEnable subscriptions then
            // throw, which kills the whole enable chain. The exact number is not
            // sacred, but a jump means someone widened the seed - go re-read
            // rule 7 before changing this.
            Assert.True(MirrorSendPolicy.RemoteSeedComponents.Count <= 8,
                "remote seed grew past 8 components; see docs/multiplayer.md rule 7");
        }

        [Fact]
        public void Remote_seed_has_no_duplicate_component_ids()
        {
            Assert.Equal(MirrorSendPolicy.RemoteSeedComponents.Count,
                         MirrorSendPolicy.RemoteSeedComponents.Distinct().Count());
        }

        // ------------------------------------------------------------------
        // Authority set (docs/multiplayer.md rule 5)
        // ------------------------------------------------------------------

        [Fact]
        public void Clients_are_granted_authority_over_TransformState_or_nobody_ever_publishes_a_position()
        {
            // A client only PUBLISHES components it holds authority over. Drop
            // 190602 and there is nothing to relay: everyone stands still.
            Assert.Contains(190602u, MirrorSendPolicy.AuthoritativeComponents);
        }

        [Fact]
        public void Clients_are_granted_authority_over_ClientAuthoritativePlayerState_or_remote_avatars_stay_in_T_pose()
        {
            // 1073's writer is authority-gated; without the grant the bone bytes
            // are never published.
            Assert.Contains(1073u, MirrorSendPolicy.AuthoritativeComponents);
        }

        [Fact]
        public void Authority_set_has_no_duplicate_component_ids()
        {
            Assert.Equal(MirrorSendPolicy.AuthoritativeComponents.Count,
                         MirrorSendPolicy.AuthoritativeComponents.Distinct().Count());
        }

        // ------------------------------------------------------------------
        // Relay reliability
        // ------------------------------------------------------------------

        [Fact]
        public void TransformState_is_relayed_unreliably_because_reliable_ordering_stalls_on_any_loss()
        {
            Assert.Equal(RelayReliability.Unreliable, MirrorSendPolicy.RelayReliabilityFor(190602));
        }

        [Fact]
        public void Bone_animation_state_is_relayed_unreliably_for_the_same_reason()
        {
            Assert.Equal(RelayReliability.Unreliable, MirrorSendPolicy.RelayReliabilityFor(1073));
        }

        [Theory]
        [InlineData(1088u)] // PlayerPropertiesState: appearance, published ONCE at spawn
        [InlineData(1098u)] // RopeControlPoints: grapple line
        [InlineData(6910u)] // UtilitySlotActivatedState: glider open/closed
        [InlineData(1086u)] // PlayerName
        [InlineData(1081u)] // InventoryState
        [InlineData(0u)]
        public void Every_other_component_is_relayed_reliably_because_a_dropped_one_shot_never_comes_back(uint componentId)
        {
            Assert.Equal(RelayReliability.Reliable, MirrorSendPolicy.RelayReliabilityFor(componentId));
        }

        [Fact]
        public void Only_the_two_known_high_rate_streams_are_ever_unreliable()
        {
            // Sweep a wide id range rather than trusting a hand-picked list: a
            // future "this one is chatty too" edit has to come here first.
            for (uint id = 0; id < 2000; id++)
            {
                if (MirrorSendPolicy.RelayReliabilityFor(id) == RelayReliability.Unreliable)
                {
                    Assert.Equal(1073u, id);
                }
            }
        }
    }
}
