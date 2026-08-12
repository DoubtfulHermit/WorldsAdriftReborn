using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Latest-state ingest for the two movement streams. The rules under test
    /// are the ones the relay's correctness now leans on: a dropped sample
    /// never reaches any other player's wire, so a rule that drops too much
    /// freezes an avatar and a rule that drops too little relays garbage.
    /// </summary>
    public class MovementIngestPolicyTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
            public void Advance(double seconds) => Elapsed += TimeSpan.FromSeconds(seconds);
        }

        private const ulong Peer = 0xbeef;

        private static MovementSample At(double x, double y, double z, float? ts = null, bool spaceChange = false)
        {
            return new MovementSample(ts.HasValue, ts ?? 0f, true, x, y, z, spaceChange);
        }

        private static MovementSample TimestampOnly(float ts)
        {
            return new MovementSample(true, ts, false, 0, 0, 0, false);
        }

        // ------------------------------------------------------------------
        // PLAIN MOTION IS ACCEPTED
        // ------------------------------------------------------------------

        [Fact]
        public void FirstSampleIsAccepted()
        {
            MovementIngest ingest = new(new FakeClock());
            Assert.Equal(IngestVerdict.Accept, ingest.Observe(Peer, MovementStream.Transform, At(0, 100, 0)));
        }

        [Fact]
        public void OrdinaryWalkingIsAccepted()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.Transform, At(0, 100, 0));

            clock.Advance(0.05);
            Assert.Equal(IngestVerdict.Accept, ingest.Observe(Peer, MovementStream.Transform, At(0.4, 100, 0)));
        }

        [Fact]
        public void GlidingSpeedIsAccepted()
        {
            // 55 m/s sustained over a full second - faster than anything the
            // game actually reaches, still under the 60 m/s ceiling. The rule
            // says generous; this is the test that keeps it generous.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.Transform, At(0, 100, 0));

            for (int i = 1; i <= 20; i++)
            {
                clock.Advance(0.05);
                Assert.Equal(IngestVerdict.Accept,
                    ingest.Observe(Peer, MovementStream.Transform, At(i * 55.0 * 0.05, 100, 0)));
            }
        }

        [Fact]
        public void StreamsAreIndependent()
        {
            // A 1073 baseline must not judge 190602 positions: they are in
            // different spaces (relative vs world).
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 1f));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.Accept,
                ingest.Observe(Peer, MovementStream.Transform, At(15000, -300, 9000)));
        }

        [Fact]
        public void PeersAreIndependent()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(1, MovementStream.Transform, At(0, 100, 0));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.Accept, ingest.Observe(2, MovementStream.Transform, At(9999, 100, 0)));
        }

        // ------------------------------------------------------------------
        // TIMESTAMP REGRESSIONS
        // ------------------------------------------------------------------

        [Fact]
        public void OlderTimestampIsDropped()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 5.0f));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.DropTimestampRegression,
                ingest.Observe(Peer, MovementStream.PlayerState, At(0.1, 0, 0, ts: 4.9f)));
        }

        [Fact]
        public void OneRegressionDoesNotMoveTheBaseline()
        {
            // After a dropped straggler, the NEXT in-order sample must still be
            // judged against the pre-straggler baseline and accepted.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 5.0f));
            clock.Advance(0.05);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0.1, 0, 0, ts: 4.9f));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.Accept,
                ingest.Observe(Peer, MovementStream.PlayerState, At(0.2, 0, 0, ts: 5.1f)));
        }

        [Fact]
        public void SustainedRegressionReanchorsInsteadOfMutingForever()
        {
            // A sender whose accumulator restarted publishes ts ~0.0x while the
            // baseline says 300. Dropping forever would freeze that player for
            // the rest of the session; the second consecutive regression is
            // believed instead.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 300f));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.DropTimestampRegression,
                ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 0.05f)));
            clock.Advance(0.05);
            Assert.Equal(IngestVerdict.AcceptReanchor,
                ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 0.10f)));

            // And the stream lives on from the new epoch.
            clock.Advance(0.05);
            Assert.Equal(IngestVerdict.Accept,
                ingest.Observe(Peer, MovementStream.PlayerState, At(0.1, 0, 0, ts: 0.15f)));
        }

        // ------------------------------------------------------------------
        // DUPLICATES
        // ------------------------------------------------------------------

        [Fact]
        public void ExactDuplicateIsDropped()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(1, 2, 3, ts: 5f));
            clock.Advance(0.01);

            Assert.Equal(IngestVerdict.DropDuplicate,
                ingest.Observe(Peer, MovementStream.PlayerState, At(1, 2, 3, ts: 5f)));
        }

        [Fact]
        public void SameTimestampDifferentPositionIsAccepted()
        {
            // The sender's 20 Hz timestamp limiter makes consecutive positions
            // share a stamp ROUTINELY. Those are real motion, not duplicates.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(1, 2, 3, ts: 5f));
            clock.Advance(0.016);

            Assert.Equal(IngestVerdict.Accept,
                ingest.Observe(Peer, MovementStream.PlayerState, At(1.1, 2, 3, ts: 5f)));
        }

        [Fact]
        public void TransformDuplicatePositionIsDropped()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.Transform, At(1, 2, 3));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.DropDuplicate, ingest.Observe(Peer, MovementStream.Transform, At(1, 2, 3)));
        }

        [Fact]
        public void EdgeOnlyUpdateIsAcceptedWithoutJudgement()
        {
            // A bone-data-only or flag-only 1073 carries nothing to judge and
            // must never be dropped: it may carry a teleport ack.
            MovementIngest ingest = new(new FakeClock());
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 5f));

            Assert.Equal(IngestVerdict.Accept, ingest.Observe(Peer, MovementStream.PlayerState,
                new MovementSample(false, 0, false, 0, 0, 0, false)));
        }

        // ------------------------------------------------------------------
        // ABSURD JUMPS AND RE-ANCHORING
        // ------------------------------------------------------------------

        [Fact]
        public void AbsurdJumpIsDropped()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.Transform, At(0, 100, 0));
            clock.Advance(0.05);

            // 500 m in 50 ms = 10,000 m/s.
            Assert.Equal(IngestVerdict.DropAbsurdJump, ingest.Observe(Peer, MovementStream.Transform, At(500, 100, 0)));
        }

        [Fact]
        public void ConfirmedJumpReanchors()
        {
            // A teleport: the first far sample is dropped on suspicion, the
            // second - consistent with the first - re-anchors. This server
            // SENDS teleports, so a policy that swallowed them would fight the
            // teleport service.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.Transform, At(0, 100, 0));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.DropAbsurdJump, ingest.Observe(Peer, MovementStream.Transform, At(500, 100, 0)));
            clock.Advance(0.05);
            Assert.Equal(IngestVerdict.AcceptReanchor, ingest.Observe(Peer, MovementStream.Transform, At(500.5, 100, 0)));

            // Life continues from the new anchor.
            clock.Advance(0.05);
            Assert.Equal(IngestVerdict.Accept, ingest.Observe(Peer, MovementStream.Transform, At(501, 100, 0)));
        }

        [Fact]
        public void LoneGarbageSampleDoesNotPoisonTheBaseline()
        {
            // One absurd sample, then the player is back where they were: the
            // candidate is discarded and normal service resumes immediately.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.Transform, At(0, 100, 0));
            clock.Advance(0.05);
            ingest.Observe(Peer, MovementStream.Transform, At(500, 100, 0));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.Accept, ingest.Observe(Peer, MovementStream.Transform, At(0.5, 100, 0)));
        }

        [Fact]
        public void TwoInconsistentGarbageSamplesKeepBeingDropped()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.Transform, At(0, 100, 0));
            clock.Advance(0.05);
            Assert.Equal(IngestVerdict.DropAbsurdJump, ingest.Observe(Peer, MovementStream.Transform, At(500, 100, 0)));
            clock.Advance(0.05);
            Assert.Equal(IngestVerdict.DropAbsurdJump, ingest.Observe(Peer, MovementStream.Transform, At(-800, 100, 0)));
        }

        [Fact]
        public void SpaceChangeReanchorsWithoutJudgingDistance()
        {
            // Stepping onto a ship: 1073 positions flip from island-relative to
            // ship-relative. The numeric jump is arbitrary and means nothing.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(4000, 0, 4000, ts: 5f));
            clock.Advance(0.05);

            Assert.Equal(IngestVerdict.AcceptReanchor,
                ingest.Observe(Peer, MovementStream.PlayerState, At(1.5, 0.2, -2, ts: 5.05f, spaceChange: true)));
        }

        [Fact]
        public void AllowedTravelIsGenerousAtZeroDelta()
        {
            // Two samples in the same poll turn must not fail on jitter.
            Assert.True(MovementIngestPolicy.AllowedTravelMetres(0) >= MovementIngestPolicy.JumpSlackMetres);
        }

        // ------------------------------------------------------------------
        // THE STALENESS DISCRIMINATOR
        // ------------------------------------------------------------------

        [Fact]
        public void HealthySenderAccumulatesZeroStaleness()
        {
            // Sender timestamps advance exactly as fast as wall clock: the sum
            // of (wallΔ − tsΔ) is zero however many samples arrive.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            for (int i = 0; i <= 20; i++)
            {
                ingest.Observe(Peer, MovementStream.PlayerState, At(i * 0.1, 0, 0, ts: i * 0.05f));
                clock.Advance(0.05);
            }

            Assert.Equal(0.0, ingest.SnapshotAndReset(Peer).StalenessSeconds, 3);
        }

        [Fact]
        public void SlowSenderClockAccumulatesPositiveStaleness()
        {
            // The sender's simulation clock runs at 80% of wall clock (the
            // measured live pathology was ~96%): every 50 ms of wall time its
            // stamp advances only 40 ms, so each accepted sample adds +10 ms.
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            for (int i = 0; i <= 10; i++)
            {
                ingest.Observe(Peer, MovementStream.PlayerState, At(i * 0.1, 0, 0, ts: i * 0.04f));
                clock.Advance(0.05);
            }

            Assert.Equal(0.1, ingest.SnapshotAndReset(Peer).StalenessSeconds, 3);
        }

        [Fact]
        public void SnapshotResetsTheWindowButNotTheBaseline()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 5f));
            ingest.SnapshotAndReset(Peer);
            clock.Advance(0.05);

            // The baseline survives: a regression against it is still caught.
            Assert.Equal(IngestVerdict.DropTimestampRegression,
                ingest.Observe(Peer, MovementStream.PlayerState, At(0.1, 0, 0, ts: 4f)));

            IngestWindowStats window = ingest.SnapshotAndReset(Peer);
            Assert.Equal(1, window.TimestampRegressions);
            Assert.Equal(0, window.Accepted);
        }

        [Fact]
        public void DropCountersAreCounted()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 5f));
            clock.Advance(0.01);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 5f));       // duplicate
            clock.Advance(0.01);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 4f));       // regression
            clock.Advance(0.01);
            ingest.Observe(Peer, MovementStream.PlayerState, At(9000, 0, 0, ts: 6f));    // jump

            IngestWindowStats stats = ingest.SnapshotAndReset(Peer);
            Assert.Equal(1, stats.Duplicates);
            Assert.Equal(1, stats.TimestampRegressions);
            Assert.Equal(1, stats.AbsurdJumps);
            Assert.Equal(1, stats.Accepted);
        }

        // ------------------------------------------------------------------
        // LIFECYCLE
        // ------------------------------------------------------------------

        [Fact]
        public void ForgetDropsEverything()
        {
            FakeClock clock = new();
            MovementIngest ingest = new(clock);
            ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 500f));
            ingest.Forget(Peer);
            Assert.Empty(ingest.KnownPeers());

            // A reconnected sender starting at ts ~0 must NOT be judged against
            // the dead session's baseline.
            Assert.Equal(IngestVerdict.Accept,
                ingest.Observe(Peer, MovementStream.PlayerState, At(0, 0, 0, ts: 0.05f)));
        }

        [Fact]
        public void KnownPeersListsObservedPeers()
        {
            MovementIngest ingest = new(new FakeClock());
            ingest.Observe(1, MovementStream.Transform, At(0, 0, 0));
            ingest.Observe(2, MovementStream.Transform, At(0, 0, 0));
            Assert.Equal(new ulong[] { 1, 2 }, ingest.KnownPeers().OrderBy(p => p).ToArray());
        }
    }
}
