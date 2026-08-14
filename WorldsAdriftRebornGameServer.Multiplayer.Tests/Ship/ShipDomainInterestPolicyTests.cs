using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public class ShipDomainInterestPolicyTests
    {
        private static readonly FixedPointPosition Origin = FixedPointPosition.FromMetres(0, 0, 0);

        [Fact]
        public void Hysteresis_loads_near_retains_between_radii_and_unloads_far()
        {
            FixedPointPosition middle = FixedPointPosition.FromMetres(150, 0, 0);
            Assert.False(ShipDomainInterestPolicy.ShouldBeLoaded(false, false, false,
                Origin, middle, 100, 200));
            Assert.True(ShipDomainInterestPolicy.ShouldBeLoaded(true, false, false,
                Origin, middle, 100, 200));
            Assert.False(ShipDomainInterestPolicy.ShouldBeLoaded(true, false, false,
                Origin, FixedPointPosition.FromMetres(201, 0, 0), 100, 200));
        }

        [Fact]
        public void Pilot_or_passenger_protection_always_keeps_domain_loaded()
        {
            Assert.True(ShipDomainInterestPolicy.ShouldBeLoaded(false, true, false,
                Origin, FixedPointPosition.FromMetres(10000, 0, 0), 100, 200));
        }

        [Fact]
        public void Any_crew_or_pilot_keeps_ship_global_until_players_join_domain_checkout()
        {
            Assert.True(ShipDomainInterestPolicy.ShouldBeLoaded(false, false, true,
                Origin, FixedPointPosition.FromMetres(10000, 0, 0), 100, 200));
        }

        [Fact]
        public void Lifecycle_adds_root_first_and_removes_root_last()
        {
            Assert.Equal(new long[] { 10, 11, 12 },
                ShipDomainInterestPolicy.AddOrder(10, new long[] { 12, 11, 12 }));
            Assert.Equal(new long[] { 12, 11, 10 },
                ShipDomainInterestPolicy.RemoveOrder(10, new long[] { 11, 12, 11 }));
        }

        [Fact]
        public void Live_decks_and_late_restored_or_runtime_mounts_form_the_member_set()
        {
            Assert.Equal(new long[] { 11, 12, 20, 21 },
                ShipDomainInterestPolicy.Members(
                    new long[] { 12, 11 },
                    new long[] { 20, 21, 20 }));
        }

        [Theory]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        public void Late_component_interest_cannot_reseed_an_unloaded_domain_entity(
            bool managed, bool checkedOut, bool expected)
        {
            Assert.Equal(expected,
                ShipDomainInterestPolicy.MayServeComponents(managed, checkedOut));
        }
    }
}
