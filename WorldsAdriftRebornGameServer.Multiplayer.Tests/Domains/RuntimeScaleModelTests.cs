using WorldsAdriftRebornGameServer.Multiplayer.Domains;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Domains
{
    public sealed class RuntimeScaleModelTests
    {
        [Theory]
        [InlineData(5, 250, 160, 32, 90, 90, 20_480)]
        [InlineData(20, 1000, 640, 32, 1560, 1280, 81_920)]
        [InlineData(50, 2500, 1600, 32, 9900, 3200, 204_800)]
        [InlineData(100, 5000, 3200, 32, 39_800, 6400, 409_600)]
        public void Five_to_one_hundred_ship_baseline_is_deterministic_and_capped(
            int ships, long steps, long scanBefore, long scanAfter,
            long relayBefore, long relayAfter, long snapshotBytes)
        {
            RuntimeScaleEstimate estimate = RuntimeScaleBaseline.Estimate(ships);

            Assert.Equal(steps, estimate.PhysicsStepsPerSecond);
            Assert.Equal(scanBefore, estimate.MembershipComparisonsPerChangedShipBeforeIndex);
            Assert.Equal(scanAfter, estimate.MembershipComparisonsPerChangedShipAfterIndex);
            Assert.Equal(relayBefore, estimate.WorstCaseAvatarRelayPairs);
            Assert.Equal(relayAfter, estimate.BoundedAvatarRelayPairs);
            Assert.Equal(snapshotBytes, estimate.SnapshotBytes);
        }

        [Fact]
        public void Telemetry_keeps_only_the_newest_bounded_window()
        {
            var telemetry = new BoundedRuntimeTelemetry(capacity: 2);
            telemetry.Record(new RuntimeWorkSample(RuntimeWorkKind.Physics, 1, 10, 20));
            telemetry.Record(new RuntimeWorkSample(RuntimeWorkKind.Gateway, 2, 30, 20));
            telemetry.Record(new RuntimeWorkSample(RuntimeWorkKind.Physics, 3, 40, 20, Replayed: true));

            Assert.Equal(2, telemetry.Count);
            Assert.Equal(new[] { RuntimeWorkKind.Gateway, RuntimeWorkKind.Physics },
                telemetry.Snapshot().Select(x => x.Kind));
            RuntimeWorkSummary physics = telemetry.Summarize(RuntimeWorkKind.Physics);
            Assert.Equal(1, physics.SampleCount);
            Assert.Equal(1, physics.OverBudgetCount);
            Assert.Equal(3, physics.TotalWorkUnits);
            Assert.Equal(40, physics.MaxElapsedMicroseconds);
        }

        [Fact]
        public void Telemetry_rejects_unbounded_or_negative_inputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BoundedRuntimeTelemetry(BoundedRuntimeTelemetry.MaxCapacity + 1));
            var telemetry = new BoundedRuntimeTelemetry();
            Assert.Throws<ArgumentOutOfRangeException>(() => telemetry.Record(
                new RuntimeWorkSample(RuntimeWorkKind.Physics, -1, 0, 0)));
        }
    }
}
