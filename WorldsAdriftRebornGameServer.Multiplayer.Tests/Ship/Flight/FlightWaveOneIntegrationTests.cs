using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>Cross-track guards for the bounds/fixed-clock ownership seam.</summary>
    public sealed class FlightWaveOneIntegrationTests
    {
        private static readonly FlightTuning Tuning = FlightTuning.FromEnvironment(_ => null);

        [Fact]
        public void Bounds_and_fixed_step_switch_matrix_has_one_integrator_owner()
        {
            FlightState initial = new FlightState(
                17_650, 100, 0, 0, 0, 0, 0, 0, 12, 0, 3);
            long nowMs = 1_000_000;
            double firstStepEnd = (nowMs / 1000.0) - 0.22;

            FlightSession legacyOff = Session(initial);
            FlightEmit legacyOffEmit = legacyOff.Advance(nowMs, 0.24, Tuning,
                worldBounds: new RetailWorldBoundsPolicy(false));
            Assert.True(legacyOffEmit.Emit);
            Assert.Equal(0, legacyOff.LastWorldBoundsTelemetry.ReferenceSubsteps);

            FlightSession legacyOn = Session(initial);
            FlightEmit legacyOnEmit = legacyOn.Advance(nowMs, 0.24, Tuning,
                worldBounds: new RetailWorldBoundsPolicy(true));
            Assert.True(legacyOnEmit.Emit);
            Assert.Equal(12, legacyOn.LastWorldBoundsTelemetry.ReferenceSubsteps);

            FlightSession fixedOff = Session(initial);
            FlightEmit fixedOffEmit = fixedOff.AdvanceFixed(nowMs, 0.24, 12,
                firstStepEnd, Tuning, worldBounds: new RetailWorldBoundsPolicy(false));
            Assert.True(fixedOffEmit.Emit);
            Assert.Equal(0, fixedOff.LastWorldBoundsTelemetry.ReferenceSubsteps);

            FlightSession fixedOn = Session(initial);
            FlightEmit fixedOnEmit = fixedOn.AdvanceFixed(nowMs, 0.24, 12,
                firstStepEnd, Tuning, worldBounds: new RetailWorldBoundsPolicy(true));
            Assert.True(fixedOnEmit.Emit);
            Assert.Equal(12, fixedOn.LastWorldBoundsTelemetry.ReferenceSubsteps);

            // Bounds-on legacy mode already uses twelve reference slices. Enabling
            // the fixed clock must subsume that cadence, not wrap it in another
            // twelve-way loop (144 integrations).
            AssertStateEqual(legacyOn.State, fixedOn.State);
            Assert.Equal(legacyOn.LastWorldBoundsTelemetry.PushbackDeltaVxMps,
                fixedOn.LastWorldBoundsTelemetry.PushbackDeltaVxMps, 12);
            Assert.True(fixedOn.State.X < initial.X);

            // Bounds OFF retains each mode's promised semantics: one legacy 240ms
            // integration versus twelve fixed integrations. This difference is the
            // opt-in feel change, not accidental boundary coupling.
            Assert.NotEqual(legacyOff.State.X, fixedOff.State.X);
        }

        [Fact]
        public void Fixed_batch_quarantine_stops_after_first_invalid_reference_step()
        {
            var corrupt = new FlightState(double.NaN, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            FlightSession session = Session(corrupt);
            session.SetInput(new FlightControlInput(1, 1, 1, 1, 1));

            FlightEmit emit = session.AdvanceFixed(1_000_000, 0.24, 12, 999.78,
                Tuning, worldBounds: new RetailWorldBoundsPolicy(true));

            Assert.True(emit.Emit);
            Assert.True(RetailWorldBoundsPolicy.IsFinite(session.State));
            Assert.True(session.State.IsAtRest);
            Assert.True(session.LastWorldBoundsTelemetry.InvalidState);
            Assert.Equal(1, session.LastWorldBoundsTelemetry.ReferenceSubsteps);
        }

        [Fact]
        public void Parked_boundary_recovery_runs_under_fixed_clock_without_a_pilot()
        {
            var session = new FlightSession(FlightState.AtRestAt(17_650, 100, 0));

            FlightEmit emit = session.AdvanceFixed(1_000_000, 0.24, 12, 999.78,
                Tuning, worldBounds: new RetailWorldBoundsPolicy(true));

            Assert.True(emit.Emit);
            Assert.True(session.State.X < 17_650);
            Assert.True(session.State.VxMps < 0);
            Assert.Equal(12, session.LastWorldBoundsTelemetry.ReferenceSubsteps);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(26)]
        [InlineData(int.MaxValue)]
        public void Session_rejects_batches_outside_the_clock_stall_cap(int steps)
        {
            FlightSession session = Session(FlightState.AtRestAt(0, 100, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceFixed(
                1_000_000, 0.24, steps, 0.02, Tuning));
        }

        private static FlightSession Session(FlightState state)
        {
            var session = new FlightSession(state);
            session.Man();
            return session;
        }

        private static void AssertStateEqual(FlightState expected, FlightState actual)
        {
            Assert.Equal(expected.X, actual.X, 12);
            Assert.Equal(expected.Y, actual.Y, 12);
            Assert.Equal(expected.Z, actual.Z, 12);
            Assert.Equal(expected.YawRadians, actual.YawRadians, 12);
            Assert.Equal(expected.YawRateRadPerSec, actual.YawRateRadPerSec, 12);
            Assert.Equal(expected.RollRadians, actual.RollRadians, 12);
            Assert.Equal(expected.PitchRadians, actual.PitchRadians, 12);
            Assert.Equal(expected.SpeedCmdMps, actual.SpeedCmdMps, 12);
            Assert.Equal(expected.VxMps, actual.VxMps, 12);
            Assert.Equal(expected.VyMps, actual.VyMps, 12);
            Assert.Equal(expected.VzMps, actual.VzMps, 12);
        }
    }
}
