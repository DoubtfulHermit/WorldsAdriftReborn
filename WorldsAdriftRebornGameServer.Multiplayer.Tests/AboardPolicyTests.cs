using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The memoryless "aboard ship X or not" decision over a RESOLVED 1073 state.
    /// The accumulation of deltas and the board/leave edges are AboardTracker; this
    /// pins the decision itself.
    /// </summary>
    public class AboardPolicyTests
    {
        private static ShipMembership Ship(long hull)
        {
            ShipMembership m = new ShipMembership();
            m.Register(hull, hull);
            return m;
        }

        [Fact]
        public void Attached_to_a_ship_surface_is_aboard_that_ship()
        {
            AboardVerdict v = AboardPolicy.Evaluate(
                relativeToKnown: true, relativeTo: 100, relativeBias: 1f, Ship(100));

            Assert.True(v.IsAboard);
            Assert.Equal(100, v.ShipRootEntityId);
        }

        [Fact]
        public void Attached_to_the_island_is_not_aboard()
        {
            // A player on Haven ALSO sends relativeBias 1 and a valid relativeTo -
            // the island entity, which is not in the membership map. The id match,
            // not the bias, is what rejects it.
            AboardVerdict v = AboardPolicy.Evaluate(
                relativeToKnown: true, relativeTo: 5 /* island */, relativeBias: 1f, Ship(100));

            Assert.False(v.IsAboard);
        }

        [Fact]
        public void Free_falling_over_a_ship_is_not_aboard()
        {
            // relativeBias 0 = not attached, even if relativeTo somehow named the
            // ship. The client sends bias 0 with InvalidEntityId when free.
            Assert.False(AboardPolicy.Evaluate(true, 100, 0f, Ship(100)).IsAboard);
            Assert.False(AboardPolicy.Evaluate(true, -1, 0f, Ship(100)).IsAboard);
        }

        [Fact]
        public void A_bias_exactly_at_the_threshold_is_not_attached()
        {
            // Strictly greater than 0.5, matching the client's own
            // SetPlayersInitialPosition test (relativeBias > 0.5).
            Assert.Equal(0.5f, AboardPolicy.AttachedBiasThreshold);
            Assert.False(AboardPolicy.Evaluate(true, 100, 0.5f, Ship(100)).IsAboard);
            Assert.True(AboardPolicy.Evaluate(true, 100, 0.51f, Ship(100)).IsAboard);
        }

        [Fact]
        public void Before_any_relative_to_is_known_the_player_is_not_aboard()
        {
            Assert.False(AboardPolicy.Evaluate(
                relativeToKnown: false, relativeTo: 0, relativeBias: 1f, Ship(100)).IsAboard);
        }

        [Fact]
        public void The_second_of_two_ships_is_told_apart()
        {
            ShipMembership m = new ShipMembership();
            m.Register(100, 100);
            m.Register(200, 200);

            Assert.Equal(100, AboardPolicy.Evaluate(true, 100, 1f, m).ShipRootEntityId);
            Assert.Equal(200, AboardPolicy.Evaluate(true, 200, 1f, m).ShipRootEntityId);
        }
    }
}
