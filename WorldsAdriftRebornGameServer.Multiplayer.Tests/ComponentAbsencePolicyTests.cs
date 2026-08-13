using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// "This entity does not have that component" as a first-class answer, and
    /// the one way it could do harm.
    ///
    /// The harm is not that the wrong id gets omitted - that would show up the
    /// first time somebody played. It is that a genuinely UNHANDLED id gets
    /// swallowed by the same quiet path, because then a new entity type stops
    /// announcing itself and the next person spends a day on it. Every "loud"
    /// test below exists for that, not for tidiness.
    /// </summary>
    public class ComponentAbsencePolicyTests
    {
        // ------------------------------------------------------------------
        // Membership
        // ------------------------------------------------------------------

        [Fact]
        public void WeatherCellState_is_absent_because_every_entity_we_spawn_shares_one_500m_cell()
        {
            // 1139 is the storm. Five entities on a 60 m island all floor into
            // cell (34,-3), Cantor id 2857; four of them lose that dictionary
            // race every frame forever, ~197 error lines a second with a stack
            // trace each. Not serving it is the fix.
            Assert.True(ComponentAbsencePolicy.IsKnownAbsent(1139));
            Assert.Equal(1139u, ComponentAbsencePolicy.WeatherCellStateComponentId);
        }

        [Fact]
        public void RadialStormState_is_absent_because_nothing_we_spawn_is_a_storm()
        {
            Assert.True(ComponentAbsencePolicy.IsKnownAbsent(1269));
            Assert.Equal(1269u, ComponentAbsencePolicy.RadialStormStateComponentId);
        }

        [Fact]
        public void The_set_is_exactly_the_declared_ids()
        {
            // A guard on growth. Adding an id here is a decision about what our
            // entities ARE, and it must not happen by drive-by edit - anything
            // that is merely unseeded belongs in the loud path instead. The two
            // weather ids (1139/1269) plus the four loose-ship-part physics/cosmetic
            // states this server authors for no entity (1257/1121/1225/1235), plus the
            // helm's ShipControlInput (1111) - the pilot-seat control input this server
            // does not simulate - plus a ship entity's UidState (1294) and
            // ShipAtlasPulseState (1306), both off any render/lift path so the ship
            // hull's interest batch serializes instead of dropping on them.
            Assert.Equal(
                // 1294 UidState left this set on purpose: it is SERVED now (uid = entity
                // id), because the player's own movement path reads UidVisualizer.Uid
                // every tick regardless of enablement and NRE'd when never injected.
                new uint[] { 1139, 1269, 1257, 1121, 1225, 1235, 1111, 1306 },
                ComponentAbsencePolicy.KnownAbsentComponentIds);
        }

        [Theory]
        [InlineData(1257u)] // ParentingMassAdderState
        [InlineData(1121u)] // OriginalMassState
        [InlineData(1225u)] // LightningStrikableState
        [InlineData(1235u)] // DetachFromParentWhenUnderHealthThresholdState
        [InlineData(1111u)] // ShipControlInput (helm pilot seat)
        [InlineData(1306u)] // ShipAtlasPulseState (ship entity, cosmetic core pulse)
        public void Loose_ship_part_physics_states_are_absent_so_a_part_checkout_is_clean(uint componentId)
        {
            // The crafted loose part (and a built hull) bakes visualizers that request
            // these over interest, but this server simulates none of that physics and
            // seeds them for no entity. Declaring them absent stops the "[ToDo] unhandled
            // component id ... (entity NN)" on every part/hull checkout and removes any
            // batch-drop risk; the reader visualizers just stay disabled, and none is on
            // the lift path.
            Assert.True(ComponentAbsencePolicy.IsKnownAbsent(componentId));
        }

        [Fact]
        public void The_new_absent_ids_have_constants_and_names()
        {
            Assert.Equal(1257u, ComponentAbsencePolicy.ParentingMassAdderStateComponentId);
            Assert.Equal(1121u, ComponentAbsencePolicy.OriginalMassStateComponentId);
            Assert.Equal(1225u, ComponentAbsencePolicy.LightningStrikableStateComponentId);
            Assert.Equal(1235u, ComponentAbsencePolicy.DetachFromParentWhenUnderHealthThresholdStateComponentId);
            Assert.Equal(1111u, ComponentAbsencePolicy.ShipControlInputComponentId);
            Assert.Equal(1294u, ComponentAbsencePolicy.UidStateComponentId);
            Assert.Equal(1306u, ComponentAbsencePolicy.ShipAtlasPulseStateComponentId);

            Assert.Equal("ParentingMassAdderState", ComponentAbsencePolicy.NameOf(1257));
            Assert.Equal("OriginalMassState", ComponentAbsencePolicy.NameOf(1121));
            Assert.Equal("LightningStrikableState", ComponentAbsencePolicy.NameOf(1225));
            Assert.Equal("DetachFromParentWhenUnderHealthThresholdState", ComponentAbsencePolicy.NameOf(1235));
            Assert.Equal("ShipControlInput", ComponentAbsencePolicy.NameOf(1111));
            Assert.Equal("UidState", ComponentAbsencePolicy.NameOf(1294));
            Assert.Equal("ShipAtlasPulseState", ComponentAbsencePolicy.NameOf(1306));
        }

        [Fact]
        public void The_lift_path_components_are_never_absent()
        {
            // ShipPartVisualizer's own [Require] set - what actually makes a loose part
            // render and liftable - must stay served. If any of these ever landed in the
            // known-absent set the part would go inert, the exact silent failure the set
            // is designed to avoid.
            foreach (uint id in new uint[] { 8066, 1120, 190602, 190601, 1016, 1013 })
            {
                Assert.False(ComponentAbsencePolicy.IsKnownAbsent(id),
                    "lift-path component " + id + " must never be declared known-absent");
            }
        }

        [Theory]
        [InlineData(190602u)]  // TransformState - the one field that places anything
        [InlineData(8065u)]    // Blueprint
        [InlineData(1041u)]    // IslandState
        [InlineData(1073u)]    // ClientAuthoritativePlayerState
        [InlineData(1081u)]    // InventoryState
        [InlineData(190607u)]  // TeleportRequestState
        [InlineData(1209u)]    // CustomShipHullState
        public void Components_we_actually_serve_are_not_absent(uint componentId)
        {
            Assert.False(ComponentAbsencePolicy.IsKnownAbsent(componentId));
        }

        [Fact]
        public void Nothing_the_server_relies_on_can_be_declared_absent()
        {
            // The realistic catastrophe: an id lands in the set that some other
            // policy is simultaneously seeding, granting authority over or
            // mirroring. The entity then renders and does nothing, with no
            // error anywhere - the exact failure mode the known-absent path is
            // designed to be silent about. Assert the sets are disjoint instead
            // of trusting a reviewer to notice.
            AssertNoneAbsent(MirrorSendPolicy.InjectedComponents);
            AssertNoneAbsent(MirrorSendPolicy.AuthoritativeComponents);
            AssertNoneAbsent(MirrorSendPolicy.RemoteSeedComponents);
            AssertNoneAbsent(MirrorSendPolicy.MultitoolComponents);
            AssertNoneAbsent(WorldEntities.ShipFrameSeedComponents);
            AssertNoneAbsent(WorldEntities.ShipRecognitionSeedComponents);
            Assert.False(ComponentAbsencePolicy.IsKnownAbsent(MirrorSendPolicy.TransformStateComponentId));
            Assert.False(ComponentAbsencePolicy.IsKnownAbsent(TeleportPolicy.TeleportRequestStateComponentId));
        }

        private static void AssertNoneAbsent(IReadOnlyList<uint> componentIds)
        {
            foreach (uint componentId in componentIds)
            {
                Assert.False(
                    ComponentAbsencePolicy.IsKnownAbsent(componentId),
                    "component " + componentId + " is both relied upon and declared known-absent");
            }
        }

        // ------------------------------------------------------------------
        // The batch rule - what an omission costs the rest of the batch
        // ------------------------------------------------------------------

        [Fact]
        public void A_known_absent_component_never_drops_the_batch()
        {
            // Even under all-or-nothing, which every interest call site uses.
            // Without this, not-serving 1139 would take 190602 TransformState
            // with it and the entity would render and never move.
            Assert.False(ComponentAbsencePolicy.DropsBatch(ComponentSeedOutcome.KnownAbsent, failOnComponentInitError: true));
            Assert.False(ComponentAbsencePolicy.DropsBatch(ComponentSeedOutcome.KnownAbsent, failOnComponentInitError: false));
        }

        [Fact]
        public void An_unhandled_id_still_drops_the_batch()
        {
            // The old contract, deliberately unchanged. An id nobody predicted
            // must keep costing the caller its batch; that is what has made
            // every new entity type visible so far.
            Assert.True(ComponentAbsencePolicy.DropsBatch(ComponentSeedOutcome.UnhandledId, failOnComponentInitError: true));
            Assert.True(ComponentAbsencePolicy.DropsBatch(ComponentSeedOutcome.NoClientVtable, failOnComponentInitError: true));
            Assert.True(ComponentAbsencePolicy.DropsBatch(ComponentSeedOutcome.SerializeFailed, failOnComponentInitError: true));

            // Best-effort callers (the mirror path) still skip and carry on.
            Assert.False(ComponentAbsencePolicy.DropsBatch(ComponentSeedOutcome.UnhandledId, failOnComponentInitError: false));
        }

        [Fact]
        public void Only_a_serialized_component_goes_on_the_wire()
        {
            Assert.True(ComponentAbsencePolicy.BelongsInBatch(ComponentSeedOutcome.Serialized));
            Assert.False(ComponentAbsencePolicy.BelongsInBatch(ComponentSeedOutcome.KnownAbsent));
            Assert.False(ComponentAbsencePolicy.BelongsInBatch(ComponentSeedOutcome.UnhandledId));
            Assert.False(ComponentAbsencePolicy.BelongsInBatch(ComponentSeedOutcome.NoClientVtable));
            Assert.False(ComponentAbsencePolicy.BelongsInBatch(ComponentSeedOutcome.SerializeFailed));
        }

        // ------------------------------------------------------------------
        // The trap: the two cases must not read alike
        // ------------------------------------------------------------------

        [Fact]
        public void A_deliberate_omission_does_not_look_like_a_fault()
        {
            string line = ComponentAbsencePolicy.DescribeKnownAbsent(3, 1139);

            Assert.DoesNotContain("[error]", line);
            Assert.DoesNotContain("[ToDo]", line);
            Assert.DoesNotContain("unhandled", line);
            Assert.DoesNotContain("failed", line);

            // It still has to be findable and self-explaining: the id, the
            // entity, the name, and its own prefix to count occurrences with.
            Assert.StartsWith("[known-absent]", line);
            Assert.Contains("1139", line);
            Assert.Contains("entity 3", line);
            Assert.Contains("WeatherCellState", line);
        }

        [Fact]
        public void An_unpredicted_id_stays_loud()
        {
            string line = ComponentAbsencePolicy.DescribeUnhandled(3, 4242);

            // The historic wording, kept so old notes and greps still work.
            Assert.Contains("[ToDo] unhandled component id", line);
            Assert.Contains("4242", line);
            Assert.Contains("entity 3", line);

            // And an explicit denial, because "it printed something" is not the
            // same as "a human can tell which of the two things happened".
            Assert.Contains("NOT known-absent", line);
        }

        [Fact]
        public void The_two_lines_cannot_be_mistaken_for_each_other()
        {
            string absent = ComponentAbsencePolicy.DescribeKnownAbsent(7, 1269);
            string unhandled = ComponentAbsencePolicy.DescribeUnhandled(7, 1269);

            Assert.NotEqual(absent, unhandled);

            // Each carries the other's marker nowhere. A single grep for either
            // prefix returns exactly one class of event.
            Assert.DoesNotContain("[ToDo]", absent);
            Assert.DoesNotContain("[known-absent]", unhandled);
        }

        [Fact]
        public void The_batch_summary_says_how_many_were_left_out()
        {
            string line = ComponentAbsencePolicy.DescribeBatchOmissions(3, requested: 4, sent: 3, knownAbsent: 1);

            Assert.DoesNotContain("[error]", line);
            Assert.Contains("entity 3", line);
            Assert.Contains("3 of 4", line);
            Assert.Contains("1 omitted as known-absent", line);
        }

        [Fact]
        public void Ids_outside_the_set_are_named_by_number()
        {
            Assert.Equal("WeatherCellState", ComponentAbsencePolicy.NameOf(1139));
            Assert.Equal("RadialStormState", ComponentAbsencePolicy.NameOf(1269));
            Assert.Equal("4242", ComponentAbsencePolicy.NameOf(4242));
        }
    }
}
