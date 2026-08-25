using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The turn-vibration root cause and its correction, as arithmetic.
    ///
    /// The legacy publisher advances exactly one 240 ms step of simulation per
    /// emitted 1130 point and then stamps it at wall clock whenever the poll loop
    /// was late. Because a control point carries linear velocity but no ANGULAR
    /// velocity, the client hermite-eases the uneven interval out of the position
    /// path and slerps the attitude across the raw timestamp gap - so an uneven
    /// stamp delta is a turn-rate error and nothing else.
    /// </summary>
    public class FlightStampPolicyTests
    {
        private const long StepMs = 240;

        /// <summary>
        /// A realistic poll-jitter trace: the cadence timer is drift-free, so the
        /// IDEAL fire times are exactly 240 ms apart and each actual fire lands
        /// somewhere in the following poll window (the loop turns once per ENet
        /// event under a 50 ms timeout).
        /// </summary>
        private static IReadOnlyList<long> JitteredPollTimes(long firstMs, params long[] jitterMs)
        {
            var times = new List<long>();
            for (int i = 0; i < jitterMs.Length; i++)
            {
                times.Add(firstMs + i * StepMs + jitterMs[i]);
            }
            return times;
        }

        private static IReadOnlyList<long> StampsFor(FlightStampMode mode, IReadOnlyList<long> pollTimes)
        {
            var stamps = new List<long>();
            bool everEmitted = false;
            long last = 0;
            foreach (long now in pollTimes)
            {
                long stamp = FlightStampPolicy.NextStamp(mode, everEmitted, last, now, StepMs);
                stamps.Add(stamp);
                last = stamp;
                everEmitted = true;
            }
            return stamps;
        }

        private static IReadOnlyList<long> DeltasOf(IReadOnlyList<long> stamps)
        {
            var deltas = new List<long>();
            for (int i = 1; i < stamps.Count; i++)
            {
                deltas.Add(stamps[i] - stamps[i - 1]);
            }
            return deltas;
        }

        // ---- the defect, pinned so it cannot be "fixed" by deleting the evidence ----

        [Fact]
        public void Wall_clock_stamps_stretch_unevenly_under_ordinary_poll_jitter()
        {
            IReadOnlyList<long> deltas = DeltasOf(StampsFor(
                FlightStampMode.WallClock,
                JitteredPollTimes(10_000, 0, 37, 4, 41, 12, 48, 3)));

            Assert.Contains(deltas, d => d != StepMs);
        }

        [Fact]
        public void Wall_clock_jitter_is_a_real_rendered_turn_rate_error()
        {
            // 50 ms of lateness on a 240 ms point is a fifth of the interval, and
            // the client can only slerp the attitude across it.
            double fraction = FlightStampPolicy.RenderedAngularRateFraction(StepMs, StepMs + 50);

            Assert.True(fraction < 0.84,
                "a 50 ms stretched stamp must render a visibly slower turn than commanded");
            Assert.Equal(1.0, FlightStampPolicy.RenderedAngularRateFraction(StepMs, StepMs), 9);
        }

        // ---- the correction ----

        [Fact]
        public void Continuity_stamps_are_exactly_one_step_apart_under_poll_jitter()
        {
            IReadOnlyList<long> deltas = DeltasOf(StampsFor(
                FlightStampMode.Continuity,
                JitteredPollTimes(10_000, 0, 37, 4, 41, 12, 48, 3, 29, 50, 1)));

            Assert.All(deltas, d => Assert.Equal(StepMs, d));
        }

        [Fact]
        public void Continuity_renders_the_commanded_turn_rate_on_every_point()
        {
            IReadOnlyList<long> deltas = DeltasOf(StampsFor(
                FlightStampMode.Continuity,
                JitteredPollTimes(10_000, 0, 37, 4, 41, 12, 48, 3)));

            Assert.All(deltas, d => Assert.Equal(
                1.0, FlightStampPolicy.RenderedAngularRateFraction(StepMs, d), 9));
        }

        [Fact]
        public void Continuity_resyncs_to_wall_clock_after_a_skipped_interval()
        {
            // The cadence timer's stall branch re-bases to `now`, so the next fire
            // lands a whole interval later than the phase lock expects.
            long stamp = FlightStampPolicy.NextStamp(
                FlightStampMode.Continuity, everEmitted: true,
                lastStampMs: 10_000, nowMs: 10_000 + (2 * StepMs), stepMs: StepMs);

            Assert.Equal(10_000 + (2 * StepMs), stamp);
        }

        [Fact]
        public void Continuity_does_not_resync_for_lateness_below_a_whole_interval()
        {
            long phaseLocked = 10_000 + StepMs;

            Assert.False(FlightStampPolicy.ShouldResyncToWallClock(
                phaseLocked, phaseLocked + StepMs - 1, StepMs));
            Assert.True(FlightStampPolicy.ShouldResyncToWallClock(
                phaseLocked, phaseLocked + StepMs, StepMs));
        }

        [Fact]
        public void Continuity_relocks_after_a_resync()
        {
            var pollTimes = new List<long> { 10_000, 10_243, 11_500, 11_741, 11_982 };
            IReadOnlyList<long> stamps = StampsFor(FlightStampMode.Continuity, pollTimes);

            Assert.Equal(10_000, stamps[0]);
            Assert.Equal(10_240, stamps[1]);
            Assert.Equal(11_500, stamps[2]); // resync: the timer skipped
            Assert.Equal(11_740, stamps[3]); // relocked
            Assert.Equal(11_980, stamps[4]);
        }

        [Fact]
        public void Continuity_never_lags_wall_clock_by_more_than_one_interval()
        {
            var pollTimes = JitteredPollTimes(10_000, 0, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            IReadOnlyList<long> stamps = StampsFor(FlightStampMode.Continuity, pollTimes);

            for (int i = 0; i < stamps.Count; i++)
            {
                Assert.True(pollTimes[i] - stamps[i] < StepMs,
                    "the phase-locked wire clock must stay within one interval of wall clock");
            }
        }

        // ---- invariants every mode owes the client ----

        [Theory]
        [InlineData(FlightStampMode.WallClock)]
        [InlineData(FlightStampMode.PhaseLocked)]
        [InlineData(FlightStampMode.Continuity)]
        public void Every_mode_is_monotonic_and_legally_separated(FlightStampMode mode)
        {
            var pollTimes = new List<long>
            {
                10_000, 10_201, 10_240, 10_530, 10_531, 12_000, 12_240, 12_289,
            };
            IReadOnlyList<long> stamps = StampsFor(mode, pollTimes);

            for (int i = 1; i < stamps.Count; i++)
            {
                Assert.True(stamps[i] > stamps[i - 1], mode + " must be strictly monotonic");
                Assert.True(
                    ShipMotionPolicy.IsLegalSeparation(stamps[i - 1], stamps[i]),
                    mode + " must never emit inside the client's 0.228 s reject floor");
            }
        }

        [Theory]
        [InlineData(FlightStampMode.WallClock)]
        [InlineData(FlightStampMode.PhaseLocked)]
        [InlineData(FlightStampMode.Continuity)]
        public void The_first_point_of_a_session_is_always_wall_clock(FlightStampMode mode)
        {
            Assert.Equal(7_777, FlightStampPolicy.NextStamp(
                mode, everEmitted: false, lastStampMs: 0, nowMs: 7_777, stepMs: StepMs));
        }

        [Fact]
        public void Phase_locked_mode_is_unchanged_by_this_correction()
        {
            IReadOnlyList<long> stamps = StampsFor(
                FlightStampMode.PhaseLocked,
                JitteredPollTimes(10_000, 0, 37, 4, 900, 12));

            Assert.Equal(new long[] { 10_000, 10_240, 10_480, 10_720, 10_960 }, stamps);
        }

        [Fact]
        public void Wall_clock_mode_is_byte_for_byte_the_historic_rule()
        {
            // The rule this replaced, restated independently.
            static long Historic(bool everEmitted, long last, long now, long step) =>
                everEmitted && now < last + step ? last + step : now;

            var pollTimes = new List<long>
            {
                10_000, 10_100, 10_241, 10_500, 10_501, 12_000, 12_240,
            };

            bool ever = false;
            long lastPolicy = 0, lastHistoric = 0;
            foreach (long now in pollTimes)
            {
                long policy = FlightStampPolicy.NextStamp(
                    FlightStampMode.WallClock, ever, lastPolicy, now, StepMs);
                long historic = Historic(ever, lastHistoric, now, StepMs);
                Assert.Equal(historic, policy);
                lastPolicy = policy;
                lastHistoric = historic;
                ever = true;
            }
        }

        [Fact]
        public void Resync_tolerance_is_one_whole_publication_interval()
        {
            // A mutation to 0 intervals (always resync) collapses Continuity into
            // WallClock; a mutation to 2 would let the wire clock lag far enough to
            // skew the client's smoothed server-latency estimate. Pinned.
            Assert.Equal(1, FlightStampPolicy.ContinuityResyncIntervals);
        }

        [Fact]
        public void Rendered_rate_fraction_refuses_a_non_advancing_gap()
        {
            Assert.Equal(0.0, FlightStampPolicy.RenderedAngularRateFraction(StepMs, 0));
            Assert.Equal(0.0, FlightStampPolicy.RenderedAngularRateFraction(StepMs, -5));
        }
    }
}
