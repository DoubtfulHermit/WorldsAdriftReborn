using System;
using System.Collections.Generic;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The PRODUCTION path: WAREBORN_FLIGHT_FIXED_STEP=1, so stamps are
    /// phase-locked. What can still go wrong there, and the two corrections for it.
    /// </summary>
    public class FlightProductionCadenceTests
    {
        private const long StepMs = 240;
        private const double Step = ShipMotionPolicy.SendIntervalSeconds;
        private static readonly FlightTuning Tuning = new FlightTuning();

        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldsAdriftReborn.sln")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        // ---- the defect: a phase-locked stamp banks dropped simulation for ever ----

        [Fact]
        public void Uncompensated_phase_lock_banks_permanent_lag_when_steps_are_dropped()
        {
            // Twelve dropped steps is 240 ms of wall time the fixed clock consumed
            // without simulating. The stamp still advances one interval.
            long stamp = FlightStampPolicy.NextStamp(
                FlightStampMode.PhaseLocked, everEmitted: true,
                lastStampMs: 10_000, nowMs: 10_480, stepMs: StepMs);

            Assert.Equal(10_240, stamp);
            Assert.Equal(240, 10_480 - stamp); // 240 ms of lag, never recovered
        }

        [Fact]
        public void Compensating_for_dropped_simulation_keeps_the_wire_clock_on_wall_clock()
        {
            long lost = FlightStampPolicy.LostSimulationMilliseconds(
                droppedSteps: 12, stepSeconds: FixedFlightClock.StepSeconds);
            Assert.Equal(240, lost);

            long stamp = FlightStampPolicy.NextStamp(
                FlightStampMode.PhaseLocked, everEmitted: true,
                lastStampMs: 10_000, nowMs: 10_480, stepMs: StepMs,
                lostSimulationMs: lost);

            Assert.Equal(10_480, stamp);
        }

        [Fact]
        public void Lag_accumulates_without_compensation_and_does_not_with_it()
        {
            const int points = 20;
            const long droppedPerPoint = 5; // 100 ms of thrown-away simulation

            long lost = FlightStampPolicy.LostSimulationMilliseconds(
                droppedPerPoint, FixedFlightClock.StepSeconds);

            long uncompensated = 0;
            long compensated = 0;
            for (int i = 0; i < points; i++)
            {
                uncompensated = FlightStampPolicy.NextStamp(
                    FlightStampMode.PhaseLocked, true, uncompensated, 0, StepMs);
                compensated = FlightStampPolicy.NextStamp(
                    FlightStampMode.PhaseLocked, true, compensated, 0, StepMs, lost);
            }

            // Wall clock advanced (240 + 100) ms per point.
            long wallClock = points * (StepMs + lost);
            Assert.Equal(wallClock, compensated);
            Assert.True(wallClock - uncompensated >= 2000,
                "uncompensated lag must grow past the client's 5 s latency clamp in time");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void A_batch_that_dropped_nothing_contributes_nothing(long droppedSteps)
        {
            Assert.Equal(0, FlightStampPolicy.LostSimulationMilliseconds(
                droppedSteps, FixedFlightClock.StepSeconds));
        }

        [Fact]
        public void Compensation_never_rewinds_the_timeline()
        {
            long stamp = FlightStampPolicy.NextStamp(
                FlightStampMode.PhaseLocked, true, 10_000, 0, StepMs, lostSimulationMs: -5_000);

            Assert.Equal(10_240, stamp);
        }

        [Fact]
        public void Compensated_points_still_clear_the_client_reject_floor()
        {
            long last = 10_000;
            var lostPattern = new long[] { 0, 60, 0, 0, 500, 20, 0 };
            foreach (long lost in lostPattern)
            {
                long stamp = FlightStampPolicy.NextStamp(
                    FlightStampMode.PhaseLocked, true, last, 0, StepMs, lost);
                Assert.True(stamp > last);
                Assert.True(ShipMotionPolicy.IsLegalSeparation(last, stamp));
                last = stamp;
            }
        }

        [Fact]
        public void A_session_emits_the_compensated_stamp_end_to_end()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(new FlightControlInput(1f, 0f, 0f, -1f, 0f));

            long now = 5_000_000;
            FlightEmit first = session.AdvanceFixed(
                now, Step, 12, 0.0, Tuning, phaseLockedEmit: true);
            Assert.True(first.Emit);

            FlightEmit second = session.AdvanceFixed(
                now + 480, Step, 12, 0.24, Tuning, phaseLockedEmit: true,
                lostSimulationMs: 240);
            Assert.True(second.Emit);

            Assert.Equal(480, second.Spec.TimestampMs - first.Spec.TimestampMs);
        }

        [Fact]
        public void Default_is_off_so_the_live_stamp_stream_is_unchanged()
        {
            FlightSession compensated = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            FlightSession baseline = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            compensated.Man();
            baseline.Man();
            compensated.SetInput(new FlightControlInput(1f, 0f, 0f, -1f, 0f));
            baseline.SetInput(new FlightControlInput(1f, 0f, 0f, -1f, 0f));

            long now = 5_000_000;
            for (int i = 0; i < 6; i++)
            {
                FlightEmit a = compensated.AdvanceFixed(
                    now, Step, 12, i * 0.24, Tuning, phaseLockedEmit: true,
                    lostSimulationMs: 0);
                FlightEmit b = baseline.AdvanceFixed(
                    now, Step, 12, i * 0.24, Tuning, phaseLockedEmit: true);
                Assert.Equal(b.Spec.TimestampMs, a.Spec.TimestampMs);
                now += 240;
            }
        }

        // ---- the measurement ----

        [Fact]
        public void Even_sends_against_even_stamps_show_no_drift()
        {
            var cadence = new FlightSendCadence();
            double sendAt = 1_000;
            long stamp = 500_000;
            for (int i = 0; i < 20; i++)
            {
                cadence.Observe(sendAt, stamp);
                sendAt += 240;
                stamp += 240;
            }

            Assert.Equal(0.0, cadence.CumulativeDriftMilliseconds, 6);
            Assert.False(cadence.BufferErosionSuspected);
            Assert.Equal(0.0, cadence.WorstStampDeviationMilliseconds, 6);
        }

        [Fact]
        public void Bounded_send_jitter_shows_up_but_does_not_erode_the_buffer()
        {
            var cadence = new FlightSendCadence();
            double idealSend = 1_000;
            long stamp = 500_000;
            long[] jitter = { 0, 40, 5, 33, 12, 47, 2, 21, 8, 39 };
            foreach (long j in jitter)
            {
                cadence.Observe(idealSend + j, stamp);
                idealSend += 240;
                stamp += 240;
            }

            Assert.True(cadence.WorstSendDeviationMilliseconds > 0.0);
            Assert.Equal(0.0, cadence.WorstStampDeviationMilliseconds, 6);
            // Jitter around a drift-free ideal cannot accumulate.
            Assert.True(Math.Abs(cadence.CumulativeDriftMilliseconds) <= 50.0);
            Assert.False(cadence.BufferErosionSuspected);
        }

        [Fact]
        public void A_send_clock_that_runs_slow_is_reported_as_buffer_erosion()
        {
            var cadence = new FlightSendCadence();
            double sendAt = 1_000;
            long stamp = 500_000;
            for (int i = 0; i < 30; i++)
            {
                cadence.Observe(sendAt, stamp);
                sendAt += 260; // 20 ms slower than the timeline claims, every point
                stamp += 240;
            }

            Assert.True(cadence.CumulativeDriftMilliseconds > 500.0);
            Assert.True(cadence.BufferErosionSuspected);
            Assert.Contains("BUFFER-EROSION-SUSPECTED", cadence.Describe(), StringComparison.Ordinal);
        }

        [Fact]
        public void The_erosion_threshold_is_half_the_clients_extrapolation_headroom()
        {
            // ShipConfiguration.ExtrapolationTime = 0.75 s in the retail decompile.
            Assert.Equal(375.0, FlightSendCadence.BufferErosionWarnMilliseconds);
        }

        [Fact]
        public void The_first_point_only_seeds_and_a_backwards_clock_is_ignored()
        {
            var cadence = new FlightSendCadence();
            cadence.Observe(1_000, 500_000);
            Assert.Equal(0, cadence.WindowCount);
            Assert.Equal(1, cadence.Observed);

            cadence.Observe(900, 500_240); // clock went backwards
            Assert.Equal(0, cadence.WindowCount);
            Assert.Equal(0.0, cadence.CumulativeDriftMilliseconds, 6);
        }

        [Fact]
        public void The_percentile_window_is_bounded()
        {
            var cadence = new FlightSendCadence();
            double sendAt = 0;
            long stamp = 0;
            for (int i = 0; i < FlightSendCadence.WindowSize * 3; i++)
            {
                cadence.Observe(sendAt, stamp);
                sendAt += 240;
                stamp += 240;
            }
            Assert.Equal(FlightSendCadence.WindowSize, cadence.WindowCount);
            Assert.Equal(FlightSendCadence.WindowSize * 3, cadence.Observed);
        }

        // ---- source contracts ----

        [Fact]
        public void The_fixed_step_branch_charges_dropped_simulation_to_one_point_only()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");

            Assert.Contains("FlightStampPolicy.LostSimulationMilliseconds(", service,
                StringComparison.Ordinal);
            Assert.Contains("fixedBatch.DroppedSteps", service, StringComparison.Ordinal);
            Assert.Contains("lostSimulationMs: lostSimulationMs", service, StringComparison.Ordinal);
            // Charged once: cleared as soon as a point carries it.
            Assert.Contains("lostSimulationMs = 0L;", service, StringComparison.Ordinal);
            // And only under the opt-in.
            Assert.Contains("StampContinuityEnabled\n                        ? FlightStampPolicy.LostSimulationMilliseconds(",
                service.Replace("\r\n", "\n"), StringComparison.Ordinal);
        }

        [Fact]
        public void The_cadence_trace_is_opt_in_and_observes_only()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");

            Assert.Contains("WAREBORN_FLIGHT_CADENCE_TRACE", service, StringComparison.Ordinal);
            Assert.Contains("ObserveSendCadence(hullEntityId, emit)", service, StringComparison.Ordinal);
            Assert.Contains("if (!CadenceTraceEnabled || !emit.Emit)", service, StringComparison.Ordinal);
        }
    }
}
