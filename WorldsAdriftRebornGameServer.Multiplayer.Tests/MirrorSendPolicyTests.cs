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
        // The harvest path (docs/research/loop/findings-harvestable-world.md)
        // ------------------------------------------------------------------

        [Fact]
        public void Clients_are_granted_the_two_components_that_let_the_server_hear_a_chop()
        {
            // 1231 SalvagerAimerState is where the beam is pointing; 1037
            // TreeCutterState is which tree section it landed on. Both are
            // client-authoritative, so without the grant their writers never
            // resolve and nothing is ever published.
            Assert.Contains(1231u, MirrorSendPolicy.AuthoritativeComponents);
            Assert.Contains(1037u, MirrorSendPolicy.AuthoritativeComponents);
        }

        [Fact]
        public void All_three_multitool_writers_are_granted_because_two_of_three_is_worth_zero()
        {
            // 2105/2106/2002 are the [Require] WRITERS of PlayerMultitoolVisualizer,
            // and the injection system enables a visualizer only when EVERY writer
            // is injected (EntityVisualizers.AllFieldWritersInjected). Miss one and
            // the beam never charges, with no error anywhere.
            foreach (uint id in MirrorSendPolicy.MultitoolComponents)
            {
                Assert.Contains(id, MirrorSendPolicy.AuthoritativeComponents);
            }
            Assert.Equal(new uint[] { 2105, 2106, 2002 }, MirrorSendPolicy.MultitoolComponents);
        }

        [Fact]
        public void Clients_are_granted_authority_over_InteractAgentState_or_hotbar_keys_1_to_8_do_nothing()
        {
            // 1211 is the fix for dead tool-switching. InteractAgentObserver is the
            // ONLY reader of the SelectItem1..8 inputs (keys 1-8), and it carries
            // [Require] InteractAgentStateWriter. The injection system enables a
            // behaviour only once every [Require] writer is injected, and a writer
            // exists only for an authoritative component. Without the grant the
            // observer never enables, its InputSink never turns on, and pressing
            // 1-8 does nothing - the reported symptom.
            //
            // This REVERSES an earlier harvest-driven decision to keep 1211 out
            // (it also claims the left mouse button; see the AuthoritativeComponents
            // doc-comment and findings-harvest-transaction.md section 2). That
            // tradeoff touches the CHOP feature, not tool-switching, and needs a
            // live client to settle. Tool-switching does not require it here.
            Assert.Contains(1211u, MirrorSendPolicy.AuthoritativeComponents);
            Assert.Contains(MirrorSendPolicy.InteractAgentStateComponentId,
                            MirrorSendPolicy.AuthoritativeComponents);
        }

        [Fact]
        public void The_max_bolt_distance_is_non_zero_or_the_beam_never_reports_a_hit()
        {
            // SalvagerAimerObserver.IsValidHit gates on
            // AreWithinDistance(hit.point, playerPos, MaxBoltDistance). At the
            // default 0 nothing is ever in range, HitInfo stays null forever, and
            // TreeCuttingBehaviour's FinishAndSend then suppresses every send after
            // the first because nothing changes again. One 1037 packet, ever - which
            // looks exactly like the grant not working.
            Assert.True(MirrorSendPolicy.SalvagerMaxBoltDistance > 0f);

            // Not longer than the salvager's own 10 m deploy raycast
            // (PlayerMultitool._maxAimDistance), or the aimer would accept targets
            // the beam cannot reach.
            Assert.True(MirrorSendPolicy.SalvagerMaxBoltDistance <= 10f);
        }

        // ------------------------------------------------------------------
        // The injected batch, and its ORDER
        // ------------------------------------------------------------------

        [Fact]
        public void PlayerName_is_injected_before_the_multitool_writers()
        {
            // LocalPlayerInit carries [Require] PlayerNameReader, so it does not
            // enable until 1086 resolves - and until it enables there is no
            // LocalPlayer.Instance. SalvagerAimerObserver.Update opens by
            // early-returning unless LocalPlayer.Exists and
            // LocalPlayer.Instance.playerMove.Equipment.Multitool is non-null, so
            // the multitool writers are worth nothing while 1086 is outstanding.
            //
            // Asserting the ORDER rather than mere membership is the point: the
            // batch is what we control, the client's own request ordering is not.
            List<uint> injected = MirrorSendPolicy.InjectedComponents.ToList();

            int name = injected.IndexOf(MirrorSendPolicy.PlayerNameComponentId);
            Assert.True(name >= 0, "1086 must be in the injected batch at all");

            foreach (uint multitool in MirrorSendPolicy.MultitoolComponents)
            {
                int at = injected.IndexOf(multitool);
                Assert.True(at > name,
                    "1086 must land no later than " + multitool + " in the same batch");
            }
        }

        [Fact]
        public void The_injected_batch_carries_SchematicsLearnerGSimState_which_the_client_forgets_to_ask_for()
        {
            // 1080. InventoryVisualiser needs its reader and the game does not
            // reliably request it.
            Assert.Equal(1080u, MirrorSendPolicy.InjectedComponents[0]);
        }

        [Fact]
        public void The_injected_batch_is_the_authority_set_plus_exactly_four_extras()
        {
            // The injected batch and the authority grant are two different jobs
            // sharing one array; this pins how they differ so a future edit to one
            // has to be a deliberate edit to the other. 1080, 1331, 1086 and 1240 are
            // injected but NOT granted - the client must not become the writer of its
            // own name, of the schematics state, of the server's scan dedup ledger, or
            // of the server-owned known-lore reader.
            Assert.Equal(MirrorSendPolicy.AuthoritativeComponents.Count + 4,
                         MirrorSendPolicy.InjectedComponents.Count);

            foreach (uint id in MirrorSendPolicy.AuthoritativeComponents)
            {
                Assert.Contains(id, MirrorSendPolicy.InjectedComponents);
            }

            Assert.DoesNotContain(1080u, MirrorSendPolicy.AuthoritativeComponents);
            Assert.DoesNotContain(MirrorSendPolicy.ScanningAgentServerStateComponentId, MirrorSendPolicy.AuthoritativeComponents);
            Assert.DoesNotContain(MirrorSendPolicy.PlayerNameComponentId, MirrorSendPolicy.AuthoritativeComponents);
            Assert.DoesNotContain(MirrorSendPolicy.LorePiecesCollectorGsimStateComponentId, MirrorSendPolicy.AuthoritativeComponents);
        }

        [Fact]
        public void The_known_lore_reader_is_injected_but_not_granted()
        {
            // 1240 LorePiecesCollectorGsimState is the server-owned READER of the
            // player's known lore. LorePiecesCollectorVisualizer [Require]s it (as
            // _serverState) alongside the 1241 writer (already granted). Without 1240
            // checked out, _serverState is null and GetKnownPieces() throws an uncaught
            // NRE from LogbookUI.ProtectedInit, taking the whole character sheet / Tab
            // menu strip down. It must be checked out (injected) but never granted - the
            // client only reads its known-lore list; the server owns it.
            Assert.Equal(1240u, MirrorSendPolicy.LorePiecesCollectorGsimStateComponentId);
            Assert.Contains(1240u, MirrorSendPolicy.InjectedComponents);
            Assert.DoesNotContain(1240u, MirrorSendPolicy.AuthoritativeComponents);
        }

        [Fact]
        public void The_two_knowledge_writers_are_client_authoritative()
        {
            // 2107 ScannerToolPlayerState and 1334 KnowledgeClientState are the two
            // client writers of the knowledge loop; both must be granted (and so
            // injected) or the scanner and the tree-node click never publish.
            Assert.Contains(2107u, MirrorSendPolicy.AuthoritativeComponents);
            Assert.Contains(1334u, MirrorSendPolicy.AuthoritativeComponents);
            Assert.Contains(2107u, MirrorSendPolicy.InjectedComponents);
            Assert.Contains(1334u, MirrorSendPolicy.InjectedComponents);
        }

        [Fact]
        public void The_scan_dedup_ledger_is_injected_but_not_granted()
        {
            // 1331 ScanningAgentServerState is server-owned; the client reads it but
            // must never hold authority over the "already scanned" ledger.
            Assert.Contains(1331u, MirrorSendPolicy.InjectedComponents);
            Assert.DoesNotContain(1331u, MirrorSendPolicy.AuthoritativeComponents);
        }

        [Fact]
        public void The_injected_batch_has_no_duplicate_component_ids()
        {
            // A duplicate would be seeded twice in one all-or-nothing batch, which
            // for 190602 would be a teleport.
            Assert.Equal(MirrorSendPolicy.InjectedComponents.Count,
                         MirrorSendPolicy.InjectedComponents.Distinct().Count());
        }

        // ------------------------------------------------------------------
        // The relay filter
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(1231u)] // SalvagerAimerState: where my beam points
        [InlineData(1037u)] // TreeCutterState: which tree section it landed on
        [InlineData(1211u)] // InteractAgentState: what I'm looking at + my hotbar slot
        [InlineData(6910u)] // UtilitySlotActivatedState: kept off the RAW path (rate); relayed as events by its handler
        public void Local_only_cross_entity_state_is_never_relayed_to_other_players(uint componentId)
        {
            // For 1231/1037/1211 this is not a bandwidth argument:
            // RelayToOtherPlayers re-addresses every relayed update to the SENDER's
            // own entity id, which is right for a position and wrong for a payload
            // whose meaning is a reference to a THIRD entity - the tree, or the
            // entity being looked at - read by behaviours that exist only on a
            // local rig.
            //
            // 6910 is here for a DIFFERENT reason and is the odd one out: it IS
            // consumed on the remote rig (UtilitySlotActivatedVisualizer renders
            // the glider + tool-in-hand from it, live-verified) and carries no
            // cross-entity reference. It is kept off the RAW per-frame path only
            // because that path relays every ~170/s health frame; it is relayed
            // instead as low-rate bool transitions by UtilitySlotActivatedState_Handler.
            Assert.False(MirrorSendPolicy.IsRelayedToOtherPlayers(componentId));
        }

        [Theory]
        [InlineData(190602u)] // position: the whole reason the relay exists
        [InlineData(1073u)]   // bone/animation
        [InlineData(1088u)]   // appearance
        [InlineData(2105u)]   // multitool mode/visibility
        [InlineData(2106u)]   // salvager on/engaged
        [InlineData(2002u)]   // repairer on/engaged
        [InlineData(1081u)]
        public void Everything_else_including_the_beams_own_state_is_still_relayed(uint componentId)
        {
            // The three multitool components are deliberately NOT filtered: they
            // are the raw material for other players eventually SEEING someone
            // chopping, they are low-rate (they change on a mode or trigger change,
            // not when the crosshair moves), and they carry no cross-entity
            // reference to be misread.
            Assert.True(MirrorSendPolicy.IsRelayedToOtherPlayers(componentId));
        }

        [Fact]
        public void Exactly_ten_component_ids_are_filtered_out_of_the_relay()
        {
            // Sweep rather than trust a hand-picked list: widening the filter has
            // to come here first, because a silently unrelayed component is
            // invisible until two players are in the world. 6910 joined the list
            // on 2026-08-09 after its ~170/s relay bufferbloated the link; 1017
            // ItemPlacingState joined with deployable placement (a client-authored
            // confirm event realised server-side, never relayed raw); 1208
            // ShipHullAgentClientState and 1270 PlayerShipBlueprintInteractionState
            // joined with the ship-build UI - both client-authoritative, cross-entity,
            // answered by the server on 1274, never relayed raw. 1070 BuilderState and
            // 1239 PlacementToolPlayerState joined with part-mounting - both
            // client-authoritative, cross-entity (PlacePart / PickedUp name a THIRD
            // entity), realised server-side by writing the part's 8066/190602/1120.
            // 1011 IslandResourceSpawnerClientState joined with the resource-placement
            // handshake - client-authoritative on the shared ISLAND, its
            // SpawnResourcesReply realised server-side by spawning deposits every peer
            // sees, never relayed raw to another client's own island writer.
            List<uint> filtered = new List<uint>();
            for (uint id = 0; id < 200000; id++)
            {
                if (!MirrorSendPolicy.IsRelayedToOtherPlayers(id))
                {
                    filtered.Add(id);
                }
            }

            Assert.Equal(new uint[] { 1011, 1017, 1037, 1070, 1208, 1211, 1231, 1239, 1270, 6910 }, filtered);
        }

        // ------------------------------------------------------------------
        // Placed-shipyard build UI (kept out of the always-on sets; env-gated)
        // ------------------------------------------------------------------

        [Fact]
        public void ShipBuildUi_grants_authority_over_the_two_client_writers_only()
        {
            // 1208 ShipHullAgentClientState (FRAME DESIGNS visualizer's writer) and
            // 1270 PlayerShipBlueprintInteractionState (SHIP BLUEPRINTS behaviour's
            // writer). 1207 + 1274 are server-owned readers and must NOT be granted.
            Assert.Equal(new uint[] { 1208, 1270 }, MirrorSendPolicy.ShipBuildUiAuthoritativeComponents);
            Assert.DoesNotContain(1207u, MirrorSendPolicy.ShipBuildUiAuthoritativeComponents);
            Assert.DoesNotContain(1274u, MirrorSendPolicy.ShipBuildUiAuthoritativeComponents);
        }

        [Fact]
        public void ShipBuildUi_injects_all_four_so_every_require_reader_and_writer_binds()
        {
            // ShipHullAgentVisualizer [Require]s 1207 reader + 1208 writer;
            // PlayerShipBlueprintInteractionBehaviour [Require]s 1270 writer + 1274
            // reader. All four must be checked out on the player for either to resolve.
            Assert.Contains(1207u, MirrorSendPolicy.ShipBuildUiInjectedComponents);
            Assert.Contains(1208u, MirrorSendPolicy.ShipBuildUiInjectedComponents);
            Assert.Contains(1270u, MirrorSendPolicy.ShipBuildUiInjectedComponents);
            Assert.Contains(1274u, MirrorSendPolicy.ShipBuildUiInjectedComponents);
        }

        [Fact]
        public void ShipBuildUi_components_are_not_in_the_always_on_sets_so_the_feature_can_be_gated()
        {
            // They ride in only when the placement flag wires them at the setup site,
            // exactly like deployable placement - so an un-flagged server neither
            // grants the writers nor injects the four.
            foreach (uint id in new uint[] { 1207, 1208, 1270, 1274 })
            {
                Assert.DoesNotContain(id, MirrorSendPolicy.AuthoritativeComponents);
                Assert.DoesNotContain(id, MirrorSendPolicy.InjectedComponents);
            }
        }

        [Fact]
        public void ShipBuildUi_client_writers_are_never_relayed_cross_entity()
        {
            // 1208 + 1270 are client-authoritative; relaying them would re-address the
            // event to the sender's own entity on a remote rig that runs none of the
            // ship-build behaviours. The refresh is answered server-side on 1274.
            Assert.False(MirrorSendPolicy.IsRelayedToOtherPlayers(1208));
            Assert.False(MirrorSendPolicy.IsRelayedToOtherPlayers(1270));
        }

        // ------------------------------------------------------------------
        // Deployable placement (kept out of the always-on sets; env-gated)
        // ------------------------------------------------------------------

        [Fact]
        public void Placement_grants_authority_over_1017_only_and_keeps_1019_server_owned()
        {
            // The client is the WRITER of the confirm event (1017) and only a READER
            // of the placement-start agent (1019). Granting 1019 would let a modified
            // client decide when its own placement starts.
            Assert.Equal(new uint[] { 1017 }, MirrorSendPolicy.PlacementAuthoritativeComponents);
            Assert.DoesNotContain(1019u, MirrorSendPolicy.PlacementAuthoritativeComponents);
        }

        [Fact]
        public void Placement_injects_both_1017_and_1019_so_the_behaviours_writer_and_reader_bind()
        {
            // ItemPlacingBehaviour [Require]s a 1017 writer AND a 1019 reader; both
            // components must be checked out on the player for either to resolve.
            Assert.Contains(1017u, MirrorSendPolicy.PlacementInjectedComponents);
            Assert.Contains(1019u, MirrorSendPolicy.PlacementInjectedComponents);
        }

        [Fact]
        public void Placement_components_are_not_in_the_always_on_sets_so_the_feature_can_be_gated()
        {
            // They ride in only when WAREBORN_PLACEMENT=1 wires them at the setup
            // site, exactly like the ferry/databank features - so an un-flagged
            // server neither grants 1017 authority nor injects the pair.
            Assert.DoesNotContain(1017u, MirrorSendPolicy.AuthoritativeComponents);
            Assert.DoesNotContain(1017u, MirrorSendPolicy.InjectedComponents);
            Assert.DoesNotContain(1019u, MirrorSendPolicy.InjectedComponents);
        }

        [Fact]
        public void The_placement_confirm_event_is_never_relayed_raw_to_other_players()
        {
            // 1017 is client-authoritative, so it reaches the relay path; relaying it
            // would re-address a PlaceItemEvent to the sender's own entity, which a
            // remote rig cannot act on. The server realises the placement instead.
            Assert.False(MirrorSendPolicy.IsRelayedToOtherPlayers(1017u));
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

        [Fact]
        public void UtilitySlotActivatedState_is_relayed_unreliably_because_it_republishes_every_active_frame()
        {
            // 6910 fires every frame a tool is active (~140/s measured). Relaying
            // it RELIABLY spiralled two-player sync on 2026-08-09 (16 KB in-flight,
            // RTT 1.7 s, peer drop) - the same congestion we removed from movement.
            // It is a per-frame flag, so a dropped packet is invisible.
            Assert.Equal(RelayReliability.Unreliable, MirrorSendPolicy.RelayReliabilityFor(6910));
        }

        [Theory]
        [InlineData(1088u)] // PlayerPropertiesState: appearance, published ONCE at spawn
        [InlineData(1098u)] // RopeControlPoints: grapple line
        [InlineData(1086u)] // PlayerName
        [InlineData(1081u)] // InventoryState
        [InlineData(0u)]
        public void Every_other_component_is_relayed_reliably_because_a_dropped_one_shot_never_comes_back(uint componentId)
        {
            Assert.Equal(RelayReliability.Reliable, MirrorSendPolicy.RelayReliabilityFor(componentId));
        }

        [Fact]
        public void Only_the_three_known_high_rate_streams_are_ever_unreliable()
        {
            // Sweep a wide id range rather than trusting a hand-picked list: a
            // future "this one is chatty too" edit has to come here first.
            // The three high-rate streams: 1073, 190602, 6910.
            var unreliable = new HashSet<uint> { 1073u, 190602u, 6910u };
            for (uint id = 0; id < 200000; id++)
            {
                if (MirrorSendPolicy.RelayReliabilityFor(id) == RelayReliability.Unreliable)
                {
                    Assert.Contains(id, unreliable);
                }
            }
        }

        // ------------------------------------------------------------------
        // Part-mount toolchain serve + grant (findings-part-mount-spec.md 4.1/4.2).
        // ------------------------------------------------------------------

        [Fact]
        public void The_part_mount_grant_is_the_two_client_writers_only()
        {
            // 1070 (the PlacePart commit) and 1239 (carry notifications) are the two the
            // client authors and MUST be granted, or BuilderObserver / the lift tool never
            // enable. 1071 is a server-owned reader and must NOT be granted.
            Assert.Equal(
                new uint[] { 1070u, 1239u },
                MirrorSendPolicy.PartMountAuthoritativeComponents.ToArray());
            Assert.DoesNotContain(1071u, MirrorSendPolicy.PartMountAuthoritativeComponents);
        }

        [Fact]
        public void The_part_mount_inject_set_covers_every_require_including_the_1071_reader()
        {
            // All three must be checked out so every [Require] resolves: 1070 (writer),
            // 1071 (BuilderVisualizer reader), 1239 (writer). A component must be seeded
            // before its updates can be handled, so injecting is what puts 1070/1239 in
            // the component map for their handlers.
            Assert.Equal(
                new uint[] { 1070u, 1071u, 1239u },
                MirrorSendPolicy.PartMountInjectedComponents.ToArray());
        }

        [Fact]
        public void The_part_mount_components_are_NOT_in_the_always_on_sets()
        {
            // They ride the placement flag (a part is only lifted at a shipyard-docked
            // ship), exactly like the ship-build UI - never the always-on grant/inject.
            foreach (uint id in new uint[] { 1070u, 1071u, 1239u })
            {
                Assert.DoesNotContain(id, MirrorSendPolicy.AuthoritativeComponents);
                Assert.DoesNotContain(id, MirrorSendPolicy.InjectedComponents);
            }
        }

        [Fact]
        public void The_client_authoritative_mount_events_are_never_relayed_to_other_players()
        {
            // 1070/1239 are client-authoritative with cross-entity payloads; relaying
            // would re-address them to the sender's own entity on a remote rig that runs
            // neither behaviour. The mount is realised server-side, not by relaying.
            Assert.False(MirrorSendPolicy.IsRelayedToOtherPlayers(1070u));
            Assert.False(MirrorSendPolicy.IsRelayedToOtherPlayers(1239u));
        }
    }
}
