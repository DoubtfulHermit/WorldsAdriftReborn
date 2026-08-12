using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The at-most-one guard behind the timed station craft, and the regression it must not
    /// have: "craft ONE part, then everything is blocked". The bug was a guard entry that was
    /// only released on the deferred completion's happy path, so once it leaked (an exception
    /// mid-completion, or simply never releasing) every later StartCrafting at that station was
    /// refused as a duplicate. These drive the exact server sequence natively - no game install.
    /// </summary>
    public class StationCraftGuardTests
    {
        private const long Player = 6;      // the entity id from the live repro log
        private const long Station = 2;     // "station 2" in the repro log
        private const long OtherPlayer = 7;
        private const long OtherStation = 3;

        [Fact]
        public void CraftA_thenCraftB_succeeds_after_completion()
        {
            // THE REGRESSION TEST. craft A (start -> complete), then select+fill+craft B.
            StationCraftGuard guard = new StationCraftGuard();

            // Start craft A: the slot is reserved.
            Assert.True(guard.TryBegin(Station, Player));
            Assert.True(guard.IsInProgress(Station, Player));

            // A's timer elapses and the deferred completion releases the slot.
            guard.Complete(Station, Player);
            Assert.False(guard.IsInProgress(Station, Player));

            // Select + fill happen off the guard (they touch the CraftSession, not this set),
            // then the player crafts part B - which MUST be accepted, not blocked.
            Assert.True(guard.TryBegin(Station, Player));
            Assert.True(guard.IsInProgress(Station, Player));

            // And B in turn completes cleanly, leaving nothing behind.
            guard.Complete(Station, Player);
            Assert.Equal(0, guard.Count);
        }

        [Fact]
        public void SecondStart_duringAtimer_isRejected_withoutReconsume()
        {
            // The double-consume guard: a second StartCrafting DURING A's craft window is
            // refused (TryBegin==false), so the caller never consumes materials again.
            StationCraftGuard guard = new StationCraftGuard();

            Assert.True(guard.TryBegin(Station, Player));   // A accepted
            Assert.False(guard.TryBegin(Station, Player));  // duplicate during A's timer -> rejected
            Assert.False(guard.TryBegin(Station, Player));  // still rejected while A runs
            Assert.True(guard.IsInProgress(Station, Player));

            // Only after A completes does a fresh craft get in.
            guard.Complete(Station, Player);
            Assert.True(guard.TryBegin(Station, Player));
        }

        [Fact]
        public void Abandon_onConsumeFailure_freesTheSlot_soThePlayerCanRetry()
        {
            // The consume-failure path (not enough materials) abandons the reservation, so the
            // player can immediately try again once they have the materials.
            StationCraftGuard guard = new StationCraftGuard();

            Assert.True(guard.TryBegin(Station, Player));
            guard.Abandon(Station, Player);      // TryConsumeOnly failed -> free the slot
            Assert.False(guard.IsInProgress(Station, Player));

            Assert.True(guard.TryBegin(Station, Player)); // retry accepted
        }

        [Fact]
        public void Release_isIdempotent_soBeltAndBraces_doubleRelease_isSafe()
        {
            // The deferred completion releases in a finally AND the start path releases in a
            // catch; a craft that trips both must not throw or wedge. Complete/Abandon are
            // idempotent, and a redundant release never frees an UNRELATED craft.
            StationCraftGuard guard = new StationCraftGuard();

            Assert.True(guard.TryBegin(Station, Player));
            guard.Complete(Station, Player);
            guard.Complete(Station, Player);  // idempotent - no throw
            guard.Abandon(Station, Player);   // idempotent - no throw
            Assert.False(guard.IsInProgress(Station, Player));

            // A second, unrelated in-flight craft is untouched by the redundant releases above.
            Assert.True(guard.TryBegin(OtherStation, Player));
            guard.Complete(Station, Player);  // releasing the (already-done) first craft...
            Assert.True(guard.IsInProgress(OtherStation, Player)); // ...must not free the second
        }

        [Fact]
        public void TwoPlayers_atOneStation_areIndependent()
        {
            // Keyed by (station, player): two players crafting at one bench do not block each
            // other, and each releases only its own slot.
            StationCraftGuard guard = new StationCraftGuard();

            Assert.True(guard.TryBegin(Station, Player));
            Assert.True(guard.TryBegin(Station, OtherPlayer));   // not blocked by the other player
            Assert.False(guard.TryBegin(Station, Player));       // each still one-at-a-time

            guard.Complete(Station, Player);
            Assert.False(guard.IsInProgress(Station, Player));
            Assert.True(guard.IsInProgress(Station, OtherPlayer)); // the other's craft survives
        }

        [Fact]
        public void OnePlayer_atTwoStations_isIndependent()
        {
            // One player can have a craft in flight at two different benches at once.
            StationCraftGuard guard = new StationCraftGuard();

            Assert.True(guard.TryBegin(Station, Player));
            Assert.True(guard.TryBegin(OtherStation, Player));   // second bench, same player
            Assert.Equal(2, guard.Count);

            guard.Complete(Station, Player);
            Assert.True(guard.IsInProgress(OtherStation, Player));
        }

        [Fact]
        public void ForgetPlayer_dropsAllOfThatPlayers_inFlightCrafts()
        {
            // A player who leaves mid-craft must not leave a leaked entry that a re-used id
            // would then be blocked by.
            StationCraftGuard guard = new StationCraftGuard();

            guard.TryBegin(Station, Player);
            guard.TryBegin(OtherStation, Player);
            guard.TryBegin(Station, OtherPlayer);

            guard.ForgetPlayer(Player);

            Assert.False(guard.IsInProgress(Station, Player));
            Assert.False(guard.IsInProgress(OtherStation, Player));
            Assert.True(guard.IsInProgress(Station, OtherPlayer)); // other players untouched
            Assert.Equal(1, guard.Count);
        }

        [Fact]
        public void CraftManyPartsInARow_neverWedges()
        {
            // "The player must be able to craft part after part after part."
            StationCraftGuard guard = new StationCraftGuard();

            for (int i = 0; i < 25; i++)
            {
                Assert.True(guard.TryBegin(Station, Player)); // accepted every single time
                Assert.False(guard.TryBegin(Station, Player)); // and still one-at-a-time each time
                guard.Complete(Station, Player);
            }

            Assert.Equal(0, guard.Count);
        }

        // ----- BeginGuarded: release-on-every-exit for the craft-start critical section -----

        [Fact]
        public void BeginGuarded_whenCraftStartThrows_releasesTheReservation_soTheStationIsNotWedged()
        {
            // THE HARDENING REGRESSION: the craft-start does throwing setup (e.g. resolving the
            // crafter's owner uid, or scheduling the deferred completion) AFTER reserving the guard.
            // If that throws and the reservation is NOT released, every later StartCrafting at this
            // station is refused as a duplicate forever - "craft one part, then everything blocked",
            // exactly the class of bug the user keeps hitting. BeginGuarded must release on the
            // throwing path.
            StationCraftGuard guard = new StationCraftGuard();

            InvalidOperationException boom = Assert.Throws<InvalidOperationException>(() =>
                guard.BeginGuarded(Station, Player, () =>
                {
                    // stand-in for CharacterOwnership.UidForEntity / DeferredActions.After throwing
                    throw new InvalidOperationException("owner-uid resolve failed");
                }));
            Assert.Equal("owner-uid resolve failed", boom.Message); // the fault still propagates

            // The reservation must be GONE, not leaked...
            Assert.False(guard.IsInProgress(Station, Player));
            Assert.Equal(0, guard.Count);

            // ...so the very next craft at this station is accepted, not blocked.
            Assert.True(guard.TryBegin(Station, Player));
        }

        [Fact]
        public void BeginGuarded_onSuccess_holdsTheReservation_forTheDeferredCompletion()
        {
            // The happy path: craft-start ran, the reservation stays HELD so the deferred completion
            // owns its release, and a duplicate DURING the craft is still refused (no re-consume).
            StationCraftGuard guard = new StationCraftGuard();
            bool ran = false;

            bool result = guard.BeginGuarded(Station, Player, () => { ran = true; });

            Assert.True(result);
            Assert.True(ran);
            Assert.True(guard.IsInProgress(Station, Player));      // still held
            Assert.False(guard.TryBegin(Station, Player));         // duplicate during the craft refused

            guard.Complete(Station, Player);                        // the deferred completion releases it
            Assert.False(guard.IsInProgress(Station, Player));
        }

        [Fact]
        public void BeginGuarded_whenAlreadyInProgress_runsNothing_andReturnsFalse()
        {
            // A duplicate StartCrafting arriving while a craft runs must NOT re-run the craft-start
            // (which would re-consume / re-push); BeginGuarded returns false without invoking it.
            StationCraftGuard guard = new StationCraftGuard();
            Assert.True(guard.TryBegin(Station, Player)); // a craft is already in flight here

            bool ranSecond = false;
            bool result = guard.BeginGuarded(Station, Player, () => { ranSecond = true; });

            Assert.False(result);
            Assert.False(ranSecond);                               // the craft-start body never ran
            Assert.True(guard.IsInProgress(Station, Player));      // the original craft is untouched
            Assert.Equal(1, guard.Count);
        }

        [Fact]
        public void BeginGuarded_aThrowingStart_leavesOtherCraftsUntouched()
        {
            // A craft-start that throws at one (station, player) must free ONLY its own reservation,
            // never an unrelated craft in flight elsewhere.
            StationCraftGuard guard = new StationCraftGuard();
            Assert.True(guard.TryBegin(OtherStation, OtherPlayer)); // an unrelated craft is running

            Assert.Throws<InvalidOperationException>(() =>
                guard.BeginGuarded(Station, Player, () => throw new InvalidOperationException("boom")));

            Assert.False(guard.IsInProgress(Station, Player));          // the failed one is released...
            Assert.True(guard.IsInProgress(OtherStation, OtherPlayer)); // ...the other survives
            Assert.Equal(1, guard.Count);
        }

        [Fact]
        public void BeginGuarded_afterAThrow_theStationCraftsPartAfterPart_again()
        {
            // End to end: a craft-start throws (station wedged under the old code), then the station
            // must go straight back to crafting part after part.
            StationCraftGuard guard = new StationCraftGuard();

            Assert.Throws<InvalidOperationException>(() =>
                guard.BeginGuarded(Station, Player, () => throw new InvalidOperationException("boom")));

            for (int i = 0; i < 5; i++)
            {
                Assert.True(guard.BeginGuarded(Station, Player, () => { })); // accepted every time
                guard.Complete(Station, Player);
            }

            Assert.Equal(0, guard.Count);
        }
    }
}
