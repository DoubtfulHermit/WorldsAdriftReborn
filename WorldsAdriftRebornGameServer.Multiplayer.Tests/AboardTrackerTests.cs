using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Who is aboard which ship, accumulated from the 1073 DELTA stream. Every
    /// rule here is one the flight publisher and abandonment timer will lean on,
    /// and the delta accumulation is the whole reason a single update cannot answer
    /// the question - see AboardSample.
    /// </summary>
    public class AboardTrackerTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
            public void Advance(TimeSpan by) => Elapsed += by;
        }

        private const long Hull = 100;
        private const long Island = 5;
        private const ulong Player = 1;

        private static ShipMembership OneShip()
        {
            ShipMembership m = new ShipMembership();
            m.Register(Hull, Hull);
            return m;
        }

        // relativeTo AND relativeBias both change - stepping onto/off a surface.
        private static AboardSample StepOnto(long groundId, float bias, bool isShip) =>
            new AboardSample(true, groundId, true, bias, true, isShip);

        // Only relativeTo changes; bias stays what it accumulated to (the real
        // shape of walking straight from a deck onto adjacent ground).
        private static AboardSample RelativeToOnly(long groundId, bool isShip) =>
            new AboardSample(true, groundId, false, 0f, true, isShip);

        // Neither aboard-bearing field changes: a position/bone/timestamp tick,
        // which is what most updates are while standing still.
        private static AboardSample PositionOnly() =>
            new AboardSample(false, 0, false, 0f, false, false);

        [Fact]
        public void Stepping_onto_the_deck_is_a_board()
        {
            AboardTracker t = new AboardTracker(OneShip());

            AboardTransition tr = t.Observe(Player, StepOnto(Hull, 1f, isShip: true));

            Assert.Equal(AboardChange.Boarded, tr.Change);
            Assert.Equal(Hull, tr.ShipRootEntityId);
            Assert.Equal(Hull, t.ShipOf(Player));
            Assert.True(t.IsAboardAnything(Player));
        }

        [Fact]
        public void Standing_still_on_the_deck_is_not_a_disembark()
        {
            // THE delta trap: after boarding, most updates carry only a position
            // and no relativeTo/bias. Those must leave the player aboard.
            AboardTracker t = new AboardTracker(OneShip());
            t.Observe(Player, StepOnto(Hull, 1f, isShip: true));

            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(AboardChange.None, t.Observe(Player, PositionOnly()).Change);
            }
            Assert.Equal(Hull, t.ShipOf(Player));
        }

        [Fact]
        public void Walking_off_onto_the_island_is_a_disembark_even_though_bias_did_not_change()
        {
            // Stepping from deck to island changes relativeTo (hull -> island) but
            // NOT relativeBias (still attached, still 1). The tracker must have
            // accumulated the bias to decide correctly against the new relativeTo.
            AboardTracker t = new AboardTracker(OneShip());
            t.Observe(Player, StepOnto(Hull, 1f, isShip: true));

            AboardTransition tr = t.Observe(Player, RelativeToOnly(Island, isShip: false));

            Assert.Equal(AboardChange.Disembarked, tr.Change);
            Assert.Equal(Hull, tr.PreviousShipRootEntityId);
            Assert.Null(t.ShipOf(Player));
        }

        [Fact]
        public void Jumping_off_into_free_fall_is_a_disembark()
        {
            // Free: relativeTo -> InvalidEntityId, bias -> 0. Both change.
            FakeClock clock = new FakeClock();
            AboardTracker t = new AboardTracker(OneShip(), clock);
            t.Observe(Player, StepOnto(Hull, 1f, isShip: true));

            // One invalid physics frame is held as a possible collider seam.
            Assert.Equal(AboardChange.None,
                t.Observe(Player, StepOnto(-1, 0f, isShip: false)).Change);
            Assert.Equal(Hull, t.ShipOf(Player));

            clock.Advance(AboardTracker.ContactGapGrace);
            AboardTransition tr = t.Observe(Player, PositionOnly());

            Assert.Equal(AboardChange.Disembarked, tr.Change);
            Assert.Equal(Hull, tr.PreviousShipRootEntityId);
        }

        [Fact]
        public void Positive_non_ship_surface_is_an_immediate_leave_even_with_zero_bias()
        {
            FakeClock clock = new FakeClock();
            AboardTracker t = new AboardTracker(OneShip(), clock);
            t.Observe(Player, StepOnto(Hull, 1f, isShip: true));

            AboardTransition tr = t.Observe(Player, StepOnto(Island, 0f, isShip: false));

            Assert.Equal(AboardChange.Disembarked, tr.Change);
            Assert.Equal(Hull, tr.PreviousShipRootEntityId);
            Assert.Null(t.ShipOf(Player));
        }

        [Fact]
        public void Brief_hull_deck_contact_gap_does_not_emit_leave_or_reboard()
        {
            FakeClock clock = new FakeClock();
            ShipMembership membership = OneShip();
            membership.Register(101, Hull); // deck
            membership.Register(102, Hull); // mounted part collider
            AboardTracker t = new AboardTracker(membership, clock);

            Assert.Equal(AboardChange.Boarded,
                t.Observe(Player, StepOnto(Hull, 1f, isShip: true)).Change);
            Assert.Equal(AboardChange.None,
                t.Observe(Player, StepOnto(-1, 0f, isShip: false)).Change);
            // The production trace's longest seam that returned to the same hull
            // was 0.79 s. It must remain one continuous aboard interval.
            clock.Advance(TimeSpan.FromMilliseconds(790));
            Assert.Equal(AboardChange.None,
                t.Observe(Player, StepOnto(101, 1f, isShip: true)).Change);
            Assert.Equal(AboardChange.None,
                t.Observe(Player, RelativeToOnly(102, isShip: true)).Change);
            Assert.Equal(Hull, t.ShipOf(Player));
        }

        [Fact]
        public void Moving_ship_contact_grace_is_one_second_but_a_real_leave_still_matures()
        {
            Assert.Equal(TimeSpan.FromSeconds(1), AboardTracker.ContactGapGrace);

            FakeClock clock = new FakeClock();
            AboardTracker t = new AboardTracker(OneShip(), clock);
            t.Observe(Player, StepOnto(Hull, 1f, isShip: true));
            t.Observe(Player, StepOnto(-1, 0f, isShip: false));
            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.Equal(AboardChange.None, t.Observe(Player, PositionOnly()).Change);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(AboardChange.Disembarked, t.Observe(Player, PositionOnly()).Change);
        }

        [Fact]
        public void Re_boarding_after_leaving_boards_again()
        {
            FakeClock clock = new FakeClock();
            AboardTracker t = new AboardTracker(OneShip(), clock);
            t.Observe(Player, StepOnto(Hull, 1f, isShip: true));
            t.Observe(Player, StepOnto(-1, 0f, isShip: false));
            clock.Advance(AboardTracker.ContactGapGrace);
            Assert.Equal(AboardChange.Disembarked, t.Observe(Player, PositionOnly()).Change);

            AboardTransition tr = t.Observe(Player, StepOnto(Hull, 1f, isShip: true));
            Assert.Equal(AboardChange.Boarded, tr.Change);
            Assert.Equal(Hull, tr.ShipRootEntityId);
            Assert.Equal(Hull, t.ShipOf(Player));
        }

        [Fact]
        public void Stepping_straight_from_one_ship_to_another_is_a_ship_change()
        {
            ShipMembership m = new ShipMembership();
            m.Register(100, 100);
            m.Register(200, 200);
            AboardTracker t = new AboardTracker(m);

            t.Observe(Player, StepOnto(100, 1f, isShip: true));
            AboardTransition tr = t.Observe(Player, RelativeToOnly(200, isShip: true));

            Assert.Equal(AboardChange.ChangedShip, tr.Change);
            Assert.Equal(200, tr.ShipRootEntityId);
            Assert.Equal(100, tr.PreviousShipRootEntityId);
            Assert.Equal(200, t.ShipOf(Player));
        }

        [Fact]
        public void The_roster_answers_who_is_aboard_ship_X()
        {
            AboardTracker t = new AboardTracker(OneShip());
            t.Observe(1, StepOnto(Hull, 1f, isShip: true));
            t.Observe(2, StepOnto(Hull, 1f, isShip: true));
            t.Observe(3, StepOnto(Island, 1f, isShip: false)); // on the island

            Assert.True(t.AnyoneAboard(Hull));
            Assert.Equal(new ulong[] { 1, 2 }, t.AboardShip(Hull).OrderBy(x => x).ToArray());
            Assert.Empty(t.AboardShip(999));
        }

        [Fact]
        public void Disconnecting_while_aboard_reports_a_disembark_and_empties_the_ship()
        {
            AboardTracker t = new AboardTracker(OneShip());
            t.Observe(Player, StepOnto(Hull, 1f, isShip: true));

            AboardTransition tr = t.Forget(Player);

            Assert.Equal(AboardChange.Disembarked, tr.Change);
            Assert.Equal(Hull, tr.PreviousShipRootEntityId);
            Assert.False(t.AnyoneAboard(Hull));
            Assert.Null(t.ShipOf(Player));
        }

        [Fact]
        public void Forgetting_a_player_who_was_not_aboard_is_a_no_op()
        {
            AboardTracker t = new AboardTracker(OneShip());
            t.Observe(Player, StepOnto(Island, 1f, isShip: false));

            Assert.Equal(AboardChange.None, t.Forget(Player).Change);
        }
    }
}
