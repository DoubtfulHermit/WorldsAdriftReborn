using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The three client-side accept/reject numbers a server-driven ship must
    /// respect. Every one is measured off ~/Games/WAReborn-decompiled and silent
    /// when violated, which is exactly why it is pinned here.
    /// </summary>
    public class ShipMotionPolicyTests
    {
        [Fact]
        public void The_component_id_is_1130()
        {
            Assert.Equal(1130u, ShipMotionPolicy.ComponentId);
        }

        [Fact]
        public void The_send_interval_is_the_clients_own_send_interval()
        {
            // ShipConfiguration.SendInterval = 0.24 (ShipConfiguration.cs:24).
            Assert.Equal(0.24, ShipMotionPolicy.SendIntervalSeconds);
        }

        [Fact]
        public void The_minimum_separation_is_the_send_interval_times_the_clients_0_95_factor()
        {
            // ValidateControlPoints drops a pair closer than desiredInterval*0.95.
            Assert.Equal(ShipMotionPolicy.SendIntervalSeconds * 0.95, ShipMotionPolicy.MinSeparationSeconds, 12);
            Assert.Equal(0.228, ShipMotionPolicy.MinSeparationSeconds, 12);
        }

        [Fact]
        public void The_server_boundary_rejection_time_matches_the_client_config()
        {
            // ShipConfiguration.ServerBoundaryRejectionTime = 0.5.
            Assert.Equal(0.5, ShipMotionPolicy.ServerBoundaryRejectionSeconds);
        }

        [Fact]
        public void Emitting_at_the_send_interval_clears_the_reject_floor_with_headroom()
        {
            // A pair exactly one step apart must be accepted, and by a margin -
            // 0.24 vs the 0.228 floor is the 12 ms that absorbs loop jitter.
            Assert.True(ShipMotionPolicy.SendIntervalSeconds > ShipMotionPolicy.MinSeparationSeconds);
        }

        // ------------------------------------------------------------------
        // The ideal-grid timeline
        // ------------------------------------------------------------------

        [Fact]
        public void Timestamps_advance_by_exactly_one_step_per_sample()
        {
            long anchor = 1_000_000;
            long t0 = ShipMotionPolicy.TimestampMsFor(anchor, 0, ShipMotionPolicy.SendIntervalSeconds);
            long t1 = ShipMotionPolicy.TimestampMsFor(anchor, 1, ShipMotionPolicy.SendIntervalSeconds);
            long t2 = ShipMotionPolicy.TimestampMsFor(anchor, 2, ShipMotionPolicy.SendIntervalSeconds);

            Assert.Equal(anchor, t0);
            Assert.Equal(anchor + 240, t1);
            Assert.Equal(anchor + 480, t2);
        }

        [Fact]
        public void Every_consecutive_pair_on_the_grid_is_a_legal_separation()
        {
            // The whole reason the timeline is ideal-grid: no emit can ever land
            // inside the reject window, for a long flight's worth of samples.
            long anchor = 42;
            long previous = ShipMotionPolicy.TimestampMsFor(anchor, 0, ShipMotionPolicy.SendIntervalSeconds);
            for (long i = 1; i < 5000; i++)
            {
                long ms = ShipMotionPolicy.TimestampMsFor(anchor, i, ShipMotionPolicy.SendIntervalSeconds);
                Assert.True(ShipMotionPolicy.IsLegalSeparation(previous, ms),
                    "pair " + previous + "->" + ms + " must be legal");
                previous = ms;
            }
        }

        [Fact]
        public void A_regressing_or_too_close_pair_is_illegal()
        {
            Assert.False(ShipMotionPolicy.IsLegalSeparation(1000, 900));   // regression
            Assert.False(ShipMotionPolicy.IsLegalSeparation(1000, 1000));  // duplicate
            Assert.False(ShipMotionPolicy.IsLegalSeparation(1000, 1200));  // 0.2 s < 0.228 s
            Assert.True(ShipMotionPolicy.IsLegalSeparation(1000, 1228));   // exactly the floor
            Assert.True(ShipMotionPolicy.IsLegalSeparation(1000, 1240));   // one step
        }

        // ------------------------------------------------------------------
        // The speed env parse
        // ------------------------------------------------------------------

        [Fact]
        public void Unset_or_garbage_speed_falls_back_to_the_default()
        {
            Assert.Equal(ShipMotionPolicy.DefaultSpeedMetresPerSecond, ShipMotionPolicy.SpeedFrom(null));
            Assert.Equal(ShipMotionPolicy.DefaultSpeedMetresPerSecond, ShipMotionPolicy.SpeedFrom(""));
            Assert.Equal(ShipMotionPolicy.DefaultSpeedMetresPerSecond, ShipMotionPolicy.SpeedFrom("fast"));
            Assert.Equal(ShipMotionPolicy.DefaultSpeedMetresPerSecond, ShipMotionPolicy.SpeedFrom("NaN"));
            Assert.Equal(ShipMotionPolicy.DefaultSpeedMetresPerSecond, ShipMotionPolicy.SpeedFrom("0"));
            Assert.Equal(ShipMotionPolicy.DefaultSpeedMetresPerSecond, ShipMotionPolicy.SpeedFrom("-5"));
        }

        [Fact]
        public void A_valid_speed_parses_and_is_clamped()
        {
            Assert.Equal(20.0, ShipMotionPolicy.SpeedFrom("20"));
            Assert.Equal(7.5, ShipMotionPolicy.SpeedFrom("7.5"));
            Assert.Equal(ShipMotionPolicy.MaxSpeedMetresPerSecond, ShipMotionPolicy.SpeedFrom("99999"));
            Assert.Equal(ShipMotionPolicy.MinSpeedMetresPerSecond, ShipMotionPolicy.SpeedFrom("0.01"));
        }
    }
}
