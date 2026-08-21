using System;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Simulation
{
    /// <summary>The flag and the cadence, which together are the whole of "it only observes, rarely".</summary>
    public class SimulationObserverPolicyTests
    {
        [Fact]
        public void The_flag_is_the_documented_one() =>
            Assert.Equal("WAREBORN_SIMULATION_MODEL", SimulationObserverPolicy.EnabledEnvVar);

        [Fact]
        public void Only_the_exact_string_one_enables_the_observer() =>
            Assert.True(SimulationObserverPolicy.IsEnabled("1"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("0")]
        [InlineData("true")]
        [InlineData("TRUE")]
        [InlineData("yes")]
        [InlineData("on")]
        [InlineData(" 1")]
        [InlineData("1 ")]
        [InlineData("01")]
        [InlineData("2")]
        public void Everything_else_leaves_it_off(string? value) =>
            Assert.False(SimulationObserverPolicy.IsEnabled(value));

        [Fact]
        public void The_refresh_cadence_is_inside_the_five_to_ten_second_window()
        {
            Assert.InRange(SimulationObserverPolicy.RefreshInterval,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void A_refresh_is_due_only_once_the_whole_interval_has_passed()
        {
            TimeSpan interval = TimeSpan.FromSeconds(5);
            Assert.False(SimulationObserverPolicy.ShouldRefresh(TimeSpan.Zero, interval));
            Assert.False(SimulationObserverPolicy.ShouldRefresh(TimeSpan.FromMilliseconds(4999), interval));
            Assert.True(SimulationObserverPolicy.ShouldRefresh(interval, interval));
            Assert.True(SimulationObserverPolicy.ShouldRefresh(TimeSpan.FromMinutes(1), interval));
        }
    }
}
