using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Fuel
{
    /// <summary>
    /// PINS the rate half of the multiplayer-safety rule for the fuel gauge: a
    /// continuously ticking per-ship level must not become a continuous broadcast.
    /// Both gates are derived from the client's own two-stage needle smoothing, so
    /// what these tests defend is that we never spend wire on a change the player
    /// cannot see - and never suppress the two readings they act on.
    /// </summary>
    public class FuelGaugePushTrackerTests
    {
        private const long Gauge = 77L;

        [Fact]
        public void TheFirstReadingAlwaysGoesOut()
        {
            var tracker = new FuelGaugePushTracker();
            Assert.True(tracker.ShouldPush(Gauge, 250.0, 250.0, 0.0));
        }

        [Fact]
        public void ASubUnitDripIsSuppressed()
        {
            var tracker = new FuelGaugePushTracker();
            tracker.ShouldPush(Gauge, 200.0, 250.0, 0.0);

            Assert.False(tracker.ShouldPush(Gauge, 199.9, 250.0, 10.0));
            Assert.False(tracker.ShouldPush(Gauge, 199.5, 250.0, 20.0));
        }

        [Fact]
        public void AWholeUnitAfterTheFloorGoesOut()
        {
            var tracker = new FuelGaugePushTracker();
            tracker.ShouldPush(Gauge, 200.0, 250.0, 0.0);

            Assert.True(tracker.ShouldPush(Gauge, 198.0, 250.0, 1.5));
        }

        [Fact]
        public void AWholeUnitInsideTheFloorIsHeldBack()
        {
            var tracker = new FuelGaugePushTracker();
            tracker.ShouldPush(Gauge, 200.0, 250.0, 0.0);

            Assert.False(tracker.ShouldPush(Gauge, 150.0, 250.0, 0.4));
            Assert.True(tracker.ShouldPush(Gauge, 150.0, 250.0, 1.0));
        }

        [Fact]
        public void EmptyAlwaysGoesOutEvenForALessThanUnitMove()
        {
            var tracker = new FuelGaugePushTracker();
            tracker.ShouldPush(Gauge, 0.4, 250.0, 0.0);

            // 0.4 -> 0 is below the quantum, but "empty" is the reading a player
            // acts on and a 270-degree needle shows the difference.
            Assert.True(tracker.ShouldPush(Gauge, 0.0, 250.0, 5.0));
        }

        [Fact]
        public void FullAlwaysGoesOutEvenForALessThanUnitMove()
        {
            var tracker = new FuelGaugePushTracker();
            tracker.ShouldPush(Gauge, 249.6, 250.0, 0.0);

            Assert.True(tracker.ShouldPush(Gauge, 250.0, 250.0, 5.0));
        }

        [Fact]
        public void AnEndpointStillRespectsTheRateFloor()
        {
            // The endpoint exemption is about the QUANTUM, not about the cadence:
            // a ship flickering across zero must not become a packet storm.
            var tracker = new FuelGaugePushTracker();
            tracker.ShouldPush(Gauge, 5.0, 250.0, 0.0);

            Assert.False(tracker.ShouldPush(Gauge, 0.0, 250.0, 0.2));
        }

        [Fact]
        public void AnUnchangedLevelIsNeverResent()
        {
            var tracker = new FuelGaugePushTracker();
            tracker.ShouldPush(Gauge, 100.0, 250.0, 0.0);

            Assert.False(tracker.ShouldPush(Gauge, 100.0, 250.0, 3600.0));
        }

        [Fact]
        public void GaugesAreTrackedIndependently()
        {
            var tracker = new FuelGaugePushTracker();
            Assert.True(tracker.ShouldPush(1L, 100.0, 250.0, 0.0));
            Assert.True(tracker.ShouldPush(2L, 100.0, 250.0, 0.0));
            Assert.Equal(2, tracker.Count);

            Assert.True(tracker.Forget(1L));
            Assert.Equal(1, tracker.Count);
            Assert.True(tracker.ShouldPush(1L, 100.0, 250.0, 0.0));
        }

        [Fact]
        public void AGarbageLevelIsNeverPushed()
        {
            var tracker = new FuelGaugePushTracker();
            Assert.False(tracker.ShouldPush(Gauge, double.NaN, 250.0, 0.0));
            Assert.Equal(0, tracker.Count);
        }
    }
}
