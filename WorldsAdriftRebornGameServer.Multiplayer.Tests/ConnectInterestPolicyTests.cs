using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class ConnectInterestPolicyTests
    {
        private static readonly FixedPointPosition Spawn =
            FixedPointPosition.FromMetres(0, 0, 0);

        [Fact]
        public void Built_ship_domain_uses_ship_radius_and_remote_members_are_not_initial()
        {
            FixedPointPosition remote = FixedPointPosition.FromMetres(801, 0, 0);
            string hull = BuiltShipPlacement.HullKey(4);
            string deck = BuiltShipPlacement.DeckKey(4, 2);

            Assert.True(ConnectInterestPolicy.IsGateable(hull, false, false));
            Assert.True(ConnectInterestPolicy.IsGateable(deck, false, false));
            Assert.Equal(800, ConnectInterestPolicy.RadiusFor(deck, false, 120, 800));
            Assert.False(ConnectInterestPolicy.IsInitial(hull, false, true, false,
                Spawn, remote, 120, 800));
            Assert.False(ConnectInterestPolicy.IsInitial(deck, false, true, false,
                Spawn, remote, 120, 800));
        }

        [Fact]
        public void Nearby_hull_and_mounted_part_are_initial_as_one_ship_domain()
        {
            FixedPointPosition nearby = FixedPointPosition.FromMetres(799, 0, 0);

            Assert.True(ConnectInterestPolicy.IsInitial(
                BuiltShipPlacement.HullKey(1), false, true, false,
                Spawn, nearby, 120, 800));
            Assert.True(ConnectInterestPolicy.IsInitial(
                "loose-part:7:Helm01", true, true, false,
                Spawn, nearby, 120, 800));
            Assert.True(ConnectInterestPolicy.IsGateable(
                "loose-part:7:Helm01", true, false));
        }

        [Fact]
        public void Free_loose_parts_keep_the_existing_initial_rule()
        {
            FixedPointPosition remote = FixedPointPosition.FromMetres(5000, 0, 0);
            const string key = "loose-part:7:Helm01";

            Assert.False(ConnectInterestPolicy.IsGateable(key, false, true));
            Assert.True(ConnectInterestPolicy.IsInitial(key, false, true, true,
                Spawn, remote, 120, 800));
        }

        [Fact]
        public void Resources_use_the_tighter_resource_radius_only_when_enabled()
        {
            FixedPointPosition atTwoHundred = FixedPointPosition.FromMetres(200, 0, 0);
            const string key = "tree-haven-42";

            Assert.True(ConnectInterestPolicy.IsGateable(key, false, true));
            Assert.Equal(120, ConnectInterestPolicy.RadiusFor(key, false, 120, 800));
            Assert.False(ConnectInterestPolicy.IsInitial(key, false, true, true,
                Spawn, atTwoHundred, 120, 800));
            Assert.True(ConnectInterestPolicy.IsInitial(key, false, true, false,
                Spawn, atTwoHundred, 120, 800));
        }

        [Theory]
        [InlineData("global")]
        [InlineData("placed-shipyard:0")]
        public void Essential_non_spatial_entities_keep_the_barrier_rule(string key)
        {
            Assert.False(ConnectInterestPolicy.IsGateable(key, false, true));
            Assert.True(ConnectInterestPolicy.IsInitial(key, false, true, true,
                Spawn, FixedPointPosition.FromMetres(50000, 0, 0), 120, 800));
        }
    }
}
