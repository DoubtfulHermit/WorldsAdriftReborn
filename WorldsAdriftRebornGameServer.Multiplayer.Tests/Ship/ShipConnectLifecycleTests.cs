using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Headless ownership proof for the complete remote-ship visibility lifecycle.
    /// This catches cross-service regressions without running a Unity client.
    /// </summary>
    public class ShipConnectLifecycleTests
    {
        private static readonly FixedPointPosition Spawn =
            FixedPointPosition.FromMetres(0, 0, 0);

        [Fact]
        public void Remote_domain_is_skipped_at_login_and_only_ship_interest_adds_it_on_approach()
        {
            const long hullId = 100;
            long[] members = { 101, 102, 103 };
            FixedPointPosition remote = FixedPointPosition.FromMetres(5000, 0, 0);
            string hullKey = BuiltShipPlacement.HullKey(2);
            string deckKey = BuiltShipPlacement.DeckKey(2, 0);
            const string mountedKey = "loose-part:12:sail";

            Assert.False(ConnectInterestPolicy.IsInitial(hullKey, false, true, true,
                Spawn, remote, 45, 800));
            Assert.False(ConnectInterestPolicy.IsInitial(deckKey, false, true, true,
                Spawn, remote, 45, 800));
            Assert.False(ConnectInterestPolicy.IsInitial(mountedKey, true, true, true,
                Spawn, remote, 45, 800));

            Assert.False(RuntimeEntityCatchupPolicy.ShouldQueue(hullKey, true, false,
                retired: false, shipDomainManaged: true));
            Assert.False(RuntimeEntityCatchupPolicy.ShouldQueue(deckKey, true, false,
                retired: false, shipDomainManaged: true));
            Assert.False(RuntimeEntityCatchupPolicy.ShouldQueue(mountedKey, true, false,
                retired: false, shipDomainManaged: true));

            var sent = new EntitySendLedger<int>();
            const int peer = 7;
            Assert.False(sent.WasSent(peer, hullId));
            Assert.False(ShipDomainInterestPolicy.ShouldBeLoaded(
                rootLoaded: false, protectedByLocalInteraction: false, hasAnyCrew: false,
                Spawn, remote, loadRadiusMetres: 800, unloadRadiusMetres: 1000));

            FixedPointPosition approached = FixedPointPosition.FromMetres(790, 0, 0);
            Assert.True(ShipDomainInterestPolicy.ShouldBeLoaded(
                rootLoaded: false, protectedByLocalInteraction: false, hasAnyCrew: false,
                Spawn, approached, loadRadiusMetres: 800, unloadRadiusMetres: 1000));
            Assert.Equal(new long[] { hullId, 101, 102, 103 },
                ShipDomainInterestPolicy.AddOrder(hullId, members));

            foreach (long entityId in ShipDomainInterestPolicy.AddOrder(hullId, members))
                sent.MarkSent(peer, entityId);
            Assert.True(sent.WasSent(peer, hullId));
            Assert.All(members, id => Assert.True(sent.WasSent(peer, id)));
        }
    }
}
