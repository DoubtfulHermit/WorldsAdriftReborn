using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Placement
{
    /// <summary>
    /// The authoritative gate on a client-authored placement: the item must be
    /// real, mine, the right type, sourced from me, parentless (terrain) and
    /// finite - and within range when a range is known. Pure: no ENet, no game
    /// types.
    /// </summary>
    public class PlacementPolicyTests
    {
        private const string Shipyard = "shipyard";

        private static PlacementDecision Good(
            string? type = Shipyard,
            bool source = true,
            bool hasParent = false,
            double x = 10, double y = 0, double z = 10)
        {
            return PlacementPolicy.Evaluate(type, Shipyard, source, hasParent, x, y, z);
        }

        [Fact]
        public void A_real_owned_shipyard_on_terrain_is_accepted()
        {
            Assert.True(Good().Ok);
            Assert.Equal(PlacementOutcome.Ok, Good().Outcome);
        }

        [Fact]
        public void An_item_not_in_inventory_is_rejected()
        {
            // null type == "the server found no such item id" == also the
            // duplicate-event guard once the first event consumed it.
            Assert.Equal(PlacementOutcome.ItemNotInInventory, Good(type: null).Outcome);
        }

        [Fact]
        public void An_item_of_the_wrong_type_is_rejected()
        {
            Assert.Equal(PlacementOutcome.WrongItemType, Good(type: "wood").Outcome);
        }

        [Fact]
        public void A_source_that_is_not_the_player_is_rejected()
        {
            Assert.Equal(PlacementOutcome.SourceMismatch, Good(source: false).Outcome);
        }

        [Fact]
        public void A_named_parent_entity_is_rejected_for_terrain_placement()
        {
            Assert.Equal(PlacementOutcome.UnexpectedParent, Good(hasParent: true).Outcome);
        }

        [Theory]
        [InlineData(double.NaN, 0, 0)]
        [InlineData(0, double.PositiveInfinity, 0)]
        [InlineData(0, 0, double.NegativeInfinity)]
        public void A_non_finite_position_is_rejected(double x, double y, double z)
        {
            Assert.Equal(PlacementOutcome.NonFinitePosition, Good(x: x, y: y, z: z).Outcome);
        }

        [Fact]
        public void Distance_is_not_enforced_when_the_player_position_is_unknown()
        {
            // A point 10 km away is fine when the server has no player position to
            // measure against - the handler passes null and we do not invent one.
            PlacementDecision d = PlacementPolicy.Evaluate(
                Shipyard, Shipyard, sourceMatchesPlayer: true, hasParent: false,
                posX: 10000, posY: 0, posZ: 0);
            Assert.True(d.Ok);
        }

        [Fact]
        public void A_point_within_range_of_a_known_player_is_accepted()
        {
            PlacementDecision d = PlacementPolicy.Evaluate(
                Shipyard, Shipyard, sourceMatchesPlayer: true, hasParent: false,
                posX: 5, posY: 0, posZ: 0,
                playerX: 0, playerY: 0, playerZ: 0);
            Assert.True(d.Ok);
        }

        [Fact]
        public void A_point_beyond_range_of_a_known_player_is_rejected()
        {
            PlacementDecision d = PlacementPolicy.Evaluate(
                Shipyard, Shipyard, sourceMatchesPlayer: true, hasParent: false,
                posX: 500, posY: 0, posZ: 0,
                playerX: 0, playerY: 0, playerZ: 0);
            Assert.Equal(PlacementOutcome.TooFar, d.Outcome);
        }

        [Fact]
        public void Ordering_item_checks_precede_transform_checks()
        {
            // A wrong-type item with a non-finite position reports the type problem
            // first: the item identity is the cheaper, more fundamental reject.
            PlacementDecision d = PlacementPolicy.Evaluate(
                "wood", Shipyard, sourceMatchesPlayer: true, hasParent: false,
                posX: double.NaN, posY: 0, posZ: 0);
            Assert.Equal(PlacementOutcome.WrongItemType, d.Outcome);
        }

        // --- The generalization: placement is no longer shipyard-only. The 1017
        //     handler passes the item's OWN type as the expected type once it has
        //     confirmed the type is a registered deployable, so ANY deployable is
        //     accepted while a mismatch is still rejected.

        [Theory]
        [InlineData("makeshiftStorage")]
        [InlineData("storageContainer")]
        [InlineData("campFire")]
        [InlineData("cupboard")]
        public void Any_deployable_placed_as_its_own_type_is_accepted(string type)
        {
            // Mirrors the handler: expected == the item's own type when it is a
            // deployable. The transform/source/parent checks still run.
            PlacementDecision d = PlacementPolicy.Evaluate(
                type, type, sourceMatchesPlayer: true, hasParent: false,
                posX: 10, posY: 0, posZ: 10);
            Assert.True(d.Ok);
        }

        [Fact]
        public void An_item_that_is_not_a_registered_deployable_is_rejected()
        {
            // The handler passes a sentinel expected type for a non-deployable item,
            // so the type check fails exactly as a wrong item would.
            PlacementDecision d = PlacementPolicy.Evaluate(
                "sail", "<not-a-deployable>", sourceMatchesPlayer: true, hasParent: false,
                posX: 10, posY: 0, posZ: 10);
            Assert.Equal(PlacementOutcome.WrongItemType, d.Outcome);
        }

        // --- The parent-handling decision. A shipyard MUST go down parentless; a
        //     crafting station (Assembly Station) placed on a ship deck carries that
        //     deck/hull entity as its parent and is accepted (the server spawns it
        //     parentless at the global position and ignores the parent). This is the
        //     exact real bug: the live 1017 carried parent=3 (the ship's helm) and the
        //     old policy rejected every parent unconditionally.

        [Fact]
        public void A_crafting_station_placed_on_a_parent_is_accepted_when_parent_is_allowed()
        {
            // parentAllowed == true is what the handler passes for def.IsCraftingStation.
            PlacementDecision d = PlacementPolicy.Evaluate(
                "assemblyStation", "assemblyStation", sourceMatchesPlayer: true, hasParent: true,
                posX: 10, posY: 0, posZ: 10, parentAllowed: true);
            Assert.True(d.Ok);
            Assert.Equal(PlacementOutcome.Ok, d.Outcome);
        }

        [Fact]
        public void A_shipyard_placed_on_a_parent_is_still_rejected()
        {
            // The shipyard keeps its parentless invariant: parentAllowed defaults false,
            // so a 1017 that names a ship/hull parent (the real parent=3 case) is refused.
            PlacementDecision d = PlacementPolicy.Evaluate(
                Shipyard, Shipyard, sourceMatchesPlayer: true, hasParent: true,
                posX: 10, posY: 0, posZ: 10);
            Assert.Equal(PlacementOutcome.UnexpectedParent, d.Outcome);
        }

        [Fact]
        public void Allowing_a_parent_does_not_bypass_the_other_checks()
        {
            // parentAllowed relaxes ONLY the parent rule - type, source and finiteness
            // still gate a crafting station exactly as before.
            Assert.Equal(PlacementOutcome.NonFinitePosition, PlacementPolicy.Evaluate(
                "assemblyStation", "assemblyStation", sourceMatchesPlayer: true, hasParent: true,
                posX: double.NaN, posY: 0, posZ: 0, parentAllowed: true).Outcome);
            Assert.Equal(PlacementOutcome.SourceMismatch, PlacementPolicy.Evaluate(
                "assemblyStation", "assemblyStation", sourceMatchesPlayer: false, hasParent: true,
                posX: 10, posY: 0, posZ: 10, parentAllowed: true).Outcome);
        }

        // --- The tool-lock fix. A rejected 1017 must clear the CLIENT'S placement mode
        //     (send the 1019 StopPlacing), not merely drop the server session: the
        //     client entered placement on send and only exits on that clear, so a
        //     server-only EndSession left the player stuck in preview with every tool
        //     locked. The invariant holds for the accept path AND every reject.

        [Theory]
        [InlineData(PlacementOutcome.Ok)]
        [InlineData(PlacementOutcome.ItemNotInInventory)]
        [InlineData(PlacementOutcome.WrongItemType)]
        [InlineData(PlacementOutcome.SourceMismatch)]
        [InlineData(PlacementOutcome.UnexpectedParent)]
        [InlineData(PlacementOutcome.NonFinitePosition)]
        [InlineData(PlacementOutcome.TooFar)]
        public void Every_terminal_outcome_clears_the_clients_placement_mode(PlacementOutcome outcome)
        {
            // Would have caught the bug: the old reject path cleared only the server
            // session, so the client stayed in placement mode and tools were locked.
            Assert.True(PlacementPolicy.ClientMustLeavePlacing(outcome));
        }

        [Fact]
        public void A_rejected_parent_placement_still_clears_the_client()
        {
            // The exact failing case (UnexpectedParent) must clear the client, so a
            // shipyard aimed at a ship no longer strands the player.
            PlacementDecision d = PlacementPolicy.Evaluate(
                Shipyard, Shipyard, sourceMatchesPlayer: true, hasParent: true,
                posX: 10, posY: 0, posZ: 10);
            Assert.False(d.Ok);
            Assert.True(PlacementPolicy.ClientMustLeavePlacing(d.Outcome));
        }
    }
}
