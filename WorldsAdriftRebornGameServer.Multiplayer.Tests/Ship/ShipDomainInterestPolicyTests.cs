using WorldsAdriftRebornGameServer.Multiplayer;
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
        public void Ship_radii_default_to_island_scale_and_remain_hysteretic()
        {
            Assert.Equal(800d, ShipDomainInterestPolicy.LoadRadiusFrom(null));
            Assert.Equal(1000d, ShipDomainInterestPolicy.UnloadRadiusFrom(null, 800d));
            Assert.Equal(1200d, ShipDomainInterestPolicy.LoadRadiusFrom("1200"));
            Assert.Equal(1300d, ShipDomainInterestPolicy.UnloadRadiusFrom(null, 1200d));
            Assert.Equal(1200d, ShipDomainInterestPolicy.UnloadRadiusFrom("900", 1200d));
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

        [Fact]
        public void Reconcile_preserves_an_in_flight_asset_for_the_same_head_add()
        {
            Assert.Equal(181,
                ShipDomainInterestPolicy.AssetRequestAfterReconcile(181, 181));
            Assert.Equal(0,
                ShipDomainInterestPolicy.AssetRequestAfterReconcile(181, 182));
            Assert.Equal(0,
                ShipDomainInterestPolicy.AssetRequestAfterReconcile(181, null));
        }

        [Fact]
        public void Readd_executes_after_remove_but_duplicate_lifecycle_actions_do_not()
        {
            Assert.True(ShipDomainInterestPolicy.ShouldExecute(add: false, checkedOut: true));
            Assert.False(ShipDomainInterestPolicy.ShouldExecute(add: false, checkedOut: false));
            Assert.True(ShipDomainInterestPolicy.ShouldExecute(add: true, checkedOut: false));
            Assert.False(ShipDomainInterestPolicy.ShouldExecute(add: true, checkedOut: true));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        public void Recall_refresh_unloads_only_a_current_checkout(
            bool refreshRequested, bool rootCheckedOut, bool expected)
        {
            Assert.Equal(expected,
                ShipDomainInterestPolicy.RecallRefreshForcesUnload(
                    refreshRequested, rootCheckedOut));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        public void Recall_is_retained_for_every_capable_peer_holding_the_old_hull(
            bool removeSupported, bool rootCheckedOut, bool expected)
        {
            Assert.Equal(expected,
                ShipDomainInterestPolicy.ShouldQueueRecallRefresh(
                    removeSupported, rootCheckedOut));
        }

        [Fact]
        public void Two_peers_have_independent_checkout_and_returning_owner_readds()
        {
            var sent = new EntitySendLedger<int>();
            const int ownerPeer = 1;
            const int observerPeer = 2;
            const long hullEntityId = 181;
            sent.MarkSent(ownerPeer, hullEntityId);
            sent.MarkSent(observerPeer, hullEntityId);

            FixedPointPosition hull = Origin;
            FixedPointPosition ownerFar = FixedPointPosition.FromMetres(250, 0, 0);
            FixedPointPosition observerNear = FixedPointPosition.FromMetres(25, 0, 0);

            bool unloadOwner = !ShipDomainInterestPolicy.ShouldBeLoaded(
                rootLoaded: true, protectedByLocalInteraction: false, hasAnyCrew: false,
                ownerFar, hull, loadRadiusMetres: 100, unloadRadiusMetres: 200);
            bool retainObserver = ShipDomainInterestPolicy.ShouldBeLoaded(
                rootLoaded: true, protectedByLocalInteraction: false, hasAnyCrew: false,
                observerNear, hull, loadRadiusMetres: 100, unloadRadiusMetres: 200);

            Assert.True(unloadOwner);
            Assert.True(retainObserver);
            Assert.True(ShipDomainInterestPolicy.ShouldExecute(add: false,
                checkedOut: sent.WasSent(ownerPeer, hullEntityId)));
            sent.ForgetEntity(ownerPeer, hullEntityId);
            Assert.False(sent.WasSent(ownerPeer, hullEntityId));
            Assert.True(sent.WasSent(observerPeer, hullEntityId));

            // Cleanup changes only the owner's peer-keyed ledger. On return the
            // owner is absent and inside load radius, while the observer remains
            // continuously checked out and eligible for motion throughout.
            bool returningOwnerLoads = ShipDomainInterestPolicy.ShouldBeLoaded(
                rootLoaded: false, protectedByLocalInteraction: false, hasAnyCrew: false,
                observerNear, hull, loadRadiusMetres: 100, unloadRadiusMetres: 200);
            Assert.True(returningOwnerLoads);
            Assert.True(ShipDomainInterestPolicy.ShouldExecute(add: true,
                checkedOut: sent.WasSent(ownerPeer, hullEntityId)));
            Assert.False(ShipDomainInterestPolicy.ShouldExecute(add: true,
                checkedOut: sent.WasSent(observerPeer, hullEntityId)));
            Assert.True(ShipUpdateVisibilityPolicy.ShouldPublish(
                targetCheckedOut: true, isPilot: false, isAboard: false,
                observerNear, hull, radiusMetres: 100));
        }
    }
}
