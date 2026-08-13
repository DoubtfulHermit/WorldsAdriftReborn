using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Pins the sail furl ledger: registration lifecycle, the toggle, the
    /// unfurled-count flight hook, and the idempotency rule every registry in this
    /// assembly obeys (a spawn plan walks twice; the second walk must not reset a
    /// player-set state).
    /// </summary>
    public class SailsTests
    {
        [Fact]
        public void FreshMountIsFurled()
        {
            var sails = new Sails();
            Assert.True(sails.Register(sailEntityId: 10, hullEntityId: 1));
            Assert.True(sails.IsSail(10));
            Assert.False(sails.IsUnfurled(10));
        }

        [Fact]
        public void RestoreCanStartUnfurled()
        {
            var sails = new Sails();
            sails.Register(10, 1, unfurled: true);
            Assert.True(sails.IsUnfurled(10));
        }

        [Fact]
        public void ToggleFlipsAndReturnsNewState()
        {
            var sails = new Sails();
            sails.Register(10, 1);

            Assert.True(sails.Toggle(10));   // furled -> unfurled
            Assert.True(sails.IsUnfurled(10));

            Assert.False(sails.Toggle(10));  // unfurled -> furled
            Assert.False(sails.IsUnfurled(10));
        }

        [Fact]
        public void ToggleOnUnknownIdReturnsNullAndInventsNothing()
        {
            var sails = new Sails();
            Assert.Null(sails.Toggle(99));
            Assert.False(sails.IsSail(99));
            Assert.False(sails.IsUnfurled(99));
        }

        [Fact]
        public void ReRegistrationDoesNotResetPlayerSetState()
        {
            var sails = new Sails();
            sails.Register(10, 1);
            sails.Toggle(10); // player unfurled it

            // The spawn plan walks again for a second client: same id, default args.
            Assert.False(sails.Register(10, 1));
            Assert.True(sails.IsUnfurled(10)); // the unfurl survives
        }

        [Fact]
        public void UnregisterForgetsStateAndRemountStartsFurled()
        {
            var sails = new Sails();
            sails.Register(10, 1);
            sails.Toggle(10);

            Assert.True(sails.Unregister(10));
            Assert.False(sails.IsSail(10));
            Assert.False(sails.Unregister(10)); // second lift is a no-op

            // Re-mounted after a lift: fresh mount semantics, furled again.
            Assert.True(sails.Register(10, 2));
            Assert.False(sails.IsUnfurled(10));
        }

        [Fact]
        public void UnfurledCountCountsOnlyThatHullsUnfurledSails()
        {
            var sails = new Sails();
            sails.Register(10, 1);
            sails.Register(11, 1);
            sails.Register(12, 2); // another ship
            sails.Toggle(10);
            sails.Toggle(12);

            Assert.Equal(1, sails.UnfurledCountFor(1));
            Assert.Equal(1, sails.UnfurledCountFor(2));
            Assert.Equal(0, sails.UnfurledCountFor(3));

            sails.Toggle(11);
            Assert.Equal(2, sails.UnfurledCountFor(1));

            sails.Unregister(10);
            Assert.Equal(1, sails.UnfurledCountFor(1));
        }
    }
}
