using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandTerrainPeerLedgerTests
    {
        private static readonly IReadOnlyDictionary<IslandId, long> Managed =
            new Dictionary<IslandId, long>
            {
                [IslandCatalog.MentalFacilityId] = 201,
            };

        [Fact]
        public void Destination_status_covers_disabled_unknown_queued_waiting_and_ready()
        {
            var ledger = new IslandTerrainPeerLedger<string>();
            Assert.Equal(TerrainDestinationStatus.Disabled,
                ledger.RequestDestination("p", IslandCatalog.MentalFacilityId,
                    IslandCatalog.HavenId, Managed, enabled: false, assetWaiting: false));
            Assert.Equal(TerrainDestinationStatus.Unknown,
                ledger.RequestDestination("p", IslandCatalog.HighlandsHillsId,
                    IslandCatalog.HavenId, Managed, enabled: true, assetWaiting: false));
            Assert.Equal(TerrainDestinationStatus.Queued,
                ledger.RequestDestination("p", IslandCatalog.MentalFacilityId,
                    IslandCatalog.HavenId, Managed, enabled: true, assetWaiting: false));
            Assert.Equal(TerrainDestinationStatus.WaitingForAsset,
                ledger.RequestDestination("p", IslandCatalog.MentalFacilityId,
                    IslandCatalog.HavenId, Managed, enabled: true, assetWaiting: true));
            ledger.NoteLoaded("p", 201);
            Assert.Equal(TerrainDestinationStatus.Ready,
                ledger.RequestDestination("p", IslandCatalog.MentalFacilityId,
                    IslandCatalog.HavenId, Managed, enabled: true, assetWaiting: true));
        }

        [Fact]
        public void Haven_is_unconditionally_ready_without_creating_peer_state()
        {
            var ledger = new IslandTerrainPeerLedger<string>();
            Assert.Equal(TerrainDestinationStatus.Ready,
                ledger.RequestDestination("new", IslandCatalog.HavenId,
                    IslandCatalog.HavenId, Managed, enabled: true, assetWaiting: false));
            Assert.False(ledger.IsTracking("new"));
        }

        [Fact]
        public void Two_peers_never_share_loaded_terrain()
        {
            var ledger = new IslandTerrainPeerLedger<string>();
            ledger.NoteLoaded("near", 201);
            Assert.True(ledger.IsLoaded("near", 201));
            Assert.False(ledger.IsLoaded("far", 201));
        }

        [Fact]
        public void Forget_makes_reconnect_start_empty()
        {
            var ledger = new IslandTerrainPeerLedger<string>();
            ledger.NoteLoaded("peer", 201);
            ledger.RequestDestination("peer", IslandCatalog.MentalFacilityId,
                IslandCatalog.HavenId, Managed, enabled: true, assetWaiting: false);
            Assert.True(ledger.Forget("peer"));
            Assert.False(ledger.IsTracking("peer"));
            Assert.False(ledger.IsLoaded("peer", 201));
            ledger.NotePeer("peer");
            Assert.False(ledger.IsLoaded("peer", 201));
            Assert.Null(ledger.RequestedDestination("peer"));
        }

        [Fact]
        public void Proved_teleport_landing_releases_the_destination_pin()
        {
            var ledger = new IslandTerrainPeerLedger<string>();
            ledger.RequestDestination("peer", IslandCatalog.MentalFacilityId,
                IslandCatalog.HavenId, Managed, enabled: true, assetWaiting: false);

            ledger.ConfirmTeleportLanding("peer");

            Assert.Null(ledger.RequestedDestination("peer"));
        }

        [Fact]
        public void Removing_one_peer_checkout_does_not_touch_another()
        {
            var ledger = new IslandTerrainPeerLedger<int>();
            ledger.NoteLoaded(1, 201);
            ledger.NoteLoaded(2, 201);
            ledger.NoteRemoved(1, 201);
            Assert.False(ledger.IsLoaded(1, 201));
            Assert.True(ledger.IsLoaded(2, 201));
        }
    }
}
