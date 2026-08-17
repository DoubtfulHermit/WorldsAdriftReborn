using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Placement
{
    /// <summary>
    /// The stateful half of station pickup: the reservation that makes "the first
    /// pickup event wins, a concurrent second is rejected" true (no duplicate
    /// item), the rollback that keeps a station placed when the grant fails, and
    /// the picked-up tombstone the serializer seeds late joiners from.
    /// </summary>
    public class StationPickupLedgerTests
    {
        private const long Station = 4200;
        private const long PlayerA = 1;
        private const long PlayerB = 2;

        [Fact]
        public void A_fresh_station_is_neither_picked_up_nor_reserved()
        {
            var ledger = new StationPickupLedger();
            Assert.False(ledger.IsPickedUp(Station));
            Assert.False(ledger.IsReservedByOther(Station, PlayerA));
        }

        [Fact]
        public void The_first_reservation_wins_and_a_concurrent_second_is_rejected()
        {
            var ledger = new StationPickupLedger();
            Assert.True(ledger.Reserve(Station, PlayerA));

            // The concurrent second event - same drain, other player - must lose.
            Assert.False(ledger.Reserve(Station, PlayerB));
            Assert.True(ledger.IsReservedByOther(Station, PlayerB));
            Assert.False(ledger.IsReservedByOther(Station, PlayerA));
        }

        [Fact]
        public void Re_reserving_while_holding_the_reservation_succeeds()
        {
            var ledger = new StationPickupLedger();
            Assert.True(ledger.Reserve(Station, PlayerA));
            Assert.True(ledger.Reserve(Station, PlayerA));
        }

        [Fact]
        public void Rollback_releases_the_reservation_so_the_station_stays_pickable()
        {
            var ledger = new StationPickupLedger();
            Assert.True(ledger.Reserve(Station, PlayerA));
            Assert.True(ledger.Rollback(Station, PlayerA));

            Assert.False(ledger.IsPickedUp(Station));
            Assert.True(ledger.Reserve(Station, PlayerB));
        }

        [Fact]
        public void Only_the_holder_can_roll_back()
        {
            var ledger = new StationPickupLedger();
            Assert.True(ledger.Reserve(Station, PlayerA));
            Assert.False(ledger.Rollback(Station, PlayerB));
            Assert.True(ledger.IsReservedByOther(Station, PlayerB));
        }

        [Fact]
        public void Rolling_back_an_unreserved_station_is_a_no_op()
        {
            var ledger = new StationPickupLedger();
            Assert.False(ledger.Rollback(Station, PlayerA));
        }

        [Fact]
        public void Commit_marks_the_station_picked_up_and_blocks_every_later_reserve()
        {
            var ledger = new StationPickupLedger();
            Assert.True(ledger.Reserve(Station, PlayerA));
            Assert.True(ledger.Commit(Station, PlayerA));

            Assert.True(ledger.IsPickedUp(Station));
            Assert.False(ledger.Reserve(Station, PlayerA));
            Assert.False(ledger.Reserve(Station, PlayerB));
        }

        [Fact]
        public void Only_the_holder_can_commit()
        {
            var ledger = new StationPickupLedger();
            Assert.True(ledger.Reserve(Station, PlayerA));
            Assert.False(ledger.Commit(Station, PlayerB));
            Assert.False(ledger.IsPickedUp(Station));
        }

        [Fact]
        public void Committing_without_a_reservation_is_refused()
        {
            var ledger = new StationPickupLedger();
            Assert.False(ledger.Commit(Station, PlayerA));
            Assert.False(ledger.IsPickedUp(Station));
        }

        [Fact]
        public void Stations_are_independent()
        {
            var ledger = new StationPickupLedger();
            Assert.True(ledger.Reserve(Station, PlayerA));
            Assert.True(ledger.Reserve(Station + 1, PlayerB));
            Assert.True(ledger.Commit(Station, PlayerA));

            Assert.True(ledger.IsPickedUp(Station));
            Assert.False(ledger.IsPickedUp(Station + 1));
        }
    }
}
