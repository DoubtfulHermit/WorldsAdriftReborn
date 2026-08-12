using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The shared world-time epoch. The property that matters is that two clients
    /// checking out at different server uptimes are handed the SAME advancing
    /// timeline, so they end up in phase instead of frozen minutes apart at the
    /// old constant seed.
    /// </summary>
    public class WorldClockTests
    {
        [Fact]
        public void At_boot_the_seed_is_the_unchanged_baseline()
        {
            // elapsed 0 => the old constant seed exactly (days=1, time=0.15),
            // so the first checkout after boot is unchanged.
            WorldTime t = WorldClock.Current(0.0);
            Assert.Equal(WorldClock.EpochDays, t.Days);
            Assert.Equal(WorldClock.EpochDayTime, t.DayTime, 5);
        }

        [Fact]
        public void Half_a_day_of_uptime_advances_the_time_of_day_by_half()
        {
            // 43200 real seconds at rate 1 = half a day-fraction.
            WorldTime t = WorldClock.Current(WorldClock.SecondsPerDay / 2.0);
            Assert.Equal(1, t.Days);
            Assert.Equal(0.65f, t.DayTime, 4); // 0.15 + 0.5
        }

        [Fact]
        public void A_full_day_of_uptime_rolls_the_day_and_keeps_the_time_of_day()
        {
            WorldTime t = WorldClock.Current(WorldClock.SecondsPerDay);
            Assert.Equal(2, t.Days);                       // 1 -> 2
            Assert.Equal(WorldClock.EpochDayTime, t.DayTime, 4); // back to 0.15
        }

        [Fact]
        public void Crossing_midnight_wraps_the_fraction_and_bumps_the_day()
        {
            // epoch dayTime 0.9, advance 0.2 of a day => 1.1 => day+1, time 0.1.
            WorldTime t = WorldClock.Advance(epochDays: 1, epochDayTime: 0.9f, timeRate: 1f,
                elapsedSeconds: WorldClock.SecondsPerDay * 0.2);
            Assert.Equal(2, t.Days);
            Assert.Equal(0.1f, t.DayTime, 3);
        }

        [Fact]
        public void The_time_rate_scales_the_advance()
        {
            // At rate 2, half a day of real time advances a whole day-fraction.
            WorldTime t = WorldClock.Advance(epochDays: 1, epochDayTime: 0.15f, timeRate: 2f,
                elapsedSeconds: WorldClock.SecondsPerDay / 2.0);
            Assert.Equal(2, t.Days);
            Assert.Equal(0.15f, t.DayTime, 4);
        }

        [Fact]
        public void Two_clients_joining_at_different_uptimes_land_in_phase()
        {
            // THE FIX, stated as an equation. Client A checks out at uptime E and
            // free-runs for D more seconds; client B checks out at uptime E+D. They
            // must be showing the SAME world time at that instant - which is exactly
            // "advancing A's seed by D" == "B's seed". Under the old constant seed
            // both got 0.15 and were D seconds out of phase forever.
            double e = 1234.0;   // A's join uptime
            double d = 5678.0;   // real seconds later that B joins

            WorldTime a = WorldClock.Current(e);
            // A has been free-running for d seconds from its seed:
            WorldTime aNow = WorldClock.Advance(a.Days, a.DayTime, WorldClock.TimeRate, d);
            // B checks out now, at uptime e+d:
            WorldTime b = WorldClock.Current(e + d);

            Assert.Equal(aNow.Days, b.Days);
            Assert.Equal(aNow.DayTime, b.DayTime, 4);
        }

        [Fact]
        public void Advance_is_deterministic_for_the_same_inputs()
        {
            WorldTime x = WorldClock.Current(9999.5);
            WorldTime y = WorldClock.Current(9999.5);
            Assert.Equal(x.Days, y.Days);
            Assert.Equal(x.DayTime, y.DayTime, 6);
        }
    }
}
