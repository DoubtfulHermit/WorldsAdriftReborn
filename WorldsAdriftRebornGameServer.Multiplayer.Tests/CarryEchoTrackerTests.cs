using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// WHEN the server echoes a player's own 1073 relativeTo back to it so the
    /// client-side ship carry can arm. The whole point is to fire on a board/leave
    /// EDGE and stay silent otherwise - echoing every frame would fight the owner's
    /// own movement prediction. See CarryEchoTracker for the mechanism.
    /// </summary>
    public class CarryEchoTrackerTests
    {
        private const ulong Player = 1;
        private const ulong Other = 2;
        private const long Hull = 100;
        private const long Deck = 101;
        private const long Invalid = 0; // stands in for InvalidEntityId in these tests

        // An update that CARRIED a relativeTo (a board/leave edge on the wire).
        private static CarryEchoDecision Carried(CarryEchoTracker t, ulong p, long id,
            AboardTransition? transition = null, bool deferInvalid = false) =>
            t.Observe(p, transition ?? AboardTransition.NoChange,
                relativeToPresent: true, relativeTo: id,
                deferInvalidForShipContactGap: deferInvalid);

        // An update that carried NO relativeTo (a position/bone/timestamp tick).
        private static CarryEchoDecision NoField(CarryEchoTracker t, ulong p) =>
            t.Observe(p, AboardTransition.NoChange,
                relativeToPresent: false, relativeTo: 0,
                deferInvalidForShipContactGap: false);

        [Fact]
        public void First_board_echoes_the_reported_id()
        {
            CarryEchoTracker t = new CarryEchoTracker();

            CarryEchoDecision d = Carried(t, Player, Hull);

            Assert.True(d.ShouldEcho);
            Assert.Equal(Hull, d.RelativeTo);
        }

        [Fact]
        public void Same_root_surface_switch_echoes_the_EXACT_new_contact_id()
        {
            // The client arms only when the echoed id resolves to the same GameObject
            // it already ground-chose. Standing on the deck must echo the DECK id,
            // never the hull it belongs to.
            CarryEchoTracker t = new CarryEchoTracker();

            Carried(t, Player, Hull,
                new AboardTransition(AboardChange.Boarded, Hull, 0));
            CarryEchoDecision d = Carried(t, Player, Deck);

            Assert.True(d.ShouldEcho);
            Assert.Equal(Deck, d.RelativeTo);
        }

        [Fact]
        public void Same_id_again_does_not_re_echo()
        {
            // Standing still: the client re-asserts the same relativeTo. Echoing it
            // again is the per-frame spam that would rubber-band the owner.
            CarryEchoTracker t = new CarryEchoTracker();
            Carried(t, Player, Hull);

            for (int i = 0; i < 10; i++)
            {
                Assert.False(Carried(t, Player, Hull).ShouldEcho);
            }
        }

        [Fact]
        public void Updates_without_a_relativeTo_field_are_a_no_op()
        {
            // Most updates while standing on a deck carry only position/bone/timestamp
            // and no relativeTo. Those can neither arm nor disarm and must stay silent,
            // WITHOUT disturbing the last-echoed baseline.
            CarryEchoTracker t = new CarryEchoTracker();
            Carried(t, Player, Hull);

            for (int i = 0; i < 5; i++)
            {
                Assert.False(NoField(t, Player).ShouldEcho);
            }

            // The baseline survived: re-asserting the same hull still dedupes.
            Assert.False(Carried(t, Player, Hull).ShouldEcho);
        }

        [Fact]
        public void Leaving_echoes_the_invalid_transition_to_disarm()
        {
            // Stepping off must echo relativeTo = Invalid so HandleRelativeToUpdate
            // nulls the client's RelativeGameObject and the carry disarms.
            CarryEchoTracker t = new CarryEchoTracker();
            Carried(t, Player, Hull);

            CarryEchoDecision leave = Carried(t, Player, Invalid);

            Assert.True(leave.ShouldEcho);
            Assert.Equal(Invalid, leave.RelativeTo);
        }

        [Fact]
        public void Brief_invalid_ship_contact_is_silent_until_semantic_leave_is_confirmed()
        {
            CarryEchoTracker t = new CarryEchoTracker();
            Carried(t, Player, Deck,
                new AboardTransition(AboardChange.Boarded, Hull, 0));

            Assert.False(Carried(t, Player, Invalid, deferInvalid: true).ShouldEcho);

            CarryEchoDecision leave = t.Observe(Player,
                new AboardTransition(AboardChange.Disembarked, 0, Hull),
                relativeToPresent: false, relativeTo: 0,
                deferInvalidForShipContactGap: false);
            Assert.True(leave.ShouldEcho);
            Assert.Equal(-1, leave.RelativeTo);
        }

        [Fact]
        public void Invalid_contact_for_non_ship_ground_still_disarms_immediately()
        {
            CarryEchoTracker t = new CarryEchoTracker();
            Carried(t, Player, 500); // moving non-ship ground object

            CarryEchoDecision leave = Carried(t, Player, Invalid, deferInvalid: false);
            Assert.True(leave.ShouldEcho);
            Assert.Equal(Invalid, leave.RelativeTo);
        }

        [Fact]
        public void Stepping_between_two_surfaces_re_echoes_each_time()
        {
            // Beams -> deck -> beams: every real change of ground object re-arms
            // against the new one.
            CarryEchoTracker t = new CarryEchoTracker();

            Assert.True(Carried(t, Player, Hull).ShouldEcho);
            Assert.True(Carried(t, Player, Deck).ShouldEcho);
            Assert.True(Carried(t, Player, Hull).ShouldEcho);
        }

        [Fact]
        public void Re_boarding_the_same_ship_after_leaving_echoes_again()
        {
            CarryEchoTracker t = new CarryEchoTracker();
            Carried(t, Player, Hull);
            Carried(t, Player, Invalid); // leave

            Assert.True(Carried(t, Player, Hull).ShouldEcho); // board again
        }

        [Fact]
        public void Peers_are_tracked_independently()
        {
            CarryEchoTracker t = new CarryEchoTracker();
            Carried(t, Player, Hull);

            // A different peer boarding the same ship is that peer's first board.
            Assert.True(Carried(t, Other, Hull).ShouldEcho);
            // ...and does not disturb the first peer's dedupe.
            Assert.False(Carried(t, Player, Hull).ShouldEcho);
        }

        [Fact]
        public void Forget_resets_the_baseline_so_a_reconnect_re_echoes()
        {
            CarryEchoTracker t = new CarryEchoTracker();
            Carried(t, Player, Hull);
            Assert.False(Carried(t, Player, Hull).ShouldEcho);

            t.Forget(Player);

            Assert.True(Carried(t, Player, Hull).ShouldEcho);
        }
    }
}
